// -----------------------------------------------------------------------
// <copyright file="ClientStreamOwner.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;
using TurboMqtt.IO;
using TurboMqtt.IO.Tcp;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;
using TurboMqtt.Protocol.Pub;
using TurboMqtt.Streams;

namespace TurboMqtt.Client;

/// <summary>
/// Actor responsible for owning a client's streams and child actors.
/// </summary>
internal sealed class ClientStreamOwner : UntypedActor
{
    /// <summary>
    /// Marker interface for all messages that can be sent to the <see cref="ClientStreamOwner"/>
    /// </summary>
    public interface IClientStreamOwnerMessage : IDeadLetterSuppression
    {
    }

    /// <summary>
    /// Performs a graceful disconnect of the client.
    /// </summary>
    public sealed record DoDisconnect(CancellationToken CancellationToken) : IClientStreamOwnerMessage;

    public sealed class DisconnectComplete : IClientStreamOwnerMessage
    {
        public static readonly DisconnectComplete Instance = new();

        private DisconnectComplete()
        {
        }
    }

    public sealed class ServerDisconnect : IClientStreamOwnerMessage
    {
        public ServerDisconnect(DisconnectReasonCode reason)
        {
            Reason = reason;
        }

        public DisconnectReasonCode Reason { get; }
    }

    private sealed class StreamTerminated : IClientStreamOwnerMessage
    {
        private StreamTerminated()
        {
        }

        public static readonly StreamTerminated Instance = new();
    }

    public sealed class TransportConnectedSuccessfully : IClientStreamOwnerMessage
    {
        private TransportConnectedSuccessfully()
        {
        }

        public static readonly TransportConnectedSuccessfully Instance = new();
    }

    public sealed class TransportFailedToConnect : IClientStreamOwnerMessage
    {
        private TransportFailedToConnect()
        {
        }

        public static readonly TransportFailedToConnect Instance = new();
    }

    /// <summary>
    /// We've recreated the transport - ready to allow client to attempt to reconnect.
    /// </summary>
    public sealed class TransportResetComplete : IClientStreamOwnerMessage
    {
        private TransportResetComplete()
        {
        }

        public static readonly TransportResetComplete Instance = new();
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
    private IActorRef? _streamInstanceOwner;
    private IInternalMqttClient? _client;
    private IMqttTransportManager? _transportManager;
    private IMqttTransport? _currentTransport;
    private Channel<MqttPacket>? _outboundChannel;
    private Channel<MqttMessage>? _inboundChannel;
    private readonly TaskCompletionSource<DisconnectReasonCode> _trueDeath = new();

    private readonly ILoggingAdapter _log = Context.GetLogger();

    /* Data we need for automatic reconnects */
    private MqttClientConnectOptions? _connectOptions;
    private Dictionary<string, TopicSubscription> _savedSubscriptions = new();
    private int _remainingReconnectAttempts = 3;
    private int _streamOperatorId = 0;
    private bool _successfullyConnected = false;

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case CreateClient createClient when _client is null:
            {
                var sender = Sender;
                RunTask(async () =>
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

                    _atLeastOnceActor = Context.ActorOf(Props.Create(() => new AtLeastOncePublishRetryActor(
                        outboundPackets,
                        clientConnectOptions.MaxPublishRetries, clientConnectOptions.PublishRetryInterval)), "qos-1");
                    Context.Watch(_atLeastOnceActor);

                    _clientAckActor =
                        Context.ActorOf(
                            Props.Create(() => new ClientAcksActor(clientConnectOptions.PublishRetryInterval)),
                            "acks");
                    Context.Watch(_clientAckActor);

                    var heartBeat = new FailureDetector(TimeSpan.FromSeconds(clientConnectOptions.KeepAliveSeconds),
                        Self);
                    _heartBeatActor = Context.ActorOf(
                        Props.Create(() => new HeartBeatActor(outboundPackets, heartBeat)),
                        "heartbeat");
                    Context.Watch(_heartBeatActor);

                    // prepare the streams
                    var requiredActors = new MqttRequiredActors(_exactlyOnceActor, _atLeastOnceActor, _clientAckActor,
                        _heartBeatActor);

                    CreateStreamInstanceOwner();

                    // create the transport (this is a blocking call)
                    _currentTransport = await _transportManager.CreateTransportAsync();

                    var streamCreateResult = await PrepareStreamAsync(clientConnectOptions, _currentTransport,
                        _outboundChannel, _inboundChannel,
                        requiredActors, Self);

                    if (!streamCreateResult.IsSuccess) // should never happen
                    {
                        var errMsg = $"Failed to create stream. Reason: {streamCreateResult.ReasonString}";
                        _log.Error(errMsg);
                        Sender.Tell(new Status.Failure(new InvalidOperationException(errMsg)));
                        return;
                    }

                    _client = new MqttClient(_currentTransport,
                        Self,
                        requiredActors,
                        _inboundChannel.Reader,
                        outboundPackets, _log, clientConnectOptions, _trueDeath.Task);

                    // client is now fully constructed
                    Sender.Tell(_client);
                    Become(Running);
                });

                break;
            }

            default:
                Unhandled(message);
                break;
        }
    }

    private void CreateStreamInstanceOwner()
    {
        _streamInstanceOwner = Context.ActorOf(Props.Create(() => new ClientStreamInstance()),
            "stream-owner-" + _streamOperatorId++);
        Context.WatchWith(_streamInstanceOwner, StreamTerminated.Instance);
    }

    private async Task<ClientStreamInstance.CreateStreamResult> PrepareStreamAsync(
        MqttClientConnectOptions clientConnectOptions, IMqttTransport currentTransport,
        Channel<MqttPacket> outboundChannel, Channel<MqttMessage> inboundChannel, MqttRequiredActors requiredActors,
        IActorRef self, CancellationToken ct = default)
    {
        var createStream = new ClientStreamInstance.CreateStream(clientConnectOptions, currentTransport,
            outboundChannel, inboundChannel, requiredActors, self);
        return await _streamInstanceOwner!.Ask<ClientStreamInstance.CreateStreamResult>(createStream,
            cancellationToken: ct);
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
                foreach (var s in subscribePacket.Topics)
                    _savedSubscriptions[s.Topic] = s;
                break;
            }
            case UnsubscribePacket unsubscribePacket:
            {
                foreach (var s in unsubscribePacket.Topics)
                    _savedSubscriptions.Remove(s);
                break;
            }

            case TransportConnectedSuccessfully:
            {
                // this is used to determine if we should attempt to reconnect
                _successfullyConnected = true;
                break;
            }

            /* Connection handling methods */
            case TransportFailedToConnect:
            {
                var sender = Sender;
                RunTask(async () =>
                {
                    _log.Debug("Client failed to connect to the server. Replacing transport in order to allow reconnect.");
                    // just to avoid race conditions, unwatch the previous stream instance owner
                    Context.Unwatch(_streamInstanceOwner);
                    
                    _ = _currentTransport?.AbortAsync(); // have to force old resources to close
                    _currentTransport = null; // null out the old transport
                    Context.Stop(_streamInstanceOwner); // terminate previous stream
                    
                    await ReplaceTransport();
                    
                    var requiredActors = new MqttRequiredActors(_exactlyOnceActor!, _atLeastOnceActor!,
                        _clientAckActor!,
                        _heartBeatActor!);

                    // need to reconnect the streams
                    var streamCreateResult = await PrepareStreamAsync(_connectOptions!, _currentTransport!,
                        _outboundChannel!, _inboundChannel!, requiredActors, Self);

                    if (!streamCreateResult.IsSuccess) // should never happen
                    {
                        var errMsg = $"Failed to recreate stream. Reason: {streamCreateResult.ReasonString}";
                        _log.Error(errMsg);
                        Self.Tell(PoisonPill.Instance);
                        return;
                    }
                    
                    sender.Tell(TransportResetComplete.Instance);
                });
                break;
            }
            case ServerDisconnect when !_successfullyConnected:
            {
                // ignore - we haven't even connected yet
                break;
            }
            case ServerDisconnect serverDisconnect when _remainingReconnectAttempts > 0:
            {
                _log.Info("Server disconnected the client. Reason: {0}", serverDisconnect.Reason);
                _ = _currentTransport?.AbortAsync(); // have to force old resources to close
                _currentTransport = null; // null out the old transport
                Context.Stop(_streamInstanceOwner); // wait for the stream to terminate
                break;
            }

            case ServerDisconnect serverDisconnect when _remainingReconnectAttempts == 0:
            {
                _log.Info("Server disconnected the client. Reason: {0}", serverDisconnect.Reason);
                _log.Info("Client has exhausted all reconnect attempts. Shutting down.");
                Context.Stop(Self);
                break;
            }

            // old stream is dead, time to create a new one
            case StreamTerminated when _successfullyConnected:
            {
                if (_remainingReconnectAttempts <= 0)
                    return; // ignore

                RunTask(async () =>
                {
                    _remainingReconnectAttempts--;

                    // TODO: determine if this is an irrecoverable broker error
                    // if it is, we need to shut down the client
                    // if it's not, we need to reconnect

                    await ReplaceTransport();

                    var requiredActors = new MqttRequiredActors(_exactlyOnceActor!, _atLeastOnceActor!,
                        _clientAckActor!,
                        _heartBeatActor!);
                    
                    // the transport should be at rest now, no longer being written to - clear out all the old data\
                    HashSet<MqttPacket> preservedPackets = new();
                    while (_outboundChannel!.Reader.TryRead(out var p))
                    {
                        if(p.PacketType == MqttPacketType.Disconnect)
                            continue; // don't bother resending disconnect packets
                        preservedPackets.Add(p);
                    }
                    
                    _log.Debug("Preserved {0} packets for retransmission.", preservedPackets.Count);
                    
                    // NOTE: inbound channel does not need to be drained - it's a one-way channel

                    // need to reconnect the streams
                    var streamCreateResult = await PrepareStreamAsync(_connectOptions!, _currentTransport!,
                        _outboundChannel!, _inboundChannel!, requiredActors, Self);

                    if (!streamCreateResult.IsSuccess) // should never happen
                    {
                        var errMsg = $"Failed to recreate stream. Reason: {streamCreateResult.ReasonString}";
                        _log.Error(errMsg);
                        Self.Tell(PoisonPill.Instance);
                        return;
                    }

                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    _ = DoReconnect(preservedPackets, cts.Token);
                });

                break;

                // reconnect the client using the client with the new transport
                async Task DoReconnect(HashSet<MqttPacket> preserved, CancellationToken ct)
                {
                    var self = Self;
                    try
                    {
                        var resp = await _client!.ConnectAsync(ct);
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
                        
                        // requeue all the packets that were preserved
                        foreach (var p in preserved)
                            _outboundChannel.Writer.TryWrite(p);
                    }
                    catch (OperationCanceledException)
                    {
                        _log.Warning("Reconnect operation timed out. Aborting transport.");
                        // ReSharper disable once MethodSupportsCancellation
                        _ = _currentTransport!.AbortAsync();
                    }
                }
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

    private async Task ReplaceTransport()
    {
        CreateStreamInstanceOwner();

        // time to recreate the transport
        _currentTransport = await _transportManager!.CreateTransportAsync();

        // swap transports
        _client!.SwapTransport(_currentTransport);
    }

    protected override void PostStop()
    {
        // subtle race condition - if someone immediately tries to recreate a failed client, our parent
        // might yell at them and say "client already exists" - this is because DeathWatch runs slightly
        // behind the _trueDeath task completion even when it runs only in our PostStop routine.
        // Thus, we're going to front-run DeathWatch here and tell our parent that we're dead.
        Context.Parent.Tell(new ClientManagerActor.ClientDied(_connectOptions!.ClientId));

        // force both channels to complete - this will shut down the streams and the transport
        _outboundChannel?.Writer.TryComplete();
        _inboundChannel?.Writer.TryComplete();
        _trueDeath.TrySetResult(DisconnectReasonCode.NormalDisconnection);
    }
}