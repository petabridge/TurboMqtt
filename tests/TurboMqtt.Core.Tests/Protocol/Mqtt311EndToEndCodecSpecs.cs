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
public class Mqtt311EndToEndCodecSpecs
{
    public class SanityChecks()
    {
        private readonly Mqtt311Encoder _encoder = new();
        private readonly Mqtt311Decoder _decoder = new();
        
        [Theory]
        [InlineData(0u)]
        [InlineData(1u)]
        [InlineData(127u)]
        [InlineData(128u)]
        [InlineData(1000u)]
        [InlineData(16384u)]
        public void ShouldEncodeAndDecodeUnsignedShortsCorrectly(ushort value)
        {
            var bytes = new byte[2];
            var buffer = new Memory<byte>(bytes);
            var span = buffer.Span;
            var written = Mqtt311Encoder.WriteUnsignedShort(ref span, value);
            Assert.Equal(2, written);
            
            var readonlyMem = new ReadOnlyMemory<byte>(bytes);
            var remainingLength = 2;
            var decoded = Mqtt311Decoder.DecodeUnsignedShort(ref readonlyMem, ref remainingLength);
            Assert.Equal(value, decoded);
        }
    }
    
    public class MustWorkWithSingleMessage()
    {
        private readonly Mqtt311Encoder _encoder = new();
        private readonly Mqtt311Decoder _decoder = new();
        
        public static readonly TheoryData<ConnectPacket> ConnectPackets = new()
        {
            new ConnectPacket(MqttProtocolVersion.V3_1_1)
            {
                ClientId = "clientId",
                Username = "username",
                Password = "password",
                ProtocolName = "MQTT",
            }
        };
        
        [Theory]
        [MemberData(nameof(ConnectPackets))]
        public void ConnectMessage(ConnectPacket packet)
        {
            var estimatedSize = MqttPacketSizeEstimator.EstimateMqtt3PacketSize(packet);
            var headerSize = MqttPacketSizeEstimator.GetPacketLengthHeaderSize(estimatedSize) + 1; // add 1 for the lead byte
            var buffer = new Memory<byte>(new byte[estimatedSize + headerSize]);
            var actualBytesWritten = _encoder.EncodePacket(packet, ref buffer, estimatedSize);
            actualBytesWritten.Should().Be(estimatedSize + headerSize);

            var span = buffer.Span;
            var decoded = _decoder.TryDecode(buffer, out var packets);
            Assert.True(decoded);
            packets.Count.Should().Be(1);
            ConnectPacket decodedPacket = (ConnectPacket) packets[0];
            packet.Should().BeEquivalentTo(decodedPacket);
        }
    }
}