// -----------------------------------------------------------------------
// <copyright file="SubscribePacketMqtt311EndToEndCodecSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.Core.PacketTypes;
using TurboMqtt.Core.Protocol;

namespace TurboMqtt.Core.Tests.Packets.SubscribePackets;

public class SubscribePacketMqtt311EndToEndCodecSpecs
{
    public class SingleMessage
    {
        private readonly Mqtt311Decoder _decoder = new();

        public static readonly TheoryData<SubscribePacket> SubscribePackets = new()
        {
            // simple case
            new SubscribePacket
            {
                PacketId = 1,
                Topics = new List<TopicSubscription>
                {
                    new TopicSubscription("topic1")
                    {
                        
                    }
                }
            },
            
            // subscription with QoS
            new SubscribePacket
            {
                PacketId = 2,
                Topics = new List<TopicSubscription>
                {
                    new TopicSubscription("topic1")
                    {
                        Options = new SubscriptionOptions()
                        {
                            // all other options aren't supported in MQTT 3.1.1
                            QoS = QualityOfService.AtLeastOnce
                        }
                    }
                }
            },
            
            // multiple subscriptions with different QoS
            new SubscribePacket
            {
                PacketId = 3,
                Topics = new List<TopicSubscription>
                {
                    new TopicSubscription("topic1")
                    {
                        Options = new SubscriptionOptions()
                        {
                            // all other options aren't supported in MQTT 3.1.1
                            QoS = QualityOfService.AtLeastOnce
                        }
                    },
                    new TopicSubscription("topic2")
                    {
                        Options = new SubscriptionOptions()
                        {
                            // all other options aren't supported in MQTT 3.1.1
                            QoS = QualityOfService.ExactlyOnce
                        }
                    }
                }
            },
        };

        [Theory]
        [MemberData(nameof(SubscribePackets))]
        public void ShouldEncodeAndDecodeCorrectly(SubscribePacket packet)
        {
            var decodedPacket = PacketEncodingTestHelper.EncodeAndDecodeMqtt311Packet(packet, _decoder);
            decodedPacket.PacketId.Should().Be(packet.PacketId);
            decodedPacket.Topics.Should().BeEquivalentTo(packet.Topics);
        }
    }
}