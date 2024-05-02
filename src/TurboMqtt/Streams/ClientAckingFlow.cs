// -----------------------------------------------------------------------
// <copyright file="ClientAckingFlow.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Immutable;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboMqtt.PacketTypes;

namespace TurboMqtt.Streams;

/// <summary>
/// Stage that handles the process of acknowledging packets from the client.
/// </summary>
internal sealed class ClientAckingFlow : GraphStage<FlowShape<ImmutableList<MqttPacket>, MqttPacket>>
{
    private readonly TaskCompletionSource<DisconnectPacket> _disconnectPromise;
    private readonly MqttRequiredActors _actors;

    /// <summary>
    /// Used to send packets back to the broker.
    /// </summary>
    private readonly ChannelWriter<MqttPacket> _outboundPackets;

    public ClientAckingFlow(ChannelWriter<MqttPacket> outboundPackets,
        MqttRequiredActors actors, TaskCompletionSource<DisconnectPacket> disconnectPromise)
    {
        _outboundPackets = outboundPackets;
        _actors = actors;
        _disconnectPromise = disconnectPromise;
        
        Shape = new FlowShape<ImmutableList<MqttPacket>, MqttPacket>(In, Out);
    }

    public Inlet<ImmutableList<MqttPacket>> In { get; } = new("ClientAckingFlow.in");
    public Outlet<MqttPacket> Out { get; } = new("ClientAckingFlow.out");

    public override FlowShape<ImmutableList<MqttPacket>, MqttPacket> Shape { get; }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        return new Logic(this);
    }

    private sealed class Logic : InAndOutGraphStageLogic
    {
        private readonly ClientAckingFlow _stage;
        private readonly Queue<MqttPacket> _buffer = new();

        // just to stop us from logging multiple Disconnect messages
        private bool _shutdownTriggered;

        protected override object LogSource => Akka.Event.LogSource.Create("ClientAckingFlow");

        public Logic(ClientAckingFlow stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage.In, this);
            SetHandler(stage.Out, this);
        }

        public override void OnPush()
        {
            var packets = Grab(_stage.In);
            
            // need to do this to ensure that we don't block the stream
            if(!HasBeenPulled(_stage.In))
                Pull(_stage.In);
            
            Log.Debug("Processing [{0}] packets from client.", packets.Count);

            foreach (var packet in packets)
            {
                Log.Debug("Received packet of type [{0}] from client.", packet.PacketType);

                if (packet.PacketType == MqttPacketType.Publish)
                {
                    var publish = (PublishPacket)packet;
                    HandlePublish(publish);
                    continue;
                }

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
                        var pubRel = (PubRelPacket)packet;
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
                            _stage._disconnectPromise.TrySetResult((DisconnectPacket)packet);
                            _shutdownTriggered = true;
                        }

                        // a completion watch stage above will handle the rest of the cleanup
                        Complete(_stage.Out);
                        break;
                    }
                    default:
                        // we should never reach here - the rest of these messages are server msgs
                        throw new ArgumentOutOfRangeException(nameof(packet),
                            $"Unsupported packet of type [{packet.PacketType}] - this is a server message.");
                }
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
                    Log.Debug("Pushing older packet of type [{0}] from buffer.", olderPacket.PacketType);
                    _buffer.Enqueue(packet);
                }
                else
                {
                    Log.Debug("Pushing packet of type [{0}] without buffering", packet.PacketType);
                    Push(_stage.Out, packet);
                }
                    

                return true;
            }
            else
            {
                Log.Debug("Buffering packet of type [{0}] due to backpressure.", packet.PacketType);
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
     
            if (!HasBeenPulled(_stage.In))
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
                    _stage._outboundPackets.TryWrite(pubAck);
                    TryPush(publish);
                    return;
            }
                case QualityOfService.ExactlyOnce:
                {
                    var pubRec = publish.ToPubRec();
                    _stage._outboundPackets.TryWrite(pubRec);
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

            // send the PubComp packet
            _stage._outboundPackets.TryWrite(pubComp);
        }
    }
}