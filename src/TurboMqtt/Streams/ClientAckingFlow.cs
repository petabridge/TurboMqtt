// -----------------------------------------------------------------------
// <copyright file="ClientAckingFlow.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboMqtt.PacketTypes;
using TurboMqtt.Utility;

namespace TurboMqtt.Streams;

/// <summary>
/// Stage that handles the process of acknowledging packets from the client.
/// </summary>
internal sealed class ClientAckingFlow : GraphStage<FlowShape<MqttPacket, MqttPacket>>
{
    private readonly TaskCompletionSource<DisconnectPacket> _disconnectPromise;
    private readonly MqttRequiredActors _actors;
    private readonly int _bufferSize;
    private readonly TimeSpan _bufferExpiry;
    
    /// <summary>
    /// Used to send packets back to the broker.
    /// </summary>
    private readonly ChannelWriter<MqttPacket> _outboundPackets;

    public ClientAckingFlow(int bufferSize, TimeSpan bufferExpiry, ChannelWriter<MqttPacket> outboundPackets, MqttRequiredActors actors, TaskCompletionSource<DisconnectPacket> disconnectPromise)
    {
        // assert that buffer size is at least 1
        if (bufferSize < 1)
            throw new ArgumentException("Buffer size must be at least 1", nameof(bufferSize));
        
        // assert that bufferExpiry is non-zero
        if (bufferExpiry <= TimeSpan.Zero)
            throw new ArgumentException("Buffer expiry must be greater than zero", nameof(bufferExpiry));
        _outboundPackets = outboundPackets;
        _actors = actors;
        _disconnectPromise = disconnectPromise;

        _bufferSize = bufferSize;
        _bufferExpiry = bufferExpiry;
        Shape = new FlowShape<MqttPacket, MqttPacket>(In, Out);
    }
    
    public Inlet<MqttPacket> In { get; } = new("ClientAckingFlow.in");
    public Outlet<MqttPacket> Out { get; } = new("ClientAckingFlow.out");

    public override FlowShape<MqttPacket, MqttPacket> Shape { get; }
    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        return new Logic(this);
    }
    
    private sealed class Logic : InAndOutGraphStageLogic
    {
        private readonly SimpleLruCache<NonZeroUInt16> _publishIds;
        private readonly SimpleLruCache<NonZeroUInt16> _pubRelIds;
        private readonly ClientAckingFlow _stage;
        private readonly Queue<MqttPacket> _buffer = new();
        private Deadline _timeToCheckEvictions = Deadline.Now;
        
        // just to stop us from logging multiple Disconnect messages
        private bool _shutdownTriggered;

        protected override object LogSource => Akka.Event.LogSource.Create("ClientAckingFlow");

        public Logic(ClientAckingFlow stage) : base(stage.Shape)
        {
            _stage = stage;
            _publishIds = new SimpleLruCache<NonZeroUInt16>(stage._bufferSize, stage._bufferExpiry);
            _pubRelIds = new SimpleLruCache<NonZeroUInt16>(stage._bufferSize, stage._bufferExpiry);
            
            SetHandler(stage.In, this);
            SetHandler(stage.Out, this);
        }

        public override void OnPush()
        {
            var packet = Grab(_stage.In);
            Log.Debug("Received packet of type [{0}] from client.", packet.PacketType);
            
            if(packet.PacketType == MqttPacketType.Publish)
            {
                var publish = (PublishPacket) packet;
                HandlePublish(publish);
                return;
            }
            
            // need to do this to ensure that we don't block the stream
            Pull(_stage.In);

            switch (packet.PacketType)
            {
                case MqttPacketType.PubAck:
                {
                    // QoS 1 actor handles this
                    _stage._actors.Qos1Actor.Tell(packet);
                    break;
                }
                case MqttPacketType.PubRel:
                {
                    var pubRel = (PubRelPacket) packet;
                    HandlePubRel(pubRel);
                    break;
                }
                case MqttPacketType.PubRec:
                case MqttPacketType.PubComp:
                {
                    // QoS 2 actor handles this
                    _stage._actors.Qos2Actor.Tell(packet);
                    break;
                }
                case MqttPacketType.PingResp:
                {
                    // Heartbeat actor handles this
                    _stage._actors.HeartBeatActor.Tell(packet);
                    break;
                }
                case MqttPacketType.ConnAck:
                case MqttPacketType.SubAck:
                case MqttPacketType.UnsubAck:
                {
                    // Client ACK actor handles this
                    _stage._actors.ClientAck.Tell(packet);
                    break;
                }
                case MqttPacketType.Disconnect:
                {
                    if (!_shutdownTriggered)
                    {
                        Log.Info("Received DISCONNECT packet from broker - closing connection.");
                        _stage._disconnectPromise.TrySetResult((DisconnectPacket) packet);
                        _shutdownTriggered = true;
                    }
                   
                    // a completion watch stage above will handle the rest of the cleanup
                    Complete(_stage.Out);
                    break;
                }
                default:
                    // we should never reach here - the rest of these messages are server msgs
                    throw new ArgumentOutOfRangeException(nameof(packet), $"Unsupported packet of type [{packet.PacketType}] - this is a server message.");
            }
            
            // check to see if we need to evict any expired items
            if (_timeToCheckEvictions.IsOverdue)
            {
                OnExpiredTimer();
                _timeToCheckEvictions = Deadline.FromNow(TimeSpan.FromSeconds(1));
            }
        }
        
        private bool TryPush(MqttPacket packet)
        {
            // if Port is available, push the packet
            if (IsAvailable(_stage.Out))
            {
                if (_buffer.TryDequeue(out var olderPacket))
                {
                    Push(_stage.Out, olderPacket);
                    _buffer.Enqueue(packet);
                }
                else
                    Push(_stage.Out, packet);
                return true;
            }
            else 
            {
                _buffer.Enqueue(packet);
                return false;
            }
        }

        public override void OnUpstreamFinish()
        {
            // we're done here
            CompleteStage();
        }

        public override void OnUpstreamFailure(Exception e)
        {
            FailStage(e);
        }

        public override void PostStop()
        {
            // clean up any remaining packets
            Log.Info("Cleaning up remaining [{0}] packets in buffer.", _buffer.Count);
            _buffer.Clear();
        }

        public override void OnPull()
        {
            if(_buffer.TryDequeue(out var packet)) // immediately push the next packet if we have one
                Push(_stage.Out, packet);
            else
                // if we don't have any packets to push, pull from the upstream
                // to see if we can get more (this is a backpressure mechanism
                Pull(_stage.In);
        }

        public override void OnDownstreamFinish(Exception cause)
        {
            // we're done here
            CompleteStage();
        }

        private void HandlePublish(PublishPacket publish)
        {
            switch (publish.QualityOfService)
            {
                case QualityOfService.AtMostOnce:
                    TryPush(publish); // no ACK required here
                    return;
                case QualityOfService.AtLeastOnce:
                {
                    var pubAck = publish.ToPubAck();
                    var alreadySeen = _publishIds.Contains(publish.PacketId);
                    pubAck.Duplicate = alreadySeen; // mark as duplicate if this isn't the first time we've ACKd
                    _stage._outboundPackets.TryWrite(pubAck);
                    
                    // TODO: check to see if the original packet was a duplicate too - might be interesting to log that here
                    if (alreadySeen) return; // add the PacketId and push the message if we've never seen it before
                    _publishIds.Add(publish.PacketId);
                    TryPush(publish);
                    return;
                }
                case QualityOfService.ExactlyOnce:
                {
                    var pubRec = publish.ToPubRec();
                    var alreadySeen = _publishIds.Contains(publish.PacketId);
                    pubRec.Duplicate = alreadySeen; // mark as duplicate if this isn't the first time we've ACKd

                    // TODO: check to see if the original packet was a duplicate too - might be interesting to log that here
                    _stage._outboundPackets.TryWrite(pubRec);

                    if (alreadySeen) return; // add the PacketId and push the message if we've never seen it before
                    _publishIds.Add(publish.PacketId);
                    TryPush(publish);
                    return;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(publish), "Unknown QoS level");
            }
        }

        /// <summary>
        /// Used only for <see cref="QualityOfService.ExactlyOnce"/>
        /// </summary>
        /// <param name="pubRel"></param>
        private void HandlePubRel(PubRelPacket pubRel)
        {
            // edge case - what if we receive a PubRel packet for a message that we haven't seen before?
            // that would be a bug with the broker then - ignore it
            var pubComp = pubRel.ToPubComp();
            var alreadySeen = _pubRelIds.Contains(pubRel.PacketId);
            pubComp.Duplicate = alreadySeen; // mark as duplicate if this isn't the first time we've ACKd
            
            // send the PubComp packet
            _stage._outboundPackets.TryWrite(pubComp);
            
            if (alreadySeen) return; // add the PacketId and push the message if we've never seen it before
            _pubRelIds.Add(pubRel.PacketId);
        }

        private void OnExpiredTimer()
        {
            // clean up expired items
            _publishIds.EvictExpired();
            _pubRelIds.EvictExpired();
        }
    }
}