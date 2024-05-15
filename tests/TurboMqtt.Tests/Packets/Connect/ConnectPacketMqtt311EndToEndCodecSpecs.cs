// -----------------------------------------------------------------------
// <copyright file="Mqtt311EndToEndCodecSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Tests.Packets.Connect;

/// <summary>
/// Tests encoding and decoding of messages using the MQTT 3.1.1 protocol.
/// </summary>
public class ConnectPacketMqtt311EndToEndCodecSpecs
{
    public class MustWorkWithSingleMessage()
    {
        private readonly Mqtt311Decoder _decoder = new();

        public static readonly TheoryData<ConnectPacket> ConnectPackets = new()
        {
            // basic case with no username or password - about as simple as it gets
            new ConnectPacket(MqttProtocolVersion.V3_1_1)
            {
                ClientId = "customClientId",
                ProtocolName = "MQTT",
            },

            // user name and password
            new ConnectPacket(MqttProtocolVersion.V3_1_1)
            {
                ClientId = "clientId",
                UserName = "username",
                Password = "password",
                ProtocolName = "MQTT",
            },

            // user name and password with last will and testament
            new ConnectPacket(MqttProtocolVersion.V3_1_1)
            {
                ClientId = "clientId",
                UserName = "username",
                Password = "password",
                ProtocolName = "MQTT",
                Will = new MqttLastWill("topic", new ReadOnlyMemory<byte>(new byte[] { 0, 1, 2, 3 }))
                {
                },
                Flags = new ConnectFlags
                {
                    WillFlag = true,
                    WillQoS = QualityOfService.AtLeastOnce,
                    WillRetain = true,
                    UsernameFlag = true,
                    PasswordFlag = true
                }
            },
            
            // custom keep alive
            new ConnectPacket(MqttProtocolVersion.V3_1_1)
            {
                ClientId = "clientId",
                UserName = "username",
                Password = "password",
                ProtocolName = "MQTT",
                KeepAliveSeconds = 100,
                Will = new MqttLastWill("topic", new ReadOnlyMemory<byte>(new byte[] { 0, 1, 2, 3 }))
                {
                },
                Flags = new ConnectFlags
                {
                    WillFlag = true,
                    WillQoS = QualityOfService.AtLeastOnce,
                    WillRetain = true,
                    UsernameFlag = true,
                    PasswordFlag = true
                }
            },
            
            // enable clean session
            new ConnectPacket(MqttProtocolVersion.V3_1_1)
            {
                ClientId = "clientId",
                UserName = "username",
                Password = "password",
                ProtocolName = "MQTT",
                KeepAliveSeconds = 100,
                Will = new MqttLastWill("topic", new ReadOnlyMemory<byte>(new byte[] { 0, 1, 2, 3 }))
                {
                },
                Flags = new ConnectFlags
                {
                    WillFlag = true,
                    WillQoS = QualityOfService.AtLeastOnce,
                    WillRetain = true,
                    UsernameFlag = true,
                    PasswordFlag = true,
                    CleanSession = true
                }
            },
        };

        [Theory]
        [MemberData(nameof(ConnectPackets))]
        public void ConnectMessage(ConnectPacket packet)
        {
            var decodedPacket = PacketEncodingTestHelper.EncodeAndDecodeMqtt311Packet(packet, _decoder);
            decodedPacket.Should().BeEquivalentTo(packet, options => options.Excluding(x => x.Will!.Message));
            
            // check Will payloads
            if (packet.Will != null)
            {
                decodedPacket.Will!.Message.ToArray().Should().BeEquivalentTo(packet.Will.Message.ToArray());
            }
        }
    }
}