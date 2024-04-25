// -----------------------------------------------------------------------
// <copyright file="UnsubscribePacketMqtt3111End2EndCodecSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Tests.Packets.UnsubscribePackets;

public class UnsubscribePacketMqtt3111End2EndCodecSpecs
{
    public class SingleMessage
    {
        private readonly Mqtt311Decoder _decoder = new();
        
        public static readonly TheoryData<UnsubscribePacket> UnsubscribePackets = new()
        {
            new UnsubscribePacket
            {
                PacketId = 1,
                Topics = new List<string> {"topic1"}
            },
            new UnsubscribePacket
            {
                PacketId = 2,
                Topics = new List<string> {"topic1", "topic2"}
            },
            new UnsubscribePacket
            {
                PacketId = 3,
                Topics = new List<string> {"topic1", "topic2", "topic3"}
            },
            new UnsubscribePacket
            {
                PacketId = 4,
                Topics = new List<string> {"topic1", "topic2", "topic3", "topic4"}
            },
            new UnsubscribePacket
            {
                PacketId = 5,
                Topics = new List<string> {"topic1", "topic2", "topic3", "topic4", "topic5"}
            },
        };

        [Theory]
        [MemberData(nameof(UnsubscribePackets))]
        public void ShouldEncodeAndDecodeCorrectly(UnsubscribePacket packet)
        {
            var decodedPacket = PacketEncodingTestHelper.EncodeAndDecodeMqtt311Packet(packet, _decoder);
            decodedPacket.PacketType.Should().Be(packet.PacketType);
        }
    }
}