// -----------------------------------------------------------------------
// <copyright file="ClientStreamInstance.cs" company="Petabridge, LLC">
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
using TurboMqtt.IO;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;
using TurboMqtt.Streams;

namespace TurboMqtt.Client;

/// <summary>
/// Temporary actor that exists to create and destroy all streams
/// </summary>
internal sealed class ClientStreamInstance : UntypedActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IMaterializer _materializer = Context.Materializer();

    public sealed record CreateStream(
        MqttClientConnectOptions ClientConnectOptions,
        IMqttTransport Transport,
        Channel<MqttPacket> OutboundPackets,
        Channel<MqttMessage> InboundPackets,
        MqttRequiredActors RequiredActors,
        IActorRef Notifier) : INoSerializationVerificationNeeded;

    public sealed record CreateStreamResult(bool IsSuccess, string? ReasonString = null)
        : INoSerializationVerificationNeeded;
    
    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case CreateStream createStream:
            {
                var c = PrepareStream(createStream);
                if (c.IsSuccess)
                {
                    Become(AlreadyCreated);
                }
                Sender.Tell(c);
                break;
            }
            default:
                Unhandled(message);
                break;
        }
    }


    private void AlreadyCreated(object message)
    {
        switch (message)
        {
            case CreateStream:
            {
                Sender.Tell(new CreateStreamResult(false, "Stream already exists"));
                break;
            }
            default:
                Unhandled(message);
                break;
        }
    }

    private CreateStreamResult PrepareStream(CreateStream create)
    {
        try
        {
            PrepareStream(create.ClientConnectOptions, create.Transport, create.OutboundPackets, create.InboundPackets,
                create.RequiredActors, create.Notifier);
            return new CreateStreamResult(true);
        }
        catch (Exception ex)
        {
            return new CreateStreamResult(false, ex.Message);
        }
    }
    
    private void PrepareStream(MqttClientConnectOptions clientConnectOptions, 
        IMqttTransport transport,
        Channel<MqttPacket> outboundPackets,
        Channel<MqttMessage> inboundPackets,
        MqttRequiredActors requiredActors, IActorRef notifier)
    {
        var (inboundStream, outboundStream) = 
            ConfigureMqttStreams(clientConnectOptions, transport,
                outboundPackets, requiredActors, transport.MaxFrameSize);

        // begin outbound stream
        ChannelSource.FromReader(outboundPackets.Reader)
            .To(outboundStream)
            .Run(_materializer);

        // check for streams termination
        var watchTermination = Flow.Create<MqttMessage>()
            .WatchTermination((_, done) =>
            {
                done.ContinueWith(t =>
                {
                    // TODO: replace this with recreating the transport
                    _log.Warning("Transport was terminated by broker. Shutting down client.");
                    transport.AbortAsync();
                    notifier.Tell(new ClientStreamOwner.ServerDisconnect(DisconnectReasonCode.NormalDisconnection));
                }, TaskContinuationOptions.ExecuteSynchronously);
                return NotUsed.Instance;
            });

        // begin inbound stream
        inboundStream
            .Via(watchTermination)
            // setting IsOwner to false here is crucial - otherwise, we can't reboot the client after broker disconnects
            .To(ChannelSink.FromWriter(inboundPackets.Writer, false))
            .Run(_materializer);
    }
    
    private static (Source<MqttMessage, NotUsed> inbound, Sink<MqttPacket, NotUsed> outbound) ConfigureMqttStreams(
        MqttClientConnectOptions clientConnectOptions, IMqttTransport transport,
        ChannelWriter<MqttPacket> outboundPackets, MqttRequiredActors requiredActors, int maxFrameSize)
    {
        switch (clientConnectOptions.ProtocolVersion)
        {
            case MqttProtocolVersion.V3_1_1:
            {
                var inboundMessages = MqttClientStreams.Mqtt311InboundMessageSource(
                    clientConnectOptions.ClientId,
                    transport,
                    outboundPackets,
                    requiredActors,
                    clientConnectOptions.MaxRetainedPacketIds, clientConnectOptions.MaxPacketIdRetentionTime,
                    clientConnectOptions.EnableOpenTelemetry);

                var outboundMessages = MqttClientStreams.Mqtt311OutboundPacketSink(
                    clientConnectOptions.ClientId,
                    transport,
                    MemoryPool<byte>.Shared,
                    maxFrameSize, (int)clientConnectOptions.MaximumPacketSize,
                    clientConnectOptions.EnableOpenTelemetry);

                return (inboundMessages, outboundMessages);
            }

            case MqttProtocolVersion.V5_0:
            default:
                throw new NotSupportedException();
        }
    }
}