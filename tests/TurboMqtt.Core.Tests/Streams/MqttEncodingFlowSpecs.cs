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
    public async Task MqttEncodingFlow_should_encode_MqttPackets()
    {
        var flow = MqttEncodingFlows.Mqtt311Encoding(MemoryPool<byte>.Shared, 100);

        var bytes = await Source
            .Single<MqttPacket>(new ConnectPacket(MqttProtocolVersion.V3_1_1) { ClientId = "test" })
            .Via(flow)
            .Select(c =>
            {
                var byteCount = c.Memory.Length;
                // return the memory back to the pool
                c.Dispose();
                return byteCount;
            })
            .RunAggregate(0, (a, b) => a + b, Sys);
        
        bytes.Should().BeGreaterThan(0);
    }
}