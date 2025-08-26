// -----------------------------------------------------------------------
// <copyright file="Mqtt311EncoderComparisonBenchmarks.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System.Buffers;
using BenchmarkDotNet.Attributes;
using TurboMqtt.PacketTypes;
using TurboMqtt.Protocol;

namespace TurboMqtt.Benchmarks.Mqtt311;

[Config(typeof(MicroBenchmarkConfig))]
[MemoryDiagnoser]
public class Mqtt311EncoderComparisonBenchmarks
{
    private readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
    
    [Params(128, 1024, 4096)]
    public int PayloadSize { get; set; }
    
    [Params(1, 10, 50)]
    public int PacketCount { get; set; }
    
    private PublishPacket[] _packets = null!;
    private PacketSize[] _sizes = null!;
    private (MqttPacket packet, PacketSize size)[] _packetTuples = null!;
    
    [GlobalSetup]
    public void Setup()
    {
        _packets = new PublishPacket[PacketCount];
        _sizes = new PacketSize[PacketCount];
        _packetTuples = new (MqttPacket packet, PacketSize size)[PacketCount];
        
        var payload = new byte[PayloadSize];
        Random.Shared.NextBytes(payload);
        
        for (var i = 0; i < PacketCount; i++)
        {
            var qos = (QualityOfService)(i % 3);
            _packets[i] = new PublishPacket(qos, false, i % 2 == 0, $"topic/benchmark/{i}")
            {
                Payload = new ReadOnlyMemory<byte>(payload)
            };
            
            // Only set PacketId for QoS > 0
            if (qos > QualityOfService.AtMostOnce)
            {
                _packets[i].PacketId = (ushort)(i + 1);
            }
            
            _sizes[i] = MqttPacketSizeEstimator.EstimateMqtt3PacketSize(_packets[i]);
            _packetTuples[i] = (_packets[i], _sizes[i]);
        }
    }
    
    [Benchmark(Baseline = true)]
    public int StandardEncoder()
    {
        var totalSize = _sizes.Sum(s => s.TotalSize);
        var buffer = _arrayPool.Rent(totalSize);
        
        try
        {
            var memory = new Memory<byte>(buffer, 0, totalSize);
            var totalBytes = 0;
            
            for (var i = 0; i < PacketCount; i++)
            {
                var bytes = Mqtt311Encoder.EncodePacket(_packets[i], ref memory, _sizes[i]);
                memory = memory.Slice(bytes);
                totalBytes += bytes;
            }
            
            return totalBytes;
        }
        finally
        {
            _arrayPool.Return(buffer);
        }
    }
    
    [Benchmark]
    public int OptimizedEncoder()
    {
        var totalSize = _sizes.Sum(s => s.TotalSize);
        var buffer = _arrayPool.Rent(totalSize);
        
        try
        {
            var span = new Span<byte>(buffer, 0, totalSize);
            return Mqtt311EncoderOptimized.EncodePackets(_packetTuples, span);
        }
        finally
        {
            _arrayPool.Return(buffer);
        }
    }
    
    [Benchmark]
    public int StandardEncoderSinglePacket()
    {
        var buffer = _arrayPool.Rent(_sizes[0].TotalSize);
        
        try
        {
            var memory = new Memory<byte>(buffer, 0, _sizes[0].TotalSize);
            return Mqtt311Encoder.EncodePacket(_packets[0], ref memory, _sizes[0]);
        }
        finally
        {
            _arrayPool.Return(buffer);
        }
    }
    
    [Benchmark]
    public int OptimizedEncoderSinglePacket()
    {
        var buffer = _arrayPool.Rent(_sizes[0].TotalSize);
        
        try
        {
            var span = new Span<byte>(buffer, 0, _sizes[0].TotalSize);
            return Mqtt311EncoderOptimized.EncodePacket(_packets[0], span, _sizes[0]);
        }
        finally
        {
            _arrayPool.Return(buffer);
        }
    }
    

}