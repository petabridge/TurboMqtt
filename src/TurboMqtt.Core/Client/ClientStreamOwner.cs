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
using TurboMqtt.Core.IO;
using TurboMqtt.Core.PacketTypes;
using TurboMqtt.Core.Protocol;
using TurboMqtt.Core.Protocol.Pub;
using TurboMqtt.Core.Streams;

namespace TurboMqtt.Core.Client;

/// <summary>
/// Actor responsible for owning a client's streams and child actors.
/// </summary>
internal sealed class ClientStreamOwner : UntypedActor
{
    /// <summary>
    /// Performs a graceful disconnect of the client.
    /// </summary>
    public sealed record DoDisconnect(CancellationToken CancellationToken);
    
    public sealed class DisconnectComplete
    {
        public static readonly DisconnectComplete Instance = new();
        private DisconnectComplete() { }
    }
    
    public sealed class ServerDisconnect
    {
        public ServerDisconnect(DisconnectReasonCode reason)
        {
            Reason = reason;
        }

        public DisconnectReasonCode Reason { get; }
    }
    
    /// <summary>
    /// Used to create a new client.
    /// </summary>
    /// <param name="TransportManager">Creates the transport we're going to use to communicate with the broker.</param>
    public sealed record CreateClient(IMqttTransportManager TransportManager, MqttClientConnectOptions ConnectOptions)
        : INoSerializationVerificationNeeded;

    private IActorRef? _exactlyOnceActor;
    private IActorRef? _atLeastOnceActor;
    private IActorRef? _clientAckActor;
    private IActorRef? _heartBeatActor;
    private IInternalMqttClient? _client;
    private IMqttTransportManager? _transportManager;
    private IMqttTransport? _currentTransport;
    private Channel<MqttPacket>? _outboundChannel;
    private Channel<MqttMessage>? _inboundChannel;
    private readonly TaskCompletionSource<DisconnectReasonCode> _trueDeath = new();

    private readonly IMaterializer _materializer = Context.Materializer();
    private readonly ILoggingAdapter _log = Context.GetLogger();
    
    /* Data we need for automatic reconnects */
    private MqttClientConnectOptions? _connectOptions;
    private Dictionary<string, TopicSubscription> _savedSubscriptions = new();
    private int _remainingReconnectAttempts = 3;

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case CreateClient createClient when _client is null:
            {
                _transportManager = createClient.TransportManager;

                var clientConnectOptions = createClient.ConnectOptions;
                _connectOptions = clientConnectOptions;
                _remainingReconnectAttempts = clientConnectOptions.MaxReconnectAttempts;

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

                // create the transport (this is a blocking call)
                _currentTransport = _transportManager.CreateTransportAsync().GetAwaiter().GetResult();

                PrepareStream(clientConnectOptions, _currentTransport, outboundPackets, requiredActors, outboundPacketsReader);

                _client = new MqttClient(_currentTransport,
                    Self,
                    requiredActors,
                    _inboundChannel.Reader,
                    outboundPackets, _log, clientConnectOptions, _trueDeath.Task);

                // client is now fully constructed
                Sender.Tell(_client);
                Become(Running);
                break;
            }

            default:
                Unhandled(message);
                break;
        }
    }

    private void PrepareStream(MqttClientConnectOptions clientConnectOptions, 
        IMqttTransport transport,
        ChannelWriter<MqttPacket> outboundPackets,
        MqttRequiredActors requiredActors, 
        ChannelReader<MqttPacket> outboundPacketsReader)
    {
        var (inboundStream, outboundStream) = 
            ConfigureMqttStreams(clientConnectOptions, transport,
                outboundPackets, requiredActors, transport.MaxFrameSize);

        // begin outbound stream
        ChannelSource.FromReader(outboundPacketsReader)
            .To(outboundStream)
            .Run(_materializer);

        // check for streams termination
        var closureSelf = Self;
        var watchTermination = Flow.Create<MqttMessage>()
            .WatchTermination((_, done) =>
            {
                done.ContinueWith(t =>
                {
                    // TODO: replace this with recreating the transport
                    _log.Warning("Transport was terminated by broker. Shutting down client.");
                    transport.AbortAsync();
                    closureSelf.Tell(new ServerDisconnect(DisconnectReasonCode.NormalDisconnection));
                }, TaskContinuationOptions.ExecuteSynchronously);
                return NotUsed.Instance;
            });

        // begin inbound stream
        inboundStream
            .Via(watchTermination)
            // setting IsOwner to false here is crucial - otherwise, we can't reboot the client after broker disconnects
            .To(ChannelSink.FromWriter(_inboundChannel!.Writer, false))
            .Run(_materializer);
    }

    /// <summary>
    /// State that we enter after the client has launched.
    /// </summary>
    private void Running(object message)
    {
        switch (message)
        {
            /* Memorization methods - need this data for reconnects */
            case SubscribePacket subscribePacket:
            {
                foreach(var s in subscribePacket.Topics)
                    _savedSubscriptions[s.Topic] = s;
                break;
            }
            case UnsubscribePacket unsubscribePacket:
            {
                foreach(var s in unsubscribePacket.Topics)
                    _savedSubscriptions.Remove(s);
                break;
            }
            
            /* Connection handling methods */
            
            case ServerDisconnect serverDisconnect when _remainingReconnectAttempts > 0:
            {
                _log.Info("Server disconnected the client. Reason: {0}", serverDisconnect.Reason);
                _ = _currentTransport?.AbortAsync(); // have to force old resources to close
                _currentTransport = null; // null out the old transport
                _remainingReconnectAttempts--;

                // TODO: determine if this is an irrecoverable broker error
                // if it is, we need to shut down the client
                // if it's not, we need to reconnect
                
                // time to recreate the transport
                _currentTransport = _transportManager!.CreateTransportAsync().GetAwaiter().GetResult();
                
                // swap transports
                _client!.SwapTransport(_currentTransport);
                
                var requiredActors = new MqttRequiredActors(_exactlyOnceActor!, _atLeastOnceActor!, _clientAckActor!,
                    _heartBeatActor!);
                
                // need to reconnect the streams
                PrepareStream(_connectOptions!, _currentTransport, _outboundChannel!.Writer, requiredActors, _outboundChannel!.Reader);

                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                _ = DoReconnect(cts.Token);
                
                break;

                // reconnect the client using the client with the new transport
                async Task DoReconnect(CancellationToken ct)
                {
                    var self = Self;
                    try
                    {
                        var resp =await _client!.ConnectAsync(ct);
                        if (!resp.IsSuccess)
                        {
                            _log.Warning("Failed to reconnect client. Reason: {0}", resp.Reason);
                            self.Tell(new ServerDisconnect(DisconnectReasonCode.UnspecifiedError));
                        }
                    
                        // for each of our subscriptions, we need to resubscribe
                        var subscribeResp = await _client.SubscribeAsync(_savedSubscriptions.Values.ToArray(), ct);
                        if (!subscribeResp.IsSuccess)
                        {
                            _log.Warning("Failed to resubscribe to topics. Reason: {0}", subscribeResp.Reason);
                            self.Tell(new ServerDisconnect(DisconnectReasonCode.UnspecifiedError));
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _log.Warning("Reconnect operation timed out. Aborting transport.");
                        // ReSharper disable once MethodSupportsCancellation
                        _ = _currentTransport!.AbortAsync();
                    }
                }
            }
            
            case ServerDisconnect serverDisconnect when _remainingReconnectAttempts == 0:
            {
                _log.Info("Server disconnected the client. Reason: {0}", serverDisconnect.Reason);
                _log.Info("Client has exhausted all reconnect attempts. Shutting down.");
                Context.Stop(Self);
                
                
                _trueDeath.TrySetResult(DisconnectReasonCode.UnspecifiedError);
                break;
            }
            
            case DoDisconnect doDisconnect: // explicit disconnect - no coming back from this
            {
                _log.Info("Disconnecting client...");

                _ = ExecDisconnect();
                
                break;

                async Task ExecDisconnect()
                {
                    var sender = Sender;
                    var self = Self;
                    try
                    {
                        await _currentTransport!.CloseAsync(doDisconnect.CancellationToken);
                    }
                    catch (OperationCanceledException) // cts timed out
                    {
                        _log.Warning("Disconnect operation timed out. Aborting transport.");
                        await _currentTransport!.AbortAsync();
                    }
                    finally
                    {
                        sender.Tell(DisconnectComplete.Instance);
                        self.Tell(PoisonPill.Instance); // shut ourselves down
                    }
                    
                }
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

    protected override void PostStop()
    {
        // force both channels to complete - this will shut down the streams and the transport
        _outboundChannel?.Writer.TryComplete();
        _inboundChannel?.Writer.TryComplete();
        _trueDeath.TrySetResult(DisconnectReasonCode.NormalDisconnection);
    }
}