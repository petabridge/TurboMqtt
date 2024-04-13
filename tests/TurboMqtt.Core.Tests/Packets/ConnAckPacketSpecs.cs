// -----------------------------------------------------------------------
// <copyright file="ConnAckPacketSpecs311.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.Core.PacketTypes;
using TurboMqtt.Core.Protocol;

namespace TurboMqtt.Core.Tests.Packets;

public class ConnAckPacketSpecs
{
    public class WhenCreatingConnAckPacket
    {
        [Fact]
        public void ShouldHaveCorrectPacketType()
        {
            var packet = new ConnAckPacket();
            packet.PacketType.Should().Be(MqttPacketType.ConnAck);
        }

        [Fact]
        public void ShouldHaveCorrectSessionPresent()
        {
            var packet = new ConnAckPacket();
            packet.SessionPresent.Should().BeFalse();
        }

        [Fact]
        public void ShouldHaveCorrectReasonCode()
        {
            var packet = new ConnAckPacket();
            packet.ReasonCode.Should().Be(ConnAckReasonCode.Success);
        }

        [Fact]
        public void ShouldHaveCorrectProperties()
        {
            var packet = new ConnAckPacket();
            packet.UserProperties.Should().BeNull();
        }
    }
    
    // create size estimator specs for MQTT 3.1.1 ConnAck
    public class WhenEstimatingSizeInMqtt311
    {
        [Theory]
        [InlineData(true, ConnAckReasonCode.Success)]
        [InlineData(false, ConnAckReasonCode.QuotaExceeded)] // no need to test all cases
        public void ShouldEstimateSizeCorrectly(bool sessionCreated, ConnAckReasonCode reasonCode)
        {
            var packet = new ConnAckPacket()
            {
                SessionPresent = sessionCreated,
                ReasonCode = reasonCode
            };
            MqttPacketSizeEstimator.EstimatePacketSize(packet, MqttProtocolVersion.V3_1_1).Should().Be(4);
        }
    }
    
    // create size estimator specs for MQTT 5.0 ConnAck
    public class WhenEstimatingSizeInMqtt5
    {
        [Theory]
        [InlineData(true, ConnAckReasonCode.Success)]
        [InlineData(false, ConnAckReasonCode.QuotaExceeded)] // no need to test all cases
        public void ShouldEstimateSizeCorrectly(bool sessionCreated, ConnAckReasonCode reasonCode)
        {
            var packet = new ConnAckPacket()
            {
                SessionPresent = sessionCreated,
                ReasonCode = reasonCode
            };
            MqttPacketSizeEstimator.EstimatePacketSize(packet, MqttProtocolVersion.V5_0).Should().Be(4);
        }
        
        [Fact]
        public void ShouldEstimateSizeCorrectlyWithProperties()
        {
            var packet = new ConnAckPacket()
            {
                SessionPresent = true,
                ReasonCode = ConnAckReasonCode.Success,
                UserProperties = new Dictionary<string, string>
                {
                    { "key1", "value1" },
                    { "key2", "value2" }
                }
            };
            MqttPacketSizeEstimator.EstimatePacketSize(packet, MqttProtocolVersion.V5_0).Should().Be(34);
        }
    }
    
}