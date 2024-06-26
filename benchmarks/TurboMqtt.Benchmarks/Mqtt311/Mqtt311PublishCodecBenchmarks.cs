// -----------------------------------------------------------------------
// <copyright file="Mqtt311PublishCodecBenchmarks.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Immutable;
using BenchmarkDotNet.Attributes;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Benchmarks.Mqtt311;

[Config(typeof(MicroBenchmarkConfig))]
public class Mqtt311PublishCodecBenchmarks
{
    private readonly Mqtt311Decoder _decoder = new();
    
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
        var estimate = MqttPacketSizeEstimator.EstimateMqtt3PacketSize(_publishPacket);
        var memory = new Memory<byte>(new byte[estimate.TotalSize]);
        _encodedPublishPacket = memory;
        Mqtt311Encoder.EncodePacket(_publishPacket, ref memory, estimate);
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
        Mqtt311Encoder.EncodePacket(_publishPacket, ref _writeableBuffer, _estimatedPublishPacketSize);
    }
}