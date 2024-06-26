// -----------------------------------------------------------------------
// <copyright file="ConnectPacketMqtt311Specs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Tests.Packets.Connect;

public class ConnectPacketMqtt311Specs
{
    public class WhenCreatingConnectPacket
    {
        [Fact]
        public void should_have_correct_packet_type()
        {
            var packet = new ConnectPacket(MqttProtocolVersion.V3_1_1);
            packet.PacketType.Should().Be(MqttPacketType.Connect);
        }

        [Fact]
        public void should_have_correct_protocol_version()
        {
            var packet = new ConnectPacket(MqttProtocolVersion.V3_1_1);
            packet.ProtocolVersion.Should().Be(MqttProtocolVersion.V3_1_1);
        }

        [Fact]
        public void should_have_correct_client_id()
        {
            var packet = new ConnectPacket(MqttProtocolVersion.V3_1_1);
            packet.ClientId.Should().Be(ConnectPacket.DefaultClientId);
        }

        [Fact]
        public void should_have_correct_keep_alive()
        {
            var packet = new ConnectPacket(MqttProtocolVersion.V3_1_1);
            packet.KeepAliveSeconds.Should().Be(0);
        }

        [Fact]
        public void should_have_correct_flags()
        {
            var packet = new ConnectPacket(MqttProtocolVersion.V3_1_1);
            packet.Flags.Should().Be(new ConnectFlags());
        }

        [Fact]
        public void should_have_correct_will()
        {
            var packet = new ConnectPacket(MqttProtocolVersion.V3_1_1);
            packet.Will.Should().BeNull();
        }

        [Fact]
        public void should_have_correct_username()
        {
            var packet = new ConnectPacket(MqttProtocolVersion.V3_1_1);
            packet.UserName.Should().BeNull();
        }

        [Fact]
        public void should_have_correct_password()
        {
            var packet = new ConnectPacket(MqttProtocolVersion.V3_1_1);
            packet.Password.Should().BeNull();
        }

        [Fact]
        public void should_have_correct_receive_maximum()
        {
            var packet = new ConnectPacket(MqttProtocolVersion.V3_1_1);
            packet.ReceiveMaximum.Should().Be(0);
        }

        [Fact]
        public void should_have_correct_maximum_packet_size()
        {
            var packet = new ConnectPacket(MqttProtocolVersion.V3_1_1);
            packet.MaximumPacketSize.Should().Be(0);
        }

        [Fact]
        public void should_have_correct_topic_alias_maximum()
        {
            var packet = new ConnectPacket(MqttProtocolVersion.V3_1_1);
            packet.TopicAliasMaximum.Should().Be(0);
        }
    }
    
    // create a test case for working with LastWillAndTestament
    public class WhenCreatingLastWillAndTestament
    {
        [Fact] public void should_have_correct_payload()
        {
            var connectPacket = new ConnectPacket(MqttProtocolVersion.V3_1_1)
            {
                Will = new MqttLastWill("topic", new byte[] { 1, 2, 3 })
            };
            connectPacket.Will.Topic.Should().Be("topic");
            connectPacket.Will.Message.ToArray().Should().BeEquivalentTo(new byte[] { 1, 2, 3 });
        }
    }

    public class WhenSettingUserNameAndPassword()
    {
        [Fact] public void should_set_username_and_password()
        {
            var connectPacket = new ConnectPacket(MqttProtocolVersion.V3_1_1)
            {
                UserName = "username",
                Password = "password"
            };
            connectPacket.UserName.Should().Be("username");
            connectPacket.Flags.UsernameFlag.Should().BeTrue();
            connectPacket.Password.Should().Be("password");
            connectPacket.Flags.PasswordFlag.Should().BeTrue();
        }
    }
    
    // create test cases for estimating the size of the packet
    public class WhenEstimatingPacketSize
    {
        [Fact] public void should_estimate_correct_size()
        {
            var connectPacket = new ConnectPacket(MqttProtocolVersion.V3_1_1)
            {
                ClientId = "clientId", 
                UserName = "username",
                Password = "password",
                Will = new MqttLastWill("topic", new byte[] { 1, 2, 3 }),
                ReceiveMaximum = 10, // should be ignored - only supported in MQTT 5.0
                MaximumPacketSize = 100, // should be ignored - only supported in MQTT 5.0
                TopicAliasMaximum = 5, // should be ignored - only supported in MQTT 5.0
            };
            
            MqttPacketSizeEstimator.EstimatePacketSize(connectPacket, MqttProtocolVersion.V3_1_1).Should().Be(new PacketSize(52));
        }
        
        // estimate the packet size without username and password
        [Fact] public void should_estimate_correct_size_without_username_password()
        {
            var connectPacket = new ConnectPacket(MqttProtocolVersion.V3_1_1)
            {
                ClientId = "clientId",
                Will = new MqttLastWill("topic", new byte[] { 1, 2, 3 }),
                ReceiveMaximum = 10, // should be ignored - only supported in MQTT 5.0
                MaximumPacketSize = 100, // should be ignored - only supported in MQTT 5.0
                TopicAliasMaximum = 5, // should be ignored - only supported in MQTT 5.0
            };
            
            MqttPacketSizeEstimator.EstimatePacketSize(connectPacket, MqttProtocolVersion.V3_1_1).Should().Be(new PacketSize(32));
        }
    }
}