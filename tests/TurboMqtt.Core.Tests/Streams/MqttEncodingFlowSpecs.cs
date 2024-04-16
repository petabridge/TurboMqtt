// -----------------------------------------------------------------------
// <copyright file="MqttEncodingFlowSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit2;
using TurboMqtt.Core.PacketTypes;
using TurboMqtt.Core.Protocol;
using TurboMqtt.Core.Streams;

namespace TurboMqtt.Core.Tests.Streams;

public class MqttEncodingFlowSpecs : TestKit
{
    [Fact]
    public async Task MqttEncodingFlow_should_encode_single_MqttPacket()
    {
        var connectPacket = new ConnectPacket(MqttProtocolVersion.V3_1_1)
        {
            ClientId = "test", ConnectFlags = new ConnectFlags { CleanSession = true }, ProtocolName = "MQTT"
        };
        var flow = MqttEncodingFlows.Mqtt311Encoding(MemoryPool<byte>.Shared, 1024);

        var bytes = await Source
            .Single<MqttPacket>(connectPacket)
            .Via(flow)
            .Select(c =>
            {
                var byteCount = c.readableBytes;
                // return the memory back to the pool
                c.buffer.Dispose();
                return byteCount;
            })
            .RunAggregate(0, (a, b) => a + b, Sys);

        bytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task MqttEncodingFlow_should_encode_multiple_MqttPackets()
    {
        var connectPacket = new ConnectPacket(MqttProtocolVersion.V3_1_1)
        {
            ClientId = "test", ConnectFlags = new ConnectFlags { CleanSession = true }, ProtocolName = "MQTT"
        };
        var connAckPacket = new ConnAckPacket()
        {
            SessionPresent = true, ReasonCode = ConnAckReasonCode.Success
        };

        var publishPacket1 = new PublishPacket(QualityOfService.AtLeastOnce, false, false, "topic1")
        {
            PacketId = 1,
            Payload = new byte[] { 0x01, 0x02, 0x03 }
        };

        var publishPacket2 = new PublishPacket(QualityOfService.AtLeastOnce, false, false, "topic2")
        {
            PacketId = 2,
            Payload = new byte[] { 0x04, 0x05, 0x06 }
        };

        var packets = new List<MqttPacket>
        {
            connectPacket,
            connAckPacket,
            publishPacket1,
            publishPacket2
        };

        // compute the total packet size
        var totalPayloadSize = packets.Select(MqttPacketSizeEstimator.EstimateMqtt3PacketSize)
            .Select(c => c + MqttPacketSizeEstimator.GetPacketLengthHeaderSize(c) + 1).Sum();

        // set a ridiculous frame size to force the packets to be split up
        var flow = MqttEncodingFlows.Mqtt311Encoding(MemoryPool<byte>.Shared, 1024);

        var bytes = await Source
            .From(packets)
            .Via(flow)
            .Select(c =>
            {
                var byteCount = c.readableBytes;
                // return the memory back to the pool
                c.buffer.Dispose();
                return byteCount;
            })
            .RunAggregate(0, (a, b) => a + b, Sys);

        bytes.Should().Be(totalPayloadSize);
    }
}