// -----------------------------------------------------------------------
// <copyright file="ConnAckPacketMqtt311EndToEndCodecSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Tests.Packets.ConnAck;

public class ConnAckPacketMqtt311EndToEndCodecSpecs
{
    public class MustWorkWithSingleMessage()
    {
        private readonly Mqtt311Decoder _decoder = new();

        public static readonly TheoryData<ConnAckPacket> ConnAckPackets = new()
        {
            // happy path case
            new ConnAckPacket(){ SessionPresent = true, ReasonCode = ConnAckReasonCode.Success },
            
            // sad path cases
            new ConnAckPacket(){ SessionPresent = false, ReasonCode = ConnAckReasonCode.QuotaExceeded },
            new ConnAckPacket(){ SessionPresent = false, ReasonCode = ConnAckReasonCode.NotAuthorized },
            new ConnAckPacket(){ SessionPresent = false, ReasonCode = ConnAckReasonCode.ServerUnavailable }
        };
        
        [Theory]
        [MemberData(nameof(ConnAckPackets))]
        public void ShouldEncodeAndDecodeConnAckPacket(ConnAckPacket packet)
        {
            var decodedPacket = PacketEncodingTestHelper.EncodeAndDecodeMqtt311Packet(packet, _decoder);
            decodedPacket.Should().BeEquivalentTo(packet);
        }
    }
}