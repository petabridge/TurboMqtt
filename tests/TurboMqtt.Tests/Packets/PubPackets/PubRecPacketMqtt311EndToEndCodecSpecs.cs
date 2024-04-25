// -----------------------------------------------------------------------
// <copyright file="PubRecPacketMqtt311EndToEndCodecSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Tests.Packets.PubPackets;

public class PubRecPacketMqtt311EndToEndCodecSpecs
{
    public class SingleMessage
    {
        private readonly Mqtt311Decoder _decoder = new();

        public static readonly TheoryData<PubRecPacket> PubRecPackets = new()
        {
            new PubRecPacket
            {
                PacketId = 1
            },
            new PubRecPacket
            {
                PacketId = 2
            },
        };

        [Theory]
        [MemberData(nameof(PubRecPackets))]
        public void ShouldEncodeAndDecodeCorrectly(PubRecPacket packet)
        {
            var decodedPacket = PacketEncodingTestHelper.EncodeAndDecodeMqtt311Packet(packet, _decoder);
            decodedPacket.PacketId.Should().Be(packet.PacketId);
        }
    }
}