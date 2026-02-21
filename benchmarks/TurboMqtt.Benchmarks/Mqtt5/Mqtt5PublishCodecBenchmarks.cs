// -----------------------------------------------------------------------
// <copyright file="Mqtt5PublishCodecBenchmarks.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2025 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Immutable;
using BenchmarkDotNet.Attributes;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Benchmarks.Mqtt5;

[Config(typeof(MicroBenchmarkConfig))]
public class Mqtt5PublishCodecBenchmarks
{
    private readonly Mqtt5Decoder _decoder = new();

    [Params(1024, 2048, 4096, 8192)]
    public int PayloadSize { get; set; }

    private PublishPacket _publishPacket = null!;

    private ReadOnlyMemory<byte> _encodedPublishPacket;
    private PacketSize _estimatedPublishPacketSize;

    [GlobalSetup]
    public void Setup()
    {
        _publishPacket = new PublishPacket(QualityOfService.AtLeastOnce, false, false, "topic1")
        {
            PacketId = 1,
            Payload = new ReadOnlyMemory<byte>(new byte[PayloadSize])
        };
        var estimate = MqttPacketSizeEstimator.EstimateMqtt5PacketSize(_publishPacket);
        var memory = new Memory<byte>(new byte[estimate.TotalSize]);
        _encodedPublishPacket = memory;
        Mqtt5Encoder.EncodePacket(_publishPacket, ref memory, estimate);
        _estimatedPublishPacketSize = estimate;
    }

    private Memory<byte> _writeableBuffer;

    [IterationSetup]
    public void IterationSetup()
    {
        _writeableBuffer = new Memory<byte>(new byte[_estimatedPublishPacketSize.TotalSize]);
    }

    [Benchmark]
    public ImmutableList<MqttPacket> DecodePublishPacket()
    {
        _decoder.TryDecode(_encodedPublishPacket, out var packets);
        return packets;
    }

    [Benchmark]
    public void EncodePublishPacket()
    {
        Mqtt5Encoder.EncodePacket(_publishPacket, ref _writeableBuffer, _estimatedPublishPacketSize);
    }
}
