// -----------------------------------------------------------------------
// <copyright file="MqttDecodingFlowSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using System.Collections.Immutable;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit2;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;
using TurboMqtt.Streams;

namespace TurboMqtt.Tests.Streams;

public class MqttDecodingFlowSpecs : TestKit
{
    [Fact]
    public async Task MqttDecodingFlow_should_decode_single_MqttPacket()
    {
        var connectPacket = new ConnectPacket(MqttProtocolVersion.V3_1_1)
        {
            ClientId = "test", ConnectFlags = new ConnectFlags { CleanSession = true }, ProtocolName = "MQTT"
        };
        
        var encodingFlow = MqttEncodingFlows.Mqtt311Encoding(MemoryPool<byte>.Shared, 1024, 1024);
        var decodingFlow = MqttDecodingFlows.Mqtt311Decoding();

        var processedPackets = await Source
            .Single<MqttPacket>(connectPacket)
            .Via(encodingFlow)
            .Via(decodingFlow)
            .Select(c => 1)
            .RunAggregate(0, (a, b) => a + 1, Sys);

        processedPackets.Should().Be(1);
    }

    [Fact]
    public async Task MqttDecodingFlow_should_decode_multiple_MqttPackets()
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

        var encodingFlow = MqttEncodingFlows.Mqtt311Encoding(MemoryPool<byte>.Shared, 1024, 1024);
        var decodingFlow = MqttDecodingFlows.Mqtt311Decoding();

        var decodedPackets = await Source
            .From(new MqttPacket[] { connectPacket, connAckPacket, publishPacket1 })
            .Via(encodingFlow)
            .Via(decodingFlow)
            .RunAggregate(ImmutableList<MqttPacket>.Empty, (a, b) => a.AddRange(b), Sys);

        decodedPackets.Count.Should().Be(3);
    }
}