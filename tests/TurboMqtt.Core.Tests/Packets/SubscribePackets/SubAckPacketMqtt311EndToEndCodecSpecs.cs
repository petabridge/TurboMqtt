// -----------------------------------------------------------------------
// <copyright file="SubAckPacketMqtt311EndToEndCodecSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.Core.PacketTypes;
using TurboMqtt.Core.Protocol;

namespace TurboMqtt.Core.Tests.Packets.SubscribePackets;

public class SubAckPacketMqtt311EndToEndCodecSpecs
{
    public class SingleMessage
    {
        private readonly Mqtt311Decoder _decoder = new();

        public static readonly TheoryData<SubAckPacket> SubAckPackets = new()
        {
            new SubAckPacket
            {
                PacketId = 1,
                ReasonCodes = new List<MqttSubscribeReasonCode>
                {
                    MqttSubscribeReasonCode.GrantedQoS0
                }
            },
            new SubAckPacket
            {
                PacketId = 2,
                ReasonCodes = new List<MqttSubscribeReasonCode>
                {
                    MqttSubscribeReasonCode.GrantedQoS0,
                    MqttSubscribeReasonCode.GrantedQoS1,
                }
            },
            new SubAckPacket
            {
                PacketId = 3,
                ReasonCodes = new List<MqttSubscribeReasonCode>
                {
                    MqttSubscribeReasonCode.GrantedQoS0,
                    MqttSubscribeReasonCode.GrantedQoS1,
                    MqttSubscribeReasonCode.GrantedQoS2,
                }
            },
            new SubAckPacket
            {
                PacketId = 4,
                ReasonCodes = new List<MqttSubscribeReasonCode>
                {
                    MqttSubscribeReasonCode.GrantedQoS1,
                    MqttSubscribeReasonCode.GrantedQoS2,
                }
            },
            new SubAckPacket
            {
                PacketId = 5,
                ReasonCodes = new List<MqttSubscribeReasonCode>
                {
                    MqttSubscribeReasonCode.UnspecifiedError
                }
            }
        };

        [Theory]
        [MemberData(nameof(SubAckPackets))]
        public void ShouldEncodeAndDecodeCorrectly(SubAckPacket packet)
        {
            var decodedPacket = PacketEncodingTestHelper.EncodeAndDecodeMqtt311Packet(packet, _decoder);
            decodedPacket.PacketId.Should().Be(packet.PacketId);
            decodedPacket.ReasonCodes.Should().BeEquivalentTo(packet.ReasonCodes);
        }
    }
}