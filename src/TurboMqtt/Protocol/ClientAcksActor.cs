// -----------------------------------------------------------------------
// <copyright file="ClientAcksActor.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;
using TurboMqtt.Client;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol.Pub;
using TurboMqtt.Utility;
using static TurboMqtt.Protocol.AckProtocol;

namespace TurboMqtt.Protocol;

/// <summary>
/// This actor handles client-side messages that require an acknowledgment from the broker:
///
/// * <see cref="SubscribePacket"/>
/// * <see cref="UnsubscribePacket"/>
/// * <see cref="ConnectPacket"/>
/// * <see cref="AuthPacket"/> (MQTT 5.0 enhanced authentication)
///
/// <see cref="PingReqPacket"/> and <see cref="PingRespPacket"/> are handled by the <see cref="PingActor"/>.
/// </summary>
internal sealed class ClientAcksActor : UntypedActor, IWithTimers
{
    public sealed class Reconnect
    {
        public static readonly Reconnect Instance = new();
        private Reconnect() { }
    }

    /// <summary>
    /// Sent by MqttClient.ConnectAsync when the connect options include an <see cref="IMqtt5AuthHandler"/>.
    /// Carries both the CONNECT packet and the auth handler to use during the connection sequence.
    /// </summary>
    public sealed record ConnectWithAuthHandler(ConnectPacket Packet, IMqtt5AuthHandler AuthHandler)
        : IDeadLetterSuppression;

    // Internal self-tell messages for async auth challenge handling (PipeToSelf pattern)
    private sealed record AuthChallengeCompleted(AuthPacket Response);
    private sealed record AuthChallengeFailed(string Reason);

    public record struct PendingSubscribe(SubscribePacket Packet, Deadline Deadline, IActorRef Sender);

    public record struct PendingUnsubscribe(UnsubscribePacket Packet, Deadline Deadline, IActorRef Sender);

    /// <summary>
    /// Connect pending state. The Deadline is nullable: <c>null</c> means the connect has no
    /// internal actor-side deadline and relies entirely on the <see cref="Ask"/> CancellationToken
    /// supplied by the caller.
    /// </summary>
    public record struct PendingConnect(ConnectPacket Packet, Deadline? Deadline, IActorRef Sender,
        Mqtt5AuthStateMachine? AuthStateMachine = null);

    /// <summary>
    /// Timeout period used for subscribes and unsubscribes.
    /// Connect operations do NOT use this deadline; they rely on the CancellationToken
    /// passed to the Ask call in ConnectAsync.
    /// </summary>
    private readonly TimeSpan _actionTimeout;

    // pending subscribes, connects, and disconnects
    private readonly Dictionary<NonZeroUInt16, PendingSubscribe> _pendingSubscribes = new();
    private readonly Dictionary<NonZeroUInt16, PendingUnsubscribe> _pendingUnsubscribes = new();
    private PendingConnect? _pendingConnect = null;

    /// <summary>
    /// Persisted auth state machine (survives connect phase; used for re-authentication).
    /// </summary>
    private Mqtt5AuthStateMachine? _authStateMachine;

    /// <summary>
    /// Outbound channel writer; used to send AUTH response packets to the broker.
    /// Only present when the actor was created with enhanced authentication support.
    /// </summary>
    private readonly ChannelWriter<MqttPacket>? _outboundChannel;

    private readonly ILoggingAdapter _log = Context.GetLogger();

    public ClientAcksActor(TimeSpan? actionTimeout = null, ChannelWriter<MqttPacket>? outboundChannel = null)
    {
        _actionTimeout = actionTimeout ?? PublishProtocolDefaults.DefaultPublishTimeout;
        _outboundChannel = outboundChannel;
    }

    protected override void PreStart()
    {
        Timers.StartPeriodicTimer("ack-timeout", PublishProtocolDefaults.CheckTimeout.Instance, TimeSpan.FromSeconds(1));
    }

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case SubscribePacket subscribe:
            {
                // sanity check - we shouldn't be receiving duplicate packets
                if (_pendingSubscribes.ContainsKey(subscribe.PacketId))
                {
                    _log.Warning("Received duplicate subscribe packet with ID [{0}]", subscribe.PacketId);
                    Sender.Tell(new AckProtocol.SubscribeFailure("Duplicate packet ID"));
                    return;
                }

                // we don't send packets to the server first time around - Akka.Streams handles that
                var deadline = Deadline.FromNow(_actionTimeout);
                _pendingSubscribes[subscribe.PacketId] = new PendingSubscribe(subscribe, deadline, Sender);
                break;
            }

            case UnsubscribePacket unsubscribe:
            {
                // sanity check - we shouldn't be receiving duplicate packets
                if (_pendingUnsubscribes.ContainsKey(unsubscribe.PacketId))
                {
                    _log.Warning("Received duplicate unsubscribe packet with ID [{0}]", unsubscribe.PacketId);
                    Sender.Tell(new AckProtocol.UnsubscribeFailure("Duplicate packet ID"));
                    return;
                }

                // we don't send packets to the server first time around - Akka.Streams handles that
                var deadline = Deadline.FromNow(_actionTimeout);
                _pendingUnsubscribes[unsubscribe.PacketId] = new PendingUnsubscribe(unsubscribe, deadline, Sender);
                break;
            }

            case Reconnect:
                if (_log.IsDebugEnabled)
                    _log.Debug("Resetting state for a broker reconnect");
                _pendingConnect = null;
                break;

            case ConnectPacket connect:
            {
                if (_pendingConnect is not null)
                {
                    _log.Warning("Received duplicate connect request");
                    Sender.Tell(new AckProtocol.ConnectFailure("Already connecting to broker"));
                    return;
                }

                // No internal deadline for connect: the caller's CancellationToken on the Ask
                // is the authoritative timeout. Using _actionTimeout (PublishRetryInterval=5s) here
                // races with the test CTS (also ~5s) and causes spurious ConnectFailure("Timeout").
                _pendingConnect = new PendingConnect(connect, null, Sender);
                break;
            }

            case ConnectWithAuthHandler connectWithAuth:
            {
                if (_pendingConnect is not null)
                {
                    _log.Warning("Received duplicate connect request (with auth handler)");
                    Sender.Tell(new AckProtocol.ConnectFailure("Already connecting to broker"));
                    return;
                }

                var stateMachine = new Mqtt5AuthStateMachine(connectWithAuth.AuthHandler);
                _authStateMachine = stateMachine;
                _pendingConnect = new PendingConnect(connectWithAuth.Packet, null, Sender, stateMachine);
                break;
            }

            case SubAckPacket ack:
            {
                if (_pendingSubscribes.Remove(ack.PacketId, out var pending))
                {
                    // check the return code
                    if (ack.ReasonCodes.Any(rc => rc >= MqttSubscribeReasonCode.UnspecifiedError))
                    {
                        pending.Sender.Tell(new AckProtocol.SubscribeFailure(ack));
                        return;
                    }

                    pending.Sender.Tell(new AckProtocol.SubscribeSuccess(ack));
                }
                else
                {
                    // could happen in cases where a client canceled a subscribe that was already received by the server
                    _log.Warning("Received SubAck for unknown packet ID [{0}]", ack.PacketId);
                }
                break;
            }

            case UnsubAckPacket ack:
            {
                if (_pendingUnsubscribes.Remove(ack.PacketId, out var pending))
                {
                    // check the return code (MQTT 5.0)
                    if (ack.ReasonCodes.Any(rc => rc >= MqttUnsubscribeReasonCode.UnspecifiedError))
                    {
                        pending.Sender.Tell(new AckProtocol.UnsubscribeFailure(ack));
                        return;
                    }
                    pending.Sender.Tell(new AckProtocol.UnsubscribeSuccess(ack));
                }
                else
                {
                    // could happen in cases where a client canceled a subscribe that was already received by the server
                    _log.Warning("Received UnsubAck for unknown packet ID [{0}]", ack.PacketId);
                }
                break;
            }

            case ConnAckPacket ack:
            {
                if (_pendingConnect is not null)
                {
                    // check the return code
                    if (ack.ReasonCode > ConnAckReasonCode.Success)
                    {
                        _pendingConnect.Value.AuthStateMachine?.Fail();
                        _pendingConnect.Value.Sender.Tell(new AckProtocol.ConnectFailure(ack.ReasonString ?? ack.ReasonCode.ToString()));
                        _pendingConnect = null;
                        return;
                    }

                    _pendingConnect.Value.AuthStateMachine?.Complete();
                    _pendingConnect.Value.Sender.Tell(new AckProtocol.ConnectSuccess(ack));
                    _pendingConnect = null;
                }
                else
                {
                    _log.Warning("Received ConnAck for unknown connect request");
                }
                break;
            }

            // ── MQTT 5.0 Enhanced Authentication ──────────────────────────────

            case AuthPacket auth when auth.ReasonCode == AuthReasonCode.ContinueAuthentication:
            {
                HandleAuthChallenge(auth);
                break;
            }

            case AuthPacket auth when auth.ReasonCode == AuthReasonCode.ReAuthenticate:
            {
                // Broker sending AUTH(0x19) is not spec-compliant; treat as a challenge continuation.
                HandleAuthChallenge(auth);
                break;
            }

            case AuthPacket auth when auth.ReasonCode == AuthReasonCode.Success:
            {
                // Auth completed successfully via AUTH(0x00) — not CONNACK.
                _log.Debug("MQTT 5.0 AUTH success received.");
                _authStateMachine?.Complete();
                break;
            }

            // Self-tell messages from async challenge PipeToSelf ──────────────

            case AuthChallengeCompleted completed:
            {
                if (_outboundChannel is null)
                {
                    _log.Error("Auth challenge completed but no outbound channel configured — cannot send AUTH response.");
                    FailPendingConnect("Auth challenge completed but no outbound channel configured.");
                    return;
                }

                _outboundChannel.TryWrite(completed.Response);
                _log.Debug("Sent AUTH response packet to broker.");
                break;
            }

            case AuthChallengeFailed failed:
            {
                _log.Warning("Auth challenge failed: {0}", failed.Reason);
                _authStateMachine?.Fail();
                FailPendingConnect($"Auth challenge failed: {failed.Reason}");
                break;
            }

            case PublishProtocolDefaults.CheckTimeout _:
            {
                // Snapshot keys first to avoid InvalidOperationException from mutating the
                // Dictionary while iterating it (Dictionary.Remove during foreach throws in all
                // .NET versions including .NET 10).
                List<NonZeroUInt16>? timedOutSubscribes = null;
                foreach (var (packetId, pending) in _pendingSubscribes)
                {
                    if (pending.Deadline.IsOverdue)
                        (timedOutSubscribes ??= new()).Add(packetId);
                }
                if (timedOutSubscribes is not null)
                {
                    foreach (var packetId in timedOutSubscribes)
                    {
                        if (_pendingSubscribes.Remove(packetId, out var pending))
                            pending.Sender.Tell(new AckProtocol.SubscribeFailure("Timeout"));
                    }
                }

                List<NonZeroUInt16>? timedOutUnsubscribes = null;
                foreach (var (packetId, pending) in _pendingUnsubscribes)
                {
                    if (pending.Deadline.IsOverdue)
                        (timedOutUnsubscribes ??= new()).Add(packetId);
                }
                if (timedOutUnsubscribes is not null)
                {
                    foreach (var packetId in timedOutUnsubscribes)
                    {
                        if (_pendingUnsubscribes.Remove(packetId, out var pending))
                            pending.Sender.Tell(new AckProtocol.UnsubscribeFailure("Timeout"));
                    }
                }

                // Connect has no internal deadline (Deadline is null). The caller's
                // CancellationToken on the Ask call is the sole timeout mechanism for connects.
                // If Deadline is non-null (future extensibility), respect it here.
                if (_pendingConnect is { Deadline: { } connectDeadline } pendingConn
                    && connectDeadline.IsOverdue)
                {
                    pendingConn.Sender.Tell(new AckProtocol.ConnectFailure("Timeout"));
                    _pendingConnect = null;
                }
                break;
            }
        }
    }

    /// <summary>
    /// Handles an incoming AUTH challenge packet by invoking the auth state machine asynchronously.
    /// Uses PipeToSelf to avoid blocking the actor mailbox.
    /// </summary>
    private void HandleAuthChallenge(AuthPacket auth)
    {
        var stateMachine = _pendingConnect?.AuthStateMachine ?? _authStateMachine;
        if (stateMachine is null)
        {
            _log.Warning("Received AUTH challenge but no auth state machine is configured. Ignoring.");
            return;
        }

        var self = Self;

        stateMachine.HandleChallengeAsync(auth, CancellationToken.None)
            .AsTask()
            .ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully)
                    return (object)new AuthChallengeCompleted(t.Result);
                return new AuthChallengeFailed(t.Exception?.InnerException?.Message
                    ?? t.Exception?.Message
                    ?? "Unknown error during auth challenge");
            }, TaskContinuationOptions.ExecuteSynchronously)
            .PipeTo(self);
    }

    /// <summary>
    /// Fails the pending connect (if any) with the given reason and clears <c>_pendingConnect</c>.
    /// </summary>
    private void FailPendingConnect(string reason)
    {
        if (_pendingConnect is not null)
        {
            _pendingConnect.Value.Sender.Tell(new AckProtocol.ConnectFailure(reason));
            _pendingConnect = null;
        }
    }

    public ITimerScheduler Timers { get; set; } = null!;
}
