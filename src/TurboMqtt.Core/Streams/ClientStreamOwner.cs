// -----------------------------------------------------------------------
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
internal sealed class ClientStreamOwner : UntypedActor, IWithStash
{
    /// <summary>
    /// Used to create a new client.
    /// </summary>
    /// <param name="Transport">The underlying transport we're going to use to communicate with the broker.</param>
    public sealed record CreateClient(IMqttTransport Transport, MqttClientConnectOptions ConnectOptions)
        : INoSerializationVerificationNeeded;
    
    public sealed record CreateClientFailed(string Reason) : INoSerializationVerificationNeeded;

    public sealed class ClientConnected : INoSerializationVerificationNeeded;

    // public sealed record ClientConnectedParts(
    //     MqttRequiredActors RequiredActors,
    //     Source<MqttMessage, NotUsed> InboundMessages,
    //     Sink<MqttPacket, NotUsed> OutboundMessages) : INoSerializationVerificationNeeded;

    private IActorRef? _exactlyOnceActor;
    private IActorRef? _atLeastOnceActor;
    private IActorRef? _clientAckActor;
    private IActorRef? _heartBeatActor;
    private IMqttClient? _client;
    
    private readonly IMaterializer _materializer = ActorMaterializer.Create(Context);
    private readonly ILoggingAdapter _log = Context.GetLogger();

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case CreateClient createClient:
            {
                var clientConnectOptions = createClient.ConnectOptions;

                // outbound channel for packets
                var outboundChannel =
                    Channel.CreateUnbounded<MqttPacket>(new UnboundedChannelOptions() { SingleReader = true });
                var outboundPackets = outboundChannel.Writer;
                var outboundPacketsReader = outboundChannel.Reader;
                var inboundChannel =
                    Channel.CreateUnbounded<MqttMessage>(new UnboundedChannelOptions() { SingleWriter = true });

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
                
                var (inboundStream, outboundStream) = ConfigureMqttStreams(clientConnectOptions, createClient, outboundPackets, requiredActors);

                // begin outbound stream
                ChannelSource.FromReader(outboundPacketsReader)
                    .To(outboundStream)
                    .Run(_materializer);
                
                // begin inbound stream
                inboundStream
                    .To(ChannelSink.FromWriter(inboundChannel.Writer, true))
                    .Run(_materializer);
                
                
                
                break;
            }
        }
    }

    private static (Source<MqttMessage, NotUsed> inbound, Sink<MqttPacket, NotUsed> outbound) ConfigureMqttStreams(MqttClientConnectOptions clientConnectOptions, CreateClient createClient,
        ChannelWriter<MqttPacket> outboundPackets, MqttRequiredActors requiredActors)
    {
        switch (clientConnectOptions.ProtocolVersion)
        {
            case MqttProtocolVersion.V3_1_1:
            {
                var inboundMessages = MqttClientStreams.Mqtt311InboundMessageSource(createClient.Transport, outboundPackets,
                    requiredActors,
                    clientConnectOptions.MaxRetainedPacketIds, clientConnectOptions.MaxPacketIdRetentionTime);
                        
                var outboundMessages = MqttClientStreams.Mqtt311OutboundPacketSink(createClient.Transport, MemoryPool<byte>.Shared,
                    (int)clientConnectOptions.MaximumPacketSize);
                
                return (inboundMessages, outboundMessages);
            }
                        
            case MqttProtocolVersion.V5_0:
                default:
                throw new NotSupportedException();
        }
    }

    private void Connected(object message)
    {
    }

    // Will be populated by Akka.NET at actor startup
    public IStash Stash { get; set; } = null!;
}