// -----------------------------------------------------------------------
// <copyright file="UnsubAckPacketMqt311End2EndCodecSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.Core.PacketTypes;
using TurboMqtt.Core.Protocol;

namespace TurboMqtt.Core.Tests.Packets.UnsubscribePackets;

public class UnsubAckPacketMqt311End2EndCodecSpecs
{
    public class SingleMessage
    {
        private readonly Mqtt311Decoder _decoder = new();
        
        [Fact]
        public void ShouldEncodeAndDecodeCorrectly()
        {
            var packet = new UnsubAckPacket()
            {
                PacketId = 1
            };
            var decodedPacket = PacketEncodingTestHelper.EncodeAndDecodeMqtt311Packet(packet, _decoder);
            decodedPacket.PacketType.Should().Be(packet.PacketType);
            decodedPacket.PacketId.Should().Be(packet.PacketId);
        }
    }
}