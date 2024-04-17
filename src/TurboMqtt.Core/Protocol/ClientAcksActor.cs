﻿// -----------------------------------------------------------------------
// <copyright file="ClientAcksActor.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;
using TurboMqtt.Core.PacketTypes;
using TurboMqtt.Core.Protocol.Publish;
using static TurboMqtt.Core.Protocol.AckProtocol;

namespace TurboMqtt.Core.Protocol;

/// <summary>
/// This actor handles client-side messages that require an acknowledgment from the broker:
///
/// * <see cref="SubscribePacket"/>
/// * <see cref="UnsubscribePacket"/>
/// * <see cref="ConnectPacket"/>
///
/// <see cref="PingReqPacket"/> and <see cref="PingRespPacket"/> are handled by the <see cref="PingActor"/>.
/// </summary>
internal sealed class ClientAcksActor : UntypedActor, IWithTimers
{
    public record struct PendingSubscribe(SubscribePacket Packet, Deadline Deadline, IActorRef Sender);
    
    public record struct PendingUnsubscribe(UnsubscribePacket Packet, Deadline Deadline, IActorRef Sender);
    public record struct PendingConnect(ConnectPacket Packet, Deadline Deadline, IActorRef Sender);

    /// <summary>
    /// Timeout period used for connects and subscribes.
    /// </summary>
    private readonly TimeSpan _actionTimeout;
    
    // pending subscribes, connects, and disconnects
    private readonly Dictionary<NonZeroUInt16, PendingSubscribe> _pendingSubscribes = new();
    private readonly Dictionary<NonZeroUInt16, PendingUnsubscribe> _pendingUnsubscribes = new();
    private PendingConnect? _pendingConnect = null;
    
    private readonly ILoggingAdapter _log = Context.GetLogger();

    public ClientAcksActor(TimeSpan? actionTimeout = null)
    {
        _actionTimeout = actionTimeout ?? PublishProtocolDefaults.DefaultPublishTimeout;
    }
    
    protected override void PreStart()
    {
        Timers.StartPeriodicTimer("ack-timeout", PublishProtocolDefaults.CheckTimeout.Instance, _actionTimeout);
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
                    Sender.Tell(new SubscribeFailure("Duplicate packet ID"));
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
                    Sender.Tell(new UnsubscribeFailure("Duplicate packet ID"));
                    return;
                }
                
                // we don't send packets to the server first time around - Akka.Streams handles that
                var deadline = Deadline.FromNow(_actionTimeout);
                _pendingUnsubscribes[unsubscribe.PacketId] = new PendingUnsubscribe(unsubscribe, deadline, Sender);
                break;
            }
            
            case ConnectPacket connect:
            {
                if (_pendingConnect is not null)
                {
                    _log.Warning("Received duplicate connect request");
                    Sender.Tell(new ConnectFailure("Already connecting to broker"));
                    return;
                }

                var deadline = Deadline.FromNow(_actionTimeout);
                _pendingConnect = new PendingConnect(connect, deadline, Sender);
                break;
            }
            
            case SubAckPacket ack:
            {
                if (_pendingSubscribes.Remove(ack.PacketId, out var pending))
                {
                    pending.Sender.Tell(new SubscribeSuccess(ack));
                }
                else
                {
                    // could happen in cases where a client canceled a subscribe that was already received by the server
                    _log.Warning("Received SubAck for unknown packet ID [{0}]", ack.PacketId);
                }
                break;
            }
            
            case UnsubscribeAckPacket ack:
            {
                if (_pendingUnsubscribes.Remove(ack.PacketId, out var pending))
                {
                    pending.Sender.Tell(new UnsubscribeSuccess(ack));
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
                    _pendingConnect.Value.Sender.Tell(new ConnectSuccess(ack));
                    _pendingConnect = null;
                }
                else
                {
                    _log.Warning("Received ConnAck for unknown connect request");
                }
                break;
            }
            
            case PublishProtocolDefaults.CheckTimeout _:
            {
                var now = Deadline.Now;
                foreach (var (packetId, pending) in _pendingSubscribes)
                {
                    if (pending.Deadline.IsOverdue)
                    {
                        _pendingSubscribes.Remove(packetId, out _);
                        pending.Sender.Tell(new SubscribeFailure("Timeout"));
                    }
                }
                
                foreach (var (packetId, pending) in _pendingUnsubscribes)
                {
                    if (pending.Deadline.IsOverdue)
                    {
                        _pendingUnsubscribes.Remove(packetId, out _);
                        pending.Sender.Tell(new UnsubscribeFailure("Timeout"));
                    }
                }
                
                if (_pendingConnect is not null && _pendingConnect.Value.Deadline.IsOverdue)
                {
                    _pendingConnect.Value.Sender.Tell(new ConnectFailure("Timeout"));
                    _pendingConnect = null;
                }
                break;
            }
        }
    }

    public ITimerScheduler Timers { get; set; } = null!;
}