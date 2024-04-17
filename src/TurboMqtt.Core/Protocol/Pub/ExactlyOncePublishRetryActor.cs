// -----------------------------------------------------------------------
// <copyright file="ExactlyOncePublishRetryActor.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;
using TurboMqtt.Core.PacketTypes;
using static TurboMqtt.Core.Protocol.Publish.PublishProtocolDefaults;

namespace TurboMqtt.Core.Protocol.Publish;

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
        int RemainingRetries);

    private readonly int _maxRetries;
    private readonly TimeSpan _publishTimeout;
    private readonly ChannelWriter<MqttPacket> _outboundPackets;
    private readonly Dictionary<NonZeroUInt16, PendingPublish> _pendingPackets = new();
    private readonly ILoggingAdapter _log = Context.GetLogger();

    public ExactlyOncePublishRetryActor(ChannelWriter<MqttPacket> outboundPackets, int maxRetries = DefaultMaxRetries,
        TimeSpan? publishTimeout = null)
    {
        _outboundPackets = outboundPackets;
        _maxRetries = maxRetries;
        _publishTimeout = publishTimeout ?? DefaultPublishTimeout;
    }

    protected override void PreStart()
    {
        Timers.StartPeriodicTimer(PublishTimerKey, CheckTimeout.Instance, TimeSpan.FromSeconds(1));
    }

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case PublishPacket packet:
            {
                if (_pendingPackets.ContainsKey(packet.PacketId))
                {
                    _log.Warning("Received duplicate publish packet with ID [{0}]", packet.PacketId);
                    Sender.Tell(new PublishingProtocol.PublishFailure("Duplicate packet ID"));
                    return;
                }

                // we don't send packets to the server first time around - Akka.Streams handles that
                _pendingPackets[packet.PacketId] = new PendingPublish(packet, Deadline.FromNow(_publishTimeout),
                    Sender, false, 3);
                return;
            }

            case PubRecPacket rec:
            {
                if (_pendingPackets.TryGetValue(rec.PacketId, out var pending))
                {
                    // need to send a PubRel packet
                    var pubRel = pending.Packet.ToPubRel();

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
                if (_pendingPackets.Remove(comp.PacketId, out var pending))
                {
                    pending.Sender.Tell(PublishingProtocol.PublishSuccess.Instance);
                }
                else
                {
                    _log.Warning("Received PubComp for unknown packet ID [{0}]", comp.PacketId);
                }

                return;
            }

            case CheckTimeout _:
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
                        pending.Sender.Tell(new PublishingProtocol.PublishFailure("Timeout"));
                    }
                }

                return;
            }
        }
    }

    /// <summary>
    /// This field gets set by Akka.NET itself when the actor is started.
    /// </summary>
    public ITimerScheduler Timers { get; set; } = null!;
}