// -----------------------------------------------------------------------
// <copyright file="Mqtt311EndToEndCodecSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.Core.PacketTypes;
using TurboMqtt.Core.Protocol;

namespace TurboMqtt.Core.Tests.Protocol;

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
                Username = "username",
                Password = "password",
                ProtocolName = "MQTT",
            },

            // user name and password with last will and testament
            new ConnectPacket(MqttProtocolVersion.V3_1_1)
            {
                ClientId = "clientId",
                Username = "username",
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
                Username = "username",
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
            }
        };

        [Theory]
        [MemberData(nameof(ConnectPackets))]
        public void ConnectMessage(ConnectPacket packet)
        {
            var estimatedSize = MqttPacketSizeEstimator.EstimateMqtt3PacketSize(packet);
            var headerSize =
                MqttPacketSizeEstimator.GetPacketLengthHeaderSize(estimatedSize) + 1; // add 1 for the lead byte
            var buffer = new Memory<byte>(new byte[estimatedSize + headerSize]);
            var actualBytesWritten = Mqtt311Encoder.EncodePacket(packet, ref buffer, estimatedSize);
            actualBytesWritten.Should().Be(estimatedSize + headerSize);

            var span = buffer.Span;
            var decoded = _decoder.TryDecode(buffer, out var packets);
            Assert.True(decoded);
            packets.Count.Should().Be(1);
            ConnectPacket decodedPacket = (ConnectPacket)packets[0];
            decodedPacket.Should().BeEquivalentTo(packet, options => options.Excluding(x => x.Will!.Message));
            
            // check Will payloads
            if (packet.Will != null)
            {
                decodedPacket.Will!.Message.ToArray().Should().BeEquivalentTo(packet.Will.Message.ToArray());
            }
        }
    }
}