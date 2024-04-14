// -----------------------------------------------------------------------
// <copyright file="PubRelPacketMqtt311EndToEndCodecSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.Core.PacketTypes;
using TurboMqtt.Core.Protocol;

namespace TurboMqtt.Core.Tests.Packets.PubPackets;

public class PubRelPacketMqtt311EndToEndCodecSpecs
{
    public class SingleMessage
    {
        private readonly Mqtt311Decoder _decoder = new();

        public static readonly TheoryData<PubRelPacket> PubRelPackets = new()
        {
            new PubRelPacket
            {
                PacketId = 1
            },
            new PubRelPacket
            {
                PacketId = 2
            },
        };

        [Theory]
        [MemberData(nameof(PubRelPackets))]
        public void ShouldEncodeAndDecodeCorrectly(PubRelPacket packet)
        {
            var decodedPacket = PacketEncodingTestHelper.EncodeAndDecodeMqtt311Packet(packet, _decoder);
            decodedPacket.PacketId.Should().Be(packet.PacketId);
        }
    }
}