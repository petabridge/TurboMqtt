// -----------------------------------------------------------------------
// <copyright file="TopicCacheManagerEvictionBenchmarks.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using BenchmarkDotNet.Attributes;
using TurboMqtt.Utility;

namespace TurboMqtt.Benchmarks.DeDupe;

public class TopicCacheManagerEvictionBenchmarks
{
    [Params(0, 1000, 5000)] public int BufferSize { get; set; }
    [Params(0.01, 0.25, 0.5, 0.75, 1.0)] public double FillPercentage { get; set; }
    [Params(1,2,3)] public int TopicsToEvict { get; set; }
    
    private TopicCacheManager<ushort> _cacheManager = null!;
    
    private const string Topic1 = "test1";
    private const string Topic2 = "test2";
    private const string Topic3 = "test3";
    
    [IterationSetup]
    public void Setup()
    {
        _cacheManager = new TopicCacheManager<ushort>(BufferSize, TimeSpan.Zero);
        
        // pre-populate the cache with zero, which is an illegal packetId so it won't get used again
        _cacheManager.AddItem(Topic1, 0);
        _cacheManager.AddItem(Topic2, 0);
        _cacheManager.AddItem(Topic3, 0);
        
        // fill the cache with the desired percentage
        var fillCount = (int) (BufferSize * FillPercentage);
        for (var i = 0; i < fillCount; i++)
        {
            for(var j = 0; j < TopicsToEvict; j++)
                _cacheManager.AddItem($"test{j}", (ushort) i);
        }
    }
    
    [Benchmark]
    public void Evict()
    {
        _cacheManager.EvictAllExpiredItems();
    }
}