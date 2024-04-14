// -----------------------------------------------------------------------
// <copyright file="PubAckPacketMqtt311EndToEndCodecSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.Core.PacketTypes;
using TurboMqtt.Core.Protocol;

namespace TurboMqtt.Core.Tests.Packets.PubPackets;

public sealed class PubAckPacketMqtt311EndToEndCodecSpecs
{
    public class SingleMessage
    {
        private readonly Mqtt311Decoder _decoder = new();

        public static readonly TheoryData<PubAckPacket> PubAckPackets = new()
        {
            new PubAckPacket
            {
                PacketId = 1
            },
            new PubAckPacket
            {
                PacketId = 2
            },
        };

        [Theory]
        [MemberData(nameof(PubAckPackets))]
        public void ShouldEncodeAndDecodeCorrectly(PubAckPacket packet)
        {
            var decodedPacket = PacketEncodingTestHelper.EncodeAndDecodeMqtt311Packet(packet, _decoder);
            decodedPacket.PacketId.Should().Be(packet.PacketId);
        }
    }
}