// -----------------------------------------------------------------------
// <copyright file="ConnectPacketMqtt311Specs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.Core.PacketTypes;
using TurboMqtt.Core.Protocol;

namespace TurboMqtt.Core.Tests.Packets.Connect;

public class ConnectPacketMqtt311Specs
{
    public class WhenCreatingConnectPacket
    {
        [Fact]
        public void should_have_correct_packet_type()
        {
            var packet = new ConnectPacket("clientId", MqttProtocolVersion.V3_1_1);
            packet.PacketType.Should().Be(MqttPacketType.Connect);
        }

        [Fact]
        public void should_have_correct_protocol_version()
        {
            var packet = new ConnectPacket("clientId", MqttProtocolVersion.V3_1_1);
            packet.ProtocolVersion.Should().Be(MqttProtocolVersion.V3_1_1);
        }

        [Fact]
        public void should_have_correct_client_id()
        {
            var packet = new ConnectPacket("clientId", MqttProtocolVersion.V3_1_1);
            packet.ClientId.Should().Be("clientId");
        }

        [Fact]
        public void should_have_correct_keep_alive()
        {
            var packet = new ConnectPacket("clientId", MqttProtocolVersion.V3_1_1);
            packet.KeepAlive.Should().Be(0);
        }

        [Fact]
        public void should_have_correct_flags()
        {
            var packet = new ConnectPacket("clientId", MqttProtocolVersion.V3_1_1);
            packet.Flags.Should().Be(new ConnectFlags());
        }

        [Fact]
        public void should_have_correct_will()
        {
            var packet = new ConnectPacket("clientId", MqttProtocolVersion.V3_1_1);
            packet.Will.Should().BeNull();
        }

        [Fact]
        public void should_have_correct_username()
        {
            var packet = new ConnectPacket("clientId", MqttProtocolVersion.V3_1_1);
            packet.Username.Should().BeNull();
        }

        [Fact]
        public void should_have_correct_password()
        {
            var packet = new ConnectPacket("clientId", MqttProtocolVersion.V3_1_1);
            packet.Password.Should().BeNull();
        }

        [Fact]
        public void should_have_correct_receive_maximum()
        {
            var packet = new ConnectPacket("clientId", MqttProtocolVersion.V3_1_1);
            packet.ReceiveMaximum.Should().Be(0);
        }

        [Fact]
        public void should_have_correct_maximum_packet_size()
        {
            var packet = new ConnectPacket("clientId", MqttProtocolVersion.V3_1_1);
            packet.MaximumPacketSize.Should().Be(0);
        }

        [Fact]
        public void should_have_correct_topic_alias_maximum()
        {
            var packet = new ConnectPacket("clientId", MqttProtocolVersion.V3_1_1);
            packet.TopicAliasMaximum.Should().Be(0);
        }
    }
    
    // create a test case for working with LastWillAndTestament
    public class WhenCreatingLastWillAndTestament
    {
        [Fact] public void should_have_correct_payload()
        {
            var connectPacket = new ConnectPacket("clientId", MqttProtocolVersion.V3_1_1)
            {
                Will = new MqttLastWill("topic", new byte[] { 1, 2, 3 })
            };
            connectPacket.Will.Topic.Should().Be("topic");
            connectPacket.Will.Message.ToArray().Should().BeEquivalentTo(new byte[] { 1, 2, 3 });
        }
    }
    
    // create test cases for estimating the size of the packet
    public class WhenEstimatingPacketSize
    {
        [Fact] public void should_estimate_correct_size()
        {
            var connectPacket = new ConnectPacket("clientId", MqttProtocolVersion.V3_1_1)
            {
                Username = "username",
                Password = new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3 }),
                Will = new MqttLastWill("topic", new byte[] { 1, 2, 3 }),
                ReceiveMaximum = 10, // should be ignored - only supported in MQTT 5.0
                MaximumPacketSize = 100, // should be ignored - only supported in MQTT 5.0
                TopicAliasMaximum = 5, // should be ignored - only supported in MQTT 5.0
            };
            
            MqttPacketSizeEstimator.EstimatePacketSize(connectPacket, MqttProtocolVersion.V3_1_1).Should().Be(51);
        }
        
        // estimate the packet size without username and password
        [Fact] public void should_estimate_correct_size_without_username_password()
        {
            var connectPacket = new ConnectPacket("clientId", MqttProtocolVersion.V3_1_1)
            {
                Will = new MqttLastWill("topic", new byte[] { 1, 2, 3 }),
                ReceiveMaximum = 10, // should be ignored - only supported in MQTT 5.0
                MaximumPacketSize = 100, // should be ignored - only supported in MQTT 5.0
                TopicAliasMaximum = 5, // should be ignored - only supported in MQTT 5.0
            };
            
            MqttPacketSizeEstimator.EstimatePacketSize(connectPacket, MqttProtocolVersion.V3_1_1).Should().Be(36);
        }
    }
}