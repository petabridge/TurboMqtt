// -----------------------------------------------------------------------
// <copyright file="AtLeastOncePublishRetryActor.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;
using TurboMqtt.PacketTypes;
using TurboMqtt.Utility;
using static TurboMqtt.Protocol.Pub.PublishingProtocol;
using static TurboMqtt.Protocol.Pub.PublishProtocolDefaults;

namespace TurboMqtt.Protocol.Pub;

/// <summary>
/// Actor is responsible for handling QoS 1 requirements for outbound <see cref="PublishPacket"/>s.
/// </summary>
internal sealed class AtLeastOncePublishRetryActor : UntypedActor, IWithTimers
{
    public record struct PendingPublish(
        PublishPacket Packet,
        Deadline Deadline,
        IActorRef Sender,
        int RemainingRetries);

    private const string PublishTimerKey = "publish-timer";

    private readonly ChannelWriter<MqttPacket> _outboundPackets;
    private readonly int _maxRetries;
    private readonly TimeSpan _publishTimeout;
    private readonly Dictionary<NonZeroUInt16, PendingPublish> _pendingPackets = new();
    private readonly Queue<(PublishPacket Packet, IActorRef Sender)> _bufferedPublishes = new();
    private ushort _receiveMaximum; // 0 = unlimited
    private readonly ILoggingAdapter _log = Context.GetLogger();

    public AtLeastOncePublishRetryActor(ChannelWriter<MqttPacket> outboundPackets, int maxRetries = DefaultMaxRetries,
        TimeSpan? publishTimeout = null)
    {
        _outboundPackets = outboundPackets;
        _maxRetries = maxRetries;
        _publishTimeout = publishTimeout ?? DefaultPublishTimeout;
    }

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case PublishingProtocol.SetReceiveMaximum setMax:
            {
                _receiveMaximum = setMax.Value;
                _log.Debug("ReceiveMaximum set to [{0}]", _receiveMaximum);
                return;
            }

            // New packet to publish - it's someone else's responsibility to make sure the QoS is correct
            case PublishPacket packet:
            {
                // sanity check - we shouldn't be receiving duplicate packets
                if (_pendingPackets.ContainsKey(packet.PacketId))
                {
                    _log.Warning("Received duplicate publish packet with ID [{0}]", packet.PacketId);
                    Sender.Tell(new PublishingProtocol.PublishFailure("Duplicate packet ID"));
                    return;
                }

                // When ReceiveMaximum is set and we're at the limit, buffer the publish
                if (_receiveMaximum > 0 && _pendingPackets.Count >= _receiveMaximum)
                {
                    _log.Debug("ReceiveMaximum [{0}] reached; buffering publish [{1}]", _receiveMaximum, packet.PacketId);
                    _bufferedPublishes.Enqueue((packet, Sender));
                    return;
                }

                var deadline = Deadline.FromNow(_publishTimeout);
                _pendingPackets[packet.PacketId] = new PendingPublish(packet, deadline, Sender, _maxRetries);

                // When ReceiveMaximum is active the actor owns the initial channel write;
                // otherwise MqttClient writes to the channel directly (legacy path).
                if (_receiveMaximum > 0)
                    _outboundPackets.TryWrite(packet);

                return;
            }

            // Acknowledgement from the server
            case PubAckPacket ack:
            {
                _log.Debug("Received PubAck with id [{0}], reason [{1}] from broker", ack.PacketId, ack.ReasonCode);

                if (_pendingPackets.Remove(ack.PacketId, out var pending))
                {
                    // check the return code
                    if (ack.ReasonCode != MqttPubAckReasonCode.Success)
                    {
                        _log.Warning("Received PubAck with non-success return code [{0}]", ack.ReasonCode);
                        pending.Sender.Tell(new PublishingProtocol.PublishFailure(ack.ReasonString ?? ack.ReasonCode.ToString()));
                        DequeueBuffered();
                        return;
                    }
                    pending.Sender.Tell(PublishingProtocol.PublishSuccess.Instance);
                    DequeueBuffered();
                }
                else
                {
                    // could happen in cases where a client canceled a publish that was already received by the server
                    _log.Warning("Received PubAck for unknown packet ID [{0}]", ack.PacketId);
                }

                return;
            }

            // Timeout for a packet
            case PublishProtocolDefaults.CheckTimeout _:
            {
                foreach (var (packetId, pending) in _pendingPackets)
                {
                    if (!pending.Deadline.IsOverdue) continue;
                    if (pending.RemainingRetries > 0)
                    {
                        // we need to retry this packet
                        _log.Debug("Pub packet with ID [{0}] timed out, retrying", packetId);
                        pending.Packet.Duplicate =
                            true; // we're going to resend this packet, need to set the duplicate flag
                        _outboundPackets.TryWrite(pending.Packet); // we use unbounded channels - this won't fail
                        _pendingPackets[packetId] = pending with
                        {
                            Deadline = Deadline.FromNow(_publishTimeout),
                            RemainingRetries = pending.RemainingRetries - 1
                        };
                    }
                    else
                    {
                        // we've run out of retries
                        _log.Warning("Pub packet with ID [{0}] timed out, no more retries left", packetId);
                        _pendingPackets.Remove(packetId, out _);
                        pending.Sender.Tell(new PublishingProtocol.PublishFailure("Timeout"));
                        DequeueBuffered();
                    }
                }

                return;
            }

            // client timed out a publish operation
            case PublishingProtocol.PublishCancelled cancel:
            {
                if (_pendingPackets.Remove(cancel.PacketId, out var pending))
                {
                    // slot freed — promote a buffered publish if any
                    DequeueBuffered();
                }
                else
                {
                    _log.Warning("Received cancellation for unknown packet ID [{0}]", cancel.PacketId);
                }

                return;
            }
        }
    }

    /// <summary>
    /// When a slot is freed (ACK or cancel), dequeue buffered publishes up to the receive maximum.
    /// </summary>
    private void DequeueBuffered()
    {
        while (_receiveMaximum > 0 && _bufferedPublishes.Count > 0 && _pendingPackets.Count < _receiveMaximum)
        {
            var (bufferedPacket, bufferedSender) = _bufferedPublishes.Dequeue();
            var deadline = Deadline.FromNow(_publishTimeout);
            _pendingPackets[bufferedPacket.PacketId] = new PendingPublish(bufferedPacket, deadline, bufferedSender, _maxRetries);
            _outboundPackets.TryWrite(bufferedPacket);
            _log.Debug("Dequeued buffered publish [{0}] after slot freed", bufferedPacket.PacketId);
        }
    }

    protected override void PreStart()
    {
        Timers.StartPeriodicTimer(PublishTimerKey, PublishProtocolDefaults.CheckTimeout.Instance, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// This field gets set by Akka.NET itself when the actor is started.
    /// </summary>
    public ITimerScheduler Timers { get; set; } = null!;
}