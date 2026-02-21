// -----------------------------------------------------------------------
// <copyright file="ConnectPacketMqtt5Specs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Tests.Packets.Connect;

public class ConnectPacketMqtt5Specs
{
    public class WhenCreatingConnectPacket
    {
        [Fact]
        public void should_have_correct_packet_type()
        {
            var packet = new ConnectPacket(MqttProtocolVersion.V5_0);
            packet.PacketType.Should().Be(MqttPacketType.Connect);
        }

        [Fact]
        public void should_have_correct_protocol_version()
        {
            var packet = new ConnectPacket(MqttProtocolVersion.V5_0);
            packet.ProtocolVersion.Should().Be(MqttProtocolVersion.V5_0);
        }

        [Fact]
        public void should_have_correct_client_id()
        {
            var packet = new ConnectPacket(MqttProtocolVersion.V5_0);
            packet.ClientId.Should().Be("turbomqtt");
        }

        [Fact]
        public void should_have_correct_keep_alive()
        {
            var packet = new ConnectPacket(MqttProtocolVersion.V5_0);
            packet.KeepAliveSeconds.Should().Be(0);
        }

        [Fact]
        public void should_have_correct_flags()
        {
            var packet = new ConnectPacket(MqttProtocolVersion.V5_0);
            packet.Flags.Should().Be(new ConnectFlags());
        }

        [Fact]
        public void should_have_correct_will()
        {
            var packet = new ConnectPacket(MqttProtocolVersion.V5_0);
            packet.Will.Should().BeNull();
        }

        [Fact]
        public void should_have_correct_username()
        {
            var packet = new ConnectPacket(MqttProtocolVersion.V5_0);
            packet.UserName.Should().BeNull();
        }

        [Fact]
        public void should_have_correct_password()
        {
            var packet = new ConnectPacket(MqttProtocolVersion.V5_0);
            packet.Password.Should().BeNull();
        }

        [Fact]
        public void should_have_correct_properties()
        {
            var packet = new ConnectPacket(MqttProtocolVersion.V5_0);
            packet.UserProperties.Should().BeNull();
        }
    }

    // create some specs for MQTT5 packet size estimation
    public class WhenEstimatingPacketSize
    {
        [Fact]
        public void should_estimate_correct_packet_size()
        {
            var packet = new ConnectPacket(MqttProtocolVersion.V5_0);
            packet.ClientId = "clientId";
            packet.ProtocolName = "MQTT";
            // Variable header: protocolName(6) + version(1) + flags(1) + keepAlive(2) + propsVBI(1) + connectProps(17) = 28
            // connectProps: SEI(5)+MaxPktSz(5)+TopAlias(3)+RRI(2)+RPI(2)=17 (ReceiveMaximum omitted when 0: §3.1.2.11.3)
            // Payload: clientId "clientId" = 2+8 = 10; Total content = 38
            MqttPacketSizeEstimator.EstimatePacketSize(packet, MqttProtocolVersion.V5_0).Should().Be(new PacketSize(38));
        }

        [Fact]
        public void should_estimate_correct_packet_size_with_properties()
        {
            var packet = new ConnectPacket(MqttProtocolVersion.V5_0)
            {
                UserProperties = new Dictionary<string, string>
                {
                    { "key1", "value1" },
                    { "key2", "value2" }
                }
            };
            packet.ClientId = "clientId";
            packet.ProtocolName = "MQTT";

            // connectProps = 17 (fixed, ReceiveMaximum omitted per §3.1.2.11.3) + 30 (2 user props x 15 each) = 47; propsVBI = 1
            // Variable header: 6+1+1+2+1+47 = 58; Payload: 2+8 = 10; Total content = 68
            MqttPacketSizeEstimator.EstimatePacketSize(packet, MqttProtocolVersion.V5_0).Should().Be(new PacketSize(68));
        }

        [Fact]
        public void should_estimate_correct_packet_size_with_will()
        {
            var packet = new ConnectPacket(MqttProtocolVersion.V5_0)
            {
                Will = new MqttLastWill("topic", new byte[] { 1, 2, 3, 4 })
                {
                    PayloadFormatIndicator = PayloadFormatIndicator.Utf8Encoded,
                    ContentType = "text/plain",
                },
                UserProperties = new Dictionary<string, string>
                {
                    { "key1", "value1" },
                    { "key2", "value2" }
                },
                Flags = new ConnectFlags
                {
                    WillFlag = true,
                    WillQoS = QualityOfService.AtLeastOnce,
                    WillRetain = true
                }
            };
            packet.ClientId = "clientId";
            packet.ProtocolName = "MQTT";
            

            // connectProps = 17 (fixed, ReceiveMaximum omitted per §3.1.2.11.3) + 30 (2 user props) = 47; willProps = 2 (payloadFmt) + 13 (contentType) = 15
            // Variable header: 6+1+1+2+propsVBI(1)+47 = 58
            // Payload: clientId(10) + willPropsVBI(1)+willProps(15) + willTopic(7) + willPayload(6) = 39
            // Total content = 97
            MqttPacketSizeEstimator.EstimatePacketSize(packet, MqttProtocolVersion.V5_0).Should().Be(new PacketSize(97));
        }
    }
}