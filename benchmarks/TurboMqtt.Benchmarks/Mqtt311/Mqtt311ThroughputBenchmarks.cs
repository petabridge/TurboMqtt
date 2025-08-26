// -----------------------------------------------------------------------
// <copyright file="Mqtt311ThroughputBenchmarks.cs" company="Petabridge, LLC">
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
public class Mqtt311ThroughputBenchmarks
{
    private readonly Mqtt311Decoder _decoder = new();
    private readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
    
    [Params(128, 512, 1024, 4096)]
    public int PayloadSize { get; set; }
    
    [Params(10, 100, 1000)]
    public int MessageCount { get; set; }
    
    private PublishPacket[] _publishPackets = null!;
    private Memory<byte>[] _encodedPackets = null!;
    private PacketSize[] _estimatedSizes = null!;
    
    [GlobalSetup]
    public void Setup()
    {
        _publishPackets = new PublishPacket[MessageCount];
        _encodedPackets = new Memory<byte>[MessageCount];
        _estimatedSizes = new PacketSize[MessageCount];
        
        var payload = new byte[PayloadSize];
        Random.Shared.NextBytes(payload);
        
        for (var i = 0; i < MessageCount; i++)
        {
            _publishPackets[i] = new PublishPacket(
                i % 3 == 0 ? QualityOfService.AtMostOnce : 
                i % 3 == 1 ? QualityOfService.AtLeastOnce : QualityOfService.ExactlyOnce, 
                false, false, $"topic/test/{i}")
            {
                PacketId = (ushort)(i + 1),
                Payload = new ReadOnlyMemory<byte>(payload)
            };
            
            var estimate = MqttPacketSizeEstimator.EstimateMqtt3PacketSize(_publishPackets[i]);
            _estimatedSizes[i] = estimate;
            
            var buffer = new byte[estimate.TotalSize];
            var memory = new Memory<byte>(buffer);
            Mqtt311Encoder.EncodePacket(_publishPackets[i], ref memory, estimate);
            _encodedPackets[i] = buffer;
        }
    }
    
    [Benchmark]
    public int EncodeBatch()
    {
        var totalBytes = 0;
        var bufferSize = _estimatedSizes.Sum(s => s.TotalSize);
        var buffer = _arrayPool.Rent(bufferSize);
        try
        {
            var memory = new Memory<byte>(buffer, 0, bufferSize);
            
            for (var i = 0; i < MessageCount; i++)
            {
                var bytesWritten = Mqtt311Encoder.EncodePacket(_publishPackets[i], ref memory, _estimatedSizes[i]);
                memory = memory.Slice(bytesWritten);
                totalBytes += bytesWritten;
            }
            
            return totalBytes;
        }
        finally
        {
            _arrayPool.Return(buffer);
        }
    }
    
    [Benchmark]
    public int DecodeBatch()
    {
        var packetCount = 0;
        
        for (var i = 0; i < MessageCount; i++)
        {
            if (_decoder.TryDecode(_encodedPackets[i], out var packets))
            {
                packetCount += packets.Count;
            }
        }
        
        return packetCount;
    }
    
    [Benchmark(Baseline = true)]
    public int EncodeDecodeRoundTrip()
    {
        var bufferSize = _estimatedSizes.Sum(s => s.TotalSize);
        var buffer = _arrayPool.Rent(bufferSize);
        try
        {
            var memory = new Memory<byte>(buffer, 0, bufferSize);
            var totalBytes = 0;
            
            // Encode
            for (var i = 0; i < MessageCount; i++)
            {
                var bytesWritten = Mqtt311Encoder.EncodePacket(_publishPackets[i], ref memory, _estimatedSizes[i]);
                memory = memory.Slice(bytesWritten);
                totalBytes += bytesWritten;
            }
            
            // Decode
            var readMemory = new ReadOnlyMemory<byte>(buffer, 0, totalBytes);
            var packetCount = 0;
            
            while (readMemory.Length > 0)
            {
                if (_decoder.TryDecode(readMemory, out var packets))
                {
                    packetCount += packets.Count;
                    // Estimate consumed bytes based on decoded packets
                    var consumedBytes = packets.Sum(p => MqttPacketSizeEstimator.EstimateMqtt3PacketSize(p).TotalSize);
                    if (consumedBytes > 0)
                    {
                        readMemory = readMemory.Slice(Math.Min(consumedBytes, readMemory.Length));
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }
            }
            
            return packetCount;
        }
        finally
        {
            _arrayPool.Return(buffer);
        }
    }
}