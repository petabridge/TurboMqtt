// -----------------------------------------------------------------------
// <copyright file="ClientStreamOwner.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Diagnostics;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;
using TurboMqtt.IO;
using TurboMqtt.IO.Tcp;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;
using TurboMqtt.Protocol.Pub;
using TurboMqtt.Streams;
using TurboMqtt.Telemetry;

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
    /// Sent before the DISCONNECT packet is written to the outbound channel.
    /// Sets <see cref="_userDisconnectRequested"/> so that the broker's DISCONNECT
    /// response (arriving as <see cref="ServerDisconnect"/>) is suppressed rather
    /// than triggering a reconnect attempt.
    /// </summary>
    public sealed class PrepareDisconnect : IClientStreamOwnerMessage
    {
        public static readonly PrepareDisconnect Instance = new();
        private PrepareDisconnect() { }
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
        public ServerDisconnect(DisconnectPacket disconnectPacket)
        {
            DisconnectPacket = disconnectPacket;
        }

        public DisconnectReasonCode Reason => DisconnectPacket.ReasonCode ?? DisconnectReasonCode.NormalDisconnection;

        public DisconnectPacket DisconnectPacket { get; }
    }

    private sealed class StreamTerminated : IClientStreamOwnerMessage
    {
        private StreamTerminated()
        {
        }

        public static readonly StreamTerminated Instance = new();
    }

    /// <summary>
    /// Self-tell: reconnect completed successfully.
    /// </summary>
    private sealed class ReconnectSuccess : IClientStreamOwnerMessage
    {
        private ReconnectSuccess()
        {
        }

        public static readonly ReconnectSuccess Instance = new();
    }

    /// <summary>
    /// Self-tell: reconnect failed.
    /// </summary>
    private sealed record ReconnectFailed(string Reason) : IClientStreamOwnerMessage;

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
    private readonly IActorRef _closureSelf = Context.Self;

    /* Data we need for automatic reconnects */
    private MqttClientConnectOptions? _connectOptions;
    private Dictionary<string, TopicSubscription> _savedSubscriptions = new();
    private int _remainingReconnectAttempts = 3;
    private int _streamOperatorId = 0;
    private bool _successfullyConnected = false;
    private bool _userDisconnectRequested = false;
    private CancellationTokenSource? _reconnectCts;

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
                            Props.Create(() => new ClientAcksActor(clientConnectOptions.PublishRetryInterval,
                                outboundPackets)),
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
                _remainingReconnectAttempts = _connectOptions!.MaxReconnectAttempts; // reset the connect attempts
                _successfullyConnected = true;
                break;
            }

            /* Connection handling methods */
            case TransportFailedToConnect:
            {
                var sender = Sender;
                RunTask(async () =>
                {
                    _log.Debug(
                        "Client failed to connect to the server. Replacing transport in order to allow reconnect.");
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
            case ServerDisconnect when _userDisconnectRequested:
            {
                // ignore - this is a synthetic disconnect packet from our own transport
                // shutdown sequence, not a genuine broker-initiated disconnect
                _log.Debug("Ignoring ServerDisconnect during user-initiated disconnect.");
                break;
            }
            case ServerDisconnect serverDisconnect when _remainingReconnectAttempts > 0:
            {
                _log.Info("Server disconnected the client. Reason: {0} ReasonString: {1}",
                    serverDisconnect.Reason, serverDisconnect.DisconnectPacket.ReasonString ?? "(none)");
                EmitServerDisconnectActivity(serverDisconnect);
                _ = _currentTransport?.AbortAsync(); // have to force old resources to close
                _currentTransport = null; // null out the old transport
                Context.Stop(_streamInstanceOwner); // wait for the stream to terminate
                break;
            }

            case ServerDisconnect serverDisconnect when _remainingReconnectAttempts == 0:
            {
                _log.Info("Server disconnected the client. Reason: {0} ReasonString: {1}",
                    serverDisconnect.Reason, serverDisconnect.DisconnectPacket.ReasonString ?? "(none)");
                _log.Info("Client has exhausted all reconnect attempts. Shutting down.");
                EmitServerDisconnectActivity(serverDisconnect);
                Self.Tell(PoisonPill.Instance);
                break;
            }

            // old stream is dead, time to create a new one — enter Reconnecting behavior
            case StreamTerminated when _userDisconnectRequested:
            {
                // ignore - stream terminated as part of user-initiated disconnect;
                // PoisonPill from DoDisconnect handler will clean up
                _log.Debug("Ignoring StreamTerminated during user-initiated disconnect.");
                break;
            }
            case StreamTerminated when _successfullyConnected:
            {
                if (_remainingReconnectAttempts <= 0)
                    return; // ignore

                _remainingReconnectAttempts--;
                _log.Info("Stream terminated. Entering Reconnecting state. Remaining attempts: {0}", _remainingReconnectAttempts);
                Become(Reconnecting);
                BeginReconnect();
                break;
            }

            case PrepareDisconnect:
            {
                _log.Debug("Preparing for user-initiated disconnect.");
                _userDisconnectRequested = true;
                break;
            }

            case DoDisconnect doDisconnect: // explicit disconnect - no coming back from this
            {
                _log.Info("Disconnecting client...");
                _userDisconnectRequested = true; // ensure flag is set even if PrepareDisconnect was not sent

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
                Self.Tell(PoisonPill.Instance);
                break;
            }
            default:
                Unhandled(message);
                break;
        }
    }

    /// <summary>
    /// Message-driven reconnection state.
    /// Waits for <see cref="ReconnectSuccess"/> or <see cref="ReconnectFailed"/> self-tells.
    /// </summary>
    private void Reconnecting(object message)
    {
        switch (message)
        {
            case ReconnectSuccess:
            {
                _log.Info("Reconnect succeeded. Returning to Running state.");
                _closureSelf.Tell(TransportConnectedSuccessfully.Instance);
                Become(Running);
                break;
            }
            case ReconnectFailed failed:
            {
                _log.Warning("Reconnect failed: {0}", failed.Reason);
                if (_remainingReconnectAttempts > 0)
                {
                    _remainingReconnectAttempts--;
                    _log.Info("Retrying reconnect. Remaining attempts: {0}", _remainingReconnectAttempts);
                    BeginReconnect();
                }
                else
                {
                    _log.Info("Client has exhausted all reconnect attempts. Shutting down.");
                    Self.Tell(PoisonPill.Instance);
                }
                break;
            }
            case TransportFailedToConnect:
            {
                // Respond to unblock MqttClient.ConnectAsync's Ask<TransportResetComplete>
                Sender.Tell(TransportResetComplete.Instance);
                break;
            }
            case TransportConnectedSuccessfully:
            {
                // Sent by MqttClient.ConnectAsync during reconnect — ignore here,
                // we send our own after ReconnectSuccess
                break;
            }
            case ServerDisconnect:
            {
                // Transport died during reconnect — cancel the in-flight ConnectAsync
                // so it fails immediately instead of waiting for the full CTS timeout.
                _log.Debug("Cancelling reconnect due to ServerDisconnect.");
                _reconnectCts?.Cancel();
                break;
            }
            case StreamTerminated:
                _log.Debug("Ignoring StreamTerminated in Reconnecting state.");
                break;
            case DoDisconnect:
            {
                _log.Info("Received disconnect request while reconnecting. Shutting down.");
                Sender.Tell(DisconnectComplete.Instance);
                Self.Tell(PoisonPill.Instance);
                break;
            }
            case Terminated t:
            {
                _log.Error(
                    "One of the required actors [{0}] has terminated during reconnect. Shutting down the client.",
                    t.ActorRef);
                Self.Tell(PoisonPill.Instance);
                break;
            }
            default:
                Unhandled(message);
                break;
        }
    }

    private void EmitServerDisconnectActivity(ServerDisconnect serverDisconnect)
    {
        using var activity = OpenTelemetrySupport.ActivitySource.StartActivity("mqtt.server_disconnect",
            ActivityKind.Client);
        if (activity is null)
            return;

        activity.SetTag(OpenTelemetrySupport.ClientIdTag, _connectOptions?.ClientId);
        activity.SetTag("disconnect.reason_code", serverDisconnect.Reason.ToString());
        var reasonString = serverDisconnect.DisconnectPacket.ReasonString;
        if (!string.IsNullOrEmpty(reasonString))
            activity.SetTag("disconnect.reason_string", reasonString);
    }

    private async Task ReplaceTransport()
    {
        CreateStreamInstanceOwner();

        // time to recreate the transport
        _currentTransport = await _transportManager!.CreateTransportAsync();

        // swap transports
        _client!.SwapTransport(_currentTransport);

        // Reset the ack actor connection state
        _clientAckActor!.Tell(ClientAcksActor.Reconnect.Instance);
    }

    /// <summary>
    /// Starts a reconnect attempt. Creates a new CTS, cleans up old resources,
    /// sets up new transport/stream, and launches ConnectAsync on the thread pool.
    /// </summary>
    private void BeginReconnect()
    {
        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        _reconnectCts = new CancellationTokenSource(_connectOptions!.ReconnectTimeout);
        var reconnectToken = _reconnectCts.Token;

        RunTask(async () =>
        {
            // Clean up old stream instance (may already be dead, that's fine)
            if (_streamInstanceOwner != null)
            {
                Context.Unwatch(_streamInstanceOwner);
                Context.Stop(_streamInstanceOwner);
            }

            _ = _currentTransport?.AbortAsync();
            _currentTransport = null;

            await ReplaceTransport();

            var requiredActors = new MqttRequiredActors(_exactlyOnceActor!, _atLeastOnceActor!,
                _clientAckActor!, _heartBeatActor!);

            // the transport should be at rest now — clear out old data
            HashSet<MqttPacket> preservedPackets = new();
            while (_outboundChannel!.Reader.TryRead(out var p))
            {
                if (p.PacketType == MqttPacketType.Disconnect)
                    continue; // don't bother resending disconnect packets
                preservedPackets.Add(p);
            }

            _log.Debug("Preserved {0} packets for retransmission.", preservedPackets.Count);

            // need to reconnect the streams
            var streamCreateResult = await PrepareStreamAsync(_connectOptions!, _currentTransport!,
                _outboundChannel!, _inboundChannel!, requiredActors, Self);

            if (!streamCreateResult.IsSuccess)
            {
                _closureSelf.Tell(new ReconnectFailed($"Failed to recreate stream. Reason: {streamCreateResult.ReasonString}"));
                return;
            }

            // Phase 2: Run ConnectAsync on the thread pool so the actor can process
            // messages (like TransportFailedToConnect) that ConnectAsync sends back.
            var closureSelf = _closureSelf;
            var client = _client!;
            var savedSubs = _savedSubscriptions;
            var outbound = _outboundChannel!;
            _ = Task.Run(async () =>
            {
                try
                {
                    var resp = await client.ConnectAsync(reconnectToken);
                    if (!resp.IsSuccess)
                    {
                        closureSelf.Tell(new ReconnectFailed($"Failed to reconnect. Reason: {resp.Reason}"));
                        return;
                    }

                    // resubscribe
                    if (savedSubs.Count > 0)
                    {
                        var subscribeResp = await client.SubscribeAsync(savedSubs.Values.ToArray(), reconnectToken);
                        if (!subscribeResp.IsSuccess)
                        {
                            _log.Warning("Failed to resubscribe to {0} topic(s) during reconnect. Reason: {1}",
                                savedSubs.Count, subscribeResp.Reason);
                        }
                    }

                    // requeue preserved packets
                    foreach (var pk in preservedPackets)
                        outbound.Writer.TryWrite(pk);

                    closureSelf.Tell(ReconnectSuccess.Instance);
                }
                catch (OperationCanceledException)
                {
                    closureSelf.Tell(new ReconnectFailed("Reconnect operation timed out or was cancelled."));
                }
            });
        });
    }

    protected override void PostStop()
    {
        // Cancel and dispose reconnect CTS
        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();

        // Deterministic shutdown ordering:
        // 1. Complete outbound channel writer — stops new data entering stream
        _outboundChannel?.Writer.TryComplete();

        // 2. Abort transport — cancels background tasks, disposes stream
        _currentTransport?.AbortAsync();

        // 3. Complete inbound channel writer — terminates consumer
        _inboundChannel?.Writer.TryComplete();

        // 4. Signal _trueDeath — unblocks WhenTerminated
        _trueDeath.TrySetResult(DisconnectReasonCode.NormalDisconnection);

        // 5. Tell parent that we're dead — front-runs DeathWatch to avoid
        //    "client already exists" race if someone immediately recreates.
        Context.Parent.Tell(new ClientManagerActor.ClientDied(_connectOptions!.ClientId));
    }
}