// -----------------------------------------------------------------------
// <copyright file="PingReqPacketMqtt311End2EndCodecSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Tests.Packets.PingPackets;

public class PingReqPacketMqtt311End2EndCodecSpecs
{
    public class SingleMessage
    {
        private readonly Mqtt311Decoder _decoder = new();
        

        [Fact]
        public void ShouldEncodeAndDecodeCorrectly()
        {
            var packet = PingReqPacket.Instance;
            var decodedPacket = PacketEncodingTestHelper.EncodeAndDecodeMqtt311Packet(packet, _decoder);
            decodedPacket.PacketType.Should().Be(packet.PacketType);
        }
    }
}