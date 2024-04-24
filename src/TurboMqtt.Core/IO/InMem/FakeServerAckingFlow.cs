// -----------------------------------------------------------------------
// <copyright file="FakeServerAckingFlow.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt.Core.IO.InMem;

/* Not currently used */

/// <summary>
/// Used to simulate the process of acknowledging packets from the server.
/// </summary>
// internal sealed class FakeServerAckingFlow : GraphStage<FlowShape<MqttPacket, MqttPacket>>
// {
//     public Inlet<MqttPacket> In { get; } = new("FakeServerAckingFlow.in");
//     public Outlet<MqttPacket> Out { get; } = new("FakeServerAckingFlow.out");
//     
//     public FakeServerAckingFlow()
//     {
//         Shape = new FlowShape<MqttPacket, MqttPacket>(In, Out);
//     }
//     
//     public override FlowShape<MqttPacket, MqttPacket> Shape { get; }
//     protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
//     {
//         throw new NotImplementedException();
//     }
//     
//     private sealed class Logic : InAndOutGraphStageLogic
//     {
//         private readonly HashSet<string> _subscribedTopics = new();
//         private readonly Queue<MqttPacket> _pendingPackets = new();
//         
//         private readonly FakeServerAckingFlow _stage;
//         
//         public Logic(FakeServerAckingFlow stage) : base(stage.Shape)
//         {
//             _stage = stage;
//             SetHandler(_stage.In, this);
//             SetHandler(_stage.Out, this);
//         }
//         
//         private void TryPush(MqttPacket packet)
//         {
//             if (IsAvailable(_stage.Out))
//             {
//                 Push(_stage.Out, packet);
//             }
//             else
//             {
//                 _pendingPackets.Enqueue(packet);
//             }
//         }
//         
//         public override void OnPush()
//         {
//             var packet = Grab(_stage.In);
//
//             switch (packet.PacketType)
//             {
//                 case MqttPacketType.Publish:
//                     var publish = (PublishPacket) packet;
//
//                     switch (publish.QualityOfService)
//                     {
//                         case QualityOfService.AtLeastOnce:
//                             var pubAck = publish.ToPubAck();
//                             TryPush(pubAck);
//                             break;
//                         case QualityOfService.ExactlyOnce:
//                             var pubRec = publish.ToPubRec();
//                             TryPush(pubRec);
//                             break;
//                     }
//                     
//                     // are there any subscribers to this topic?
//                     if (_subscribedTopics.Contains(publish.TopicName))
//                     {
//                         // if so, we need to propagate this message to them
//                         TryPush(publish);
//                     }
//                     break;
//                 case MqttPacketType.PubAck:
//                 {
//                     // nothing to do here
//                     break;
//                 }
//                 case MqttPacketType.Connect:
//                     var connect = (ConnectPacket) packet;
//                     var connAck = new ConnAckPacket()
//                     {
//                         SessionPresent = true,
//                         ReasonCode = ConnAckReasonCode.Success,
//                         MaximumPacketSize = connect.MaximumPacketSize
//                     };
//                     Push(_stage.Out, connAck);
//                     break;
//                
//                 case MqttPacketType.PingReq:
//                     var pingResp = PingRespPacket.Instance;
//                     Push(_stage.Out, pingResp);
//                     break;
//                 case MqttPacketType.Subscribe:
//                 {
//                     var subscribe = (SubscribePacket) packet;
//                     foreach (var topic in subscribe.Topics)
//                     {
//                         _subscribedTopics.Add(topic.Topic);
//                     }
//
//                     var subAck = subscribe.ToSubAckPacket(subscribe.Topics.Select(c =>
//                     {
//                         // does realistic validation here
//                         if (!MqttTopicValidator.ValidateSubscribeTopic(c.Topic).IsValid)
//                             return MqttSubscribeReasonCode.TopicFilterInvalid;
//
//                         return c.Options.QoS switch
//                         {
//                             QualityOfService.AtMostOnce => MqttSubscribeReasonCode.GrantedQoS0,
//                             QualityOfService.AtLeastOnce => MqttSubscribeReasonCode.GrantedQoS1,
//                             QualityOfService.ExactlyOnce => MqttSubscribeReasonCode.GrantedQoS2,
//                             _ => MqttSubscribeReasonCode.UnspecifiedError
//                         };
//                     }).ToArray());
//                     
//                     TryPush(subAck);
//                     break;
//                 }
//                 case MqttPacketType.PubRec:
//                 {
//                     var pubRec = (PubRecPacket) packet;
//                     var pubRel = pubRec.ToPubRel();
//                     TryPush(pubRel);
//                     break;
//                 }
//                 case MqttPacketType.PubRel:
//                 {
//                     var pubRel = (PubRelPacket) packet;
//                     var pubComp = pubRel.ToPubComp();
//                     TryPush(pubComp);
//                     break;
//                 }
//                 case MqttPacketType.Unsubscribe:
//                 {
//                     var unsubscribe = (UnsubscribePacket) packet;
//                     foreach (var topic in unsubscribe.Topics)
//                     {
//                         _subscribedTopics.Remove(topic);
//                     }
//
//                     var unsubAck = new UnsubAckPacket
//                     {
//                         PacketId = unsubscribe.PacketId,
//                         ReasonCodes = unsubscribe.Topics.Select(c =>
//                         {
//                             if (!MqttTopicValidator.ValidateSubscribeTopic(c).IsValid)
//                                 return MqttUnsubscribeReasonCode.TopicFilterInvalid;
//
//                             return MqttUnsubscribeReasonCode.Success;
//                         }).ToArray()
//                     };
//                     TryPush(unsubAck);
//                     break;
//                 }
//                 case MqttPacketType.Disconnect:
//                     CompleteStage(); // shut it down
//                     break;
//                 default:
//                     throw new NotSupportedException($"Packet type {packet.PacketType} is not supported by this flow.");
//             }
//         }
//
//         public override void OnPull()
//         {
//             if (_pendingPackets.TryDequeue(out var pendingPacket))
//             {
//                 // attempt to push the pending packet
//                 Push(_stage.Out, pendingPacket);
//             }
//             Pull(_stage.In);
//         }
//     }
// }