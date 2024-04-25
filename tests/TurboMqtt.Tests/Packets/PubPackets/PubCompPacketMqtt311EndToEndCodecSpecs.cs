// -----------------------------------------------------------------------
// <copyright file="PubCompPacketMqtt311EndToEndCodecSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Tests.Packets.PubPackets;

public class PubCompPacketMqtt311EndToEndCodecSpecs
{
    public class SingleMessage
    {
        private readonly Mqtt311Decoder _decoder = new();

        public static readonly TheoryData<PubCompPacket> PubCompPackets = new()
        {
            new PubCompPacket
            {
                PacketId = 1
            },
            new PubCompPacket
            {
                PacketId = 2
            },
        };

        [Theory]
        [MemberData(nameof(PubCompPackets))]
        public void ShouldEncodeAndDecodeCorrectly(PubCompPacket packet)
        {
            var decodedPacket = PacketEncodingTestHelper.EncodeAndDecodeMqtt311Packet(packet, _decoder);
            decodedPacket.PacketId.Should().Be(packet.PacketId);
        }
    }
}