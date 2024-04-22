﻿// -----------------------------------------------------------------------
// <copyright file="ClientStreamOwner.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.Threading.Channels;
using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboMqtt.Core.Client;
using TurboMqtt.Core.IO;
using TurboMqtt.Core.PacketTypes;
using TurboMqtt.Core.Protocol;
using TurboMqtt.Core.Protocol.Pub;

namespace TurboMqtt.Core.Streams;

/// <summary>
/// Actor responsible for owning a client's streams and child actors.
/// </summary>
internal sealed class ClientStreamOwner : UntypedActor
{
    /// <summary>
    /// Used to create a new client.
    /// </summary>
    /// <param name="Transport">The underlying transport we're going to use to communicate with the broker.</param>
    public sealed record CreateClient(IMqttTransport Transport, MqttClientConnectOptions ConnectOptions)
        : INoSerializationVerificationNeeded;

    private IActorRef? _exactlyOnceActor;
    private IActorRef? _atLeastOnceActor;
    private IActorRef? _clientAckActor;
    private IActorRef? _heartBeatActor;
    private IMqttClient? _client;
    private Channel<MqttPacket>? _outboundChannel;
    private Channel<MqttMessage>? _inboundChannel;

    private readonly IMaterializer _materializer = Context.Materializer();
    private readonly ILoggingAdapter _log = Context.GetLogger();

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case CreateClient createClient when _client is null:
            {
                var clientConnectOptions = createClient.ConnectOptions;

                // outbound channel for packets
                _outboundChannel =
                    Channel.CreateUnbounded<MqttPacket>(new UnboundedChannelOptions()
                        { SingleReader = true, SingleWriter = false });
                var outboundPackets = _outboundChannel.Writer;
                var outboundPacketsReader = _outboundChannel.Reader;
                _inboundChannel =
                    Channel.CreateUnbounded<MqttMessage>(new UnboundedChannelOptions()
                        { SingleWriter = true, SingleReader = true });

                // start the actors
                _exactlyOnceActor =
                    Context.ActorOf(
                        Props.Create(() => new ExactlyOncePublishRetryActor(outboundPackets,
                            clientConnectOptions.MaxPublishRetries, clientConnectOptions.PublishRetryInterval)),
                        "qos-2");
                Context.Watch(_exactlyOnceActor);

                _atLeastOnceActor = Context.ActorOf(Props.Create(() => new AtLeastOncePublishRetryActor(outboundPackets,
                    clientConnectOptions.MaxPublishRetries, clientConnectOptions.PublishRetryInterval)), "qos-1");
                Context.Watch(_atLeastOnceActor);

                _clientAckActor =
                    Context.ActorOf(Props.Create(() => new ClientAcksActor(clientConnectOptions.PublishRetryInterval)),
                        "acks");
                Context.Watch(_clientAckActor);

                var heartBeat = new FailureDetector(TimeSpan.FromSeconds(clientConnectOptions.KeepAliveSeconds));
                _heartBeatActor = Context.ActorOf(Props.Create(() => new HeartBeatActor(outboundPackets, heartBeat)),
                    "heartbeat");
                Context.Watch(_heartBeatActor);

                // prepare the streams
                var requiredActors = new MqttRequiredActors(_exactlyOnceActor, _atLeastOnceActor, _clientAckActor,
                    _heartBeatActor);

                var (inboundStream, outboundStream) = ConfigureMqttStreams(clientConnectOptions, createClient,
                    outboundPackets, requiredActors, createClient.Transport.MaxFrameSize);

                // begin outbound stream
                ChannelSource.FromReader(outboundPacketsReader)
                    .To(outboundStream)
                    .Run(_materializer);

                // begin inbound stream
                inboundStream
                    .To(ChannelSink.FromWriter(_inboundChannel.Writer, true))
                    .Run(_materializer);

                _client = new MqttClient(createClient.Transport,
                    Self,
                    requiredActors,
                    _inboundChannel.Reader,
                    outboundPackets, _log, clientConnectOptions);

                // client is now fully constructed
                Sender.Tell(_client);
                break;
            }

            case CreateClient:
                // Just resend the existing client
                Sender.Tell(_client);
                break;

            case Terminated t:
            {
                _log.Error(
                    "One of the required actors [{0}] has terminated. This is an unexpected and fatal error. Shutting down the client.",
                    t.ActorRef);
                Context.Stop(Self);
                break;
            }
            default:
                Unhandled(message);
                break;
        }
    }

    private static (Source<MqttMessage, NotUsed> inbound, Sink<MqttPacket, NotUsed> outbound) ConfigureMqttStreams(
        MqttClientConnectOptions clientConnectOptions, CreateClient createClient,
        ChannelWriter<MqttPacket> outboundPackets, MqttRequiredActors requiredActors, int maxFrameSize)
    {
        switch (clientConnectOptions.ProtocolVersion)
        {
            case MqttProtocolVersion.V3_1_1:
            {
                var inboundMessages = MqttClientStreams.Mqtt311InboundMessageSource(
                    createClient.ConnectOptions.ClientId,
                    createClient.Transport,
                    outboundPackets,
                    requiredActors,
                    clientConnectOptions.MaxRetainedPacketIds, clientConnectOptions.MaxPacketIdRetentionTime);

                var outboundMessages = MqttClientStreams.Mqtt311OutboundPacketSink(
                    createClient.ConnectOptions.ClientId,
                    createClient.Transport,
                    MemoryPool<byte>.Shared,
                    maxFrameSize, (int)clientConnectOptions.MaximumPacketSize);

                return (inboundMessages, outboundMessages);
            }

            case MqttProtocolVersion.V5_0:
            default:
                throw new NotSupportedException();
        }
    }

    protected override void PostStop()
    {
        // force both channels to complete - this will shut down the streams and the transport
        _outboundChannel?.Writer.TryComplete();
        _inboundChannel?.Writer.TryComplete();
    }
}