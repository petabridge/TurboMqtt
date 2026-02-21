// -----------------------------------------------------------------------
// <copyright file="ExactlyOncePublishRetryActor.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;
using TurboMqtt.PacketTypes;
using TurboMqtt.Utility;
using static TurboMqtt.Protocol.Pub.PublishProtocolDefaults;

namespace TurboMqtt.Protocol.Pub;

/// <summary>
/// Actor is responsible for handling QoS 2 requirements for outbound <see cref="PublishPacket"/>s.
/// </summary>
internal sealed class ExactlyOncePublishRetryActor : UntypedActor, IWithTimers
{
    private const string PublishTimerKey = "publish-timer";

    /// <summary>
    /// State of a pending publish operation - indicates that we're waiting for a <see cref="PubRecPacket"/>
    /// or a <see cref="PubCompPacket"/> from the server.
    /// </summary>
    public record struct PendingPublish(
        PublishPacket Packet,
        Deadline Deadline,
        IActorRef Sender,
        bool PubRecReceived,
        int RemainingRetries,
        bool QuotaClaimed);

    private readonly int _maxRetries;
    private readonly TimeSpan _publishTimeout;
    private readonly ChannelWriter<MqttPacket> _outboundPackets;
    private readonly Dictionary<NonZeroUInt16, PendingPublish> _pendingPackets = new();
    private readonly Queue<(PublishPacket Packet, IActorRef Sender)> _bufferedPublishes = new();
    private ushort _receiveMaximum; // 0 = unlimited (legacy per-actor mode)
    private readonly SharedReceiveMaximumQuota? _sharedQuota; // null = legacy mode
    private IActorRef? _siblingActor; // QoS 1 sibling for cross-QoS buffer drain
    private readonly ILoggingAdapter _log = Context.GetLogger();

    public ExactlyOncePublishRetryActor(ChannelWriter<MqttPacket> outboundPackets, int maxRetries = DefaultMaxRetries,
        TimeSpan? publishTimeout = null, SharedReceiveMaximumQuota? sharedQuota = null)
    {
        _outboundPackets = outboundPackets;
        _maxRetries = maxRetries;
        _publishTimeout = publishTimeout ?? DefaultPublishTimeout;
        _sharedQuota = sharedQuota;
    }

    protected override void PreStart()
    {
        Timers.StartPeriodicTimer(PublishTimerKey, PublishProtocolDefaults.CheckTimeout.Instance, TimeSpan.FromSeconds(1));
    }

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case SetSiblingPublisher setSibling:
            {
                _siblingActor = setSibling.Sibling;
                _log.Debug("Registered QoS 1 sibling actor [{0}]", _siblingActor);
                return;
            }

            case TryDequeueBuffered:
            {
                // Sibling freed a quota slot; try to promote buffered publishes.
                DequeueBuffered();
                return;
            }

            case PublishingProtocol.SetReceiveMaximum setMax:
            {
                if (_sharedQuota != null)
                {
                    // Shared quota mode: set the limit on the shared object.
                    _sharedQuota.SetMaximum(setMax.Value);
                }
                else
                {
                    // Legacy per-actor mode.
                    _receiveMaximum = setMax.Value;
                }
                _log.Debug("ReceiveMaximum set to [{0}]", setMax.Value);
                return;
            }

            case PublishPacket packet:
            {
                if (_pendingPackets.ContainsKey(packet.PacketId))
                {
                    _log.Warning("Received duplicate publish packet with ID [{0}]", packet.PacketId);
                    Sender.Tell(new PublishingProtocol.PublishFailure("Duplicate packet ID"));
                    return;
                }

                if (_sharedQuota != null)
                {
                    // Shared quota mode: attempt to claim a slot.
                    if (!_sharedQuota.TryClaim())
                    {
                        _log.Debug("Shared ReceiveMaximum reached; buffering QoS 2 publish [{0}]", packet.PacketId);
                        _bufferedPublishes.Enqueue((packet, Sender));
                        return;
                    }

                    if (_sharedQuota.IsLimited)
                        _outboundPackets.TryWrite(packet);

                    _pendingPackets[packet.PacketId] = new PendingPublish(packet, Deadline.FromNow(_publishTimeout),
                        Sender, false, _maxRetries, QuotaClaimed: _sharedQuota.IsLimited);
                }
                else
                {
                    // Legacy per-actor mode.
                    if (_receiveMaximum > 0 && _pendingPackets.Count >= _receiveMaximum)
                    {
                        _log.Debug("ReceiveMaximum [{0}] reached; buffering publish [{1}]", _receiveMaximum,
                            packet.PacketId);
                        _bufferedPublishes.Enqueue((packet, Sender));
                        return;
                    }

                    if (_receiveMaximum > 0)
                        _outboundPackets.TryWrite(packet);

                    _pendingPackets[packet.PacketId] = new PendingPublish(packet, Deadline.FromNow(_publishTimeout),
                        Sender, false, _maxRetries, QuotaClaimed: false);
                }
                return;
            }

            case PubRecPacket rec:
            {
                _log.Debug("Received PubRec with id [{0}], reason [{1}] from broker", rec.PacketId, rec.ReasonCode);

                if (_pendingPackets.TryGetValue(rec.PacketId, out var pending))
                {
                    // check the reason code (which will be null for MQTT 3.1.1)
                    if (rec.ReasonCode != null && rec.ReasonCode != PubRecReasonCode.Success)
                    {
                        // remove the pending packet
                        _pendingPackets.Remove(rec.PacketId, out _);
                        if (pending.QuotaClaimed) _sharedQuota?.Release();
                        _log.Warning("Received PubRec with reason code [{0}] for packet ID [{1}]", rec.ReasonCode,
                            rec.PacketId);
                        pending.Sender.Tell(new PublishingProtocol.PublishFailure("PubRec failed"));
                        DequeueBuffered();
                        return;
                    }

                    // need to send a PubRel packet
                    var pubRel = pending.Packet.ToPubRel();
                    pubRel.Duplicate = pending.PubRecReceived; // mark this as a duplicate if we've already received a PubRec

                    _outboundPackets.TryWrite(pubRel); // we use unbounded channels - this won't fail

                    // for the time being, don't update the deadline - make it cumulative until the operation finishes
                    _pendingPackets[rec.PacketId] = pending with { PubRecReceived = true };
                }
                else
                {
                    // send a PubRel indicating that we don't have any record of this packet (so it stops resending it)
                    var pubRel = new PubRelPacket()
                    {
                        PacketId = rec.PacketId, ReasonCode = PubRelReasonCode.PacketIdentifierNotFound,
                        ReasonString = "Packet ID not found", UserProperties = rec.UserProperties
                    };
                    _outboundPackets.TryWrite(pubRel); // we use unbounded channels - this won't fail
                    _log.Warning("Received PubRec for unknown packet ID [{0}]", rec.PacketId);
                }

                return;
            }

            // PubRel is the acknowledgment of the PubRec packet
            case PubCompPacket comp:
            {
                _log.Debug("Received PubComp with id [{0}], reason [{1}] from broker", comp.PacketId, comp.ReasonCode);

                if (_pendingPackets.Remove(comp.PacketId, out var pending))
                {
                    if (pending.QuotaClaimed) _sharedQuota?.Release();
                    _log.Debug("Successfully published packet with ID [{0}] and QoS=2", comp.PacketId);
                    pending.Sender.Tell(PublishingProtocol.PublishSuccess.Instance);
                    // PubComp frees the receive-maximum slot for QoS 2 (per MQTT 5 spec §4.9)
                    DequeueBuffered();
                }
                else
                {
                    _log.Warning("Received PubComp for unknown packet ID [{0}]", comp.PacketId);
                }

                return;
            }

            case PublishingProtocol.PublishCancelled cancel:
            {
                if (_pendingPackets.Remove(cancel.PacketId, out var pending))
                {
                    if (pending.QuotaClaimed) _sharedQuota?.Release();
                    pending.Sender.Tell(new PublishingProtocol.PublishFailure("Cancelled"));
                    DequeueBuffered();
                }
                else
                {
                    _log.Warning("Received cancel request for unknown packet ID [{0}]", cancel.PacketId);
                }

                return;
            }

            case PublishProtocolDefaults.CheckTimeout _:
            {
                foreach (var (packetId, pending) in _pendingPackets)
                {
                    if (!pending.Deadline.IsOverdue) continue;
                    if (pending.RemainingRetries > 0)
                    {
                        // first, we need to determine where we are in the process
                        if (pending.PubRecReceived)
                        {
                            // we need to resend the PubRel packet
                            var pubRel = new PubRelPacket()
                            {
                                PacketId = packetId, ReasonCode = PubRelReasonCode.Success, ReasonString = "Success",
                                UserProperties = pending.Packet.UserProperties, Duplicate = true
                            };

                            _outboundPackets.TryWrite(pubRel); // we use unbounded channels - this won't fail

                            _pendingPackets[packetId] = pending with
                            {
                                Deadline = Deadline.FromNow(_publishTimeout),
                                RemainingRetries = pending.RemainingRetries - 1
                            };

                            _log.Debug("Pub packet with ID [{0}] timed out, resending PubRel", packetId);
                        }
                        else
                        {
                            // we need to retry this packet

                            pending.Packet.Duplicate = true;
                            _outboundPackets.TryWrite(pending.Packet); // we use unbounded channels - this won't fail
                            _pendingPackets[packetId] = pending with
                            {
                                Deadline = Deadline.FromNow(_publishTimeout),
                                RemainingRetries = pending.RemainingRetries - 1
                            };
                            _log.Debug("Pub packet with ID [{0}] timed out, retrying", packetId);
                        }
                    }
                    else
                    {
                        // we've run out of retries
                        _log.Warning("Pub packet with ID [{0}] timed out, no more retries left", packetId);
                        _pendingPackets.Remove(packetId, out _);
                        if (pending.QuotaClaimed) _sharedQuota?.Release();
                        pending.Sender.Tell(new PublishingProtocol.PublishFailure("Timeout"));
                        DequeueBuffered();
                    }
                }

                return;
            }
        }
    }

    /// <summary>
    /// When a slot is freed (PubComp or cancel), dequeue buffered publishes up to the receive maximum.
    /// When operating in shared-quota mode and the local buffer is fully drained, notifies the
    /// sibling QoS 1 actor so it can promote its own buffered messages.
    /// </summary>
    private void DequeueBuffered()
    {
        if (_sharedQuota?.IsLimited == true)
        {
            // Shared quota mode: claim slots from the shared object for each buffered publish.
            while (_bufferedPublishes.Count > 0 && _sharedQuota.TryClaim())
            {
                var (bufferedPacket, bufferedSender) = _bufferedPublishes.Dequeue();
                var deadline = Deadline.FromNow(_publishTimeout);
                _pendingPackets[bufferedPacket.PacketId] = new PendingPublish(bufferedPacket, deadline, bufferedSender,
                    false, _maxRetries, QuotaClaimed: true);
                _outboundPackets.TryWrite(bufferedPacket);
                _log.Debug("Dequeued buffered QoS 2 publish [{0}] after slot freed", bufferedPacket.PacketId);
            }

            // If our buffer is empty, there may be a free slot available for the sibling (QoS 1) actor.
            if (_bufferedPublishes.Count == 0 && _siblingActor != null)
                _siblingActor.Tell(TryDequeueBuffered.Instance);
        }
        else
        {
            // Legacy per-actor mode.
            while (_receiveMaximum > 0 && _bufferedPublishes.Count > 0 && _pendingPackets.Count < _receiveMaximum)
            {
                var (bufferedPacket, bufferedSender) = _bufferedPublishes.Dequeue();
                var deadline = Deadline.FromNow(_publishTimeout);
                _pendingPackets[bufferedPacket.PacketId] = new PendingPublish(bufferedPacket, deadline, bufferedSender,
                    false, _maxRetries, QuotaClaimed: false);
                _outboundPackets.TryWrite(bufferedPacket);
                _log.Debug("Dequeued buffered publish [{0}] after slot freed", bufferedPacket.PacketId);
            }
        }
    }

    /// <summary>
    /// This field gets set by Akka.NET itself when the actor is started.
    /// </summary>
    public ITimerScheduler Timers { get; set; } = null!;
}
