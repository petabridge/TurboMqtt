// -----------------------------------------------------------------------
// <copyright file="TopicCacheManagerCRUDBenchmarks.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using BenchmarkDotNet.Attributes;
using TurboMqtt.Utility;

namespace TurboMqtt.Benchmarks.DeDupe;

public class TopicCacheManagerCrudBenchmarks
{
    [Params(1,2,3)] public int ActiveTopics { get; set; }
    
    public readonly TimeSpan Expiry = TimeSpan.Zero;
    private TopicCacheManager<ushort> _cacheManager = null!;
    
    private const string Topic1 = "test0";
    private const string Topic2 = "test1";
    private const string Topic3 = "test2";
    
    [IterationSetup]
    public void Setup()
    {
        _cacheManager = new TopicCacheManager<ushort>(1000, Expiry);
        
        // pre-populate the cache with zero, which is an illegal packetId so it won't get used again
        for(var i = 0; i < ActiveTopics; i++)
            _cacheManager.AddItem($"topic{i}", 0);
    }
    
    [Benchmark]
    public void Add()
    {
        _cacheManager.AddItem(Topic1, 1);
    }
    
    [Benchmark]
    public void Remove()
    {
        _cacheManager.RemoveItem(Topic1, 0);
    }
    
    [Benchmark]
    public void Contains()
    {
        _cacheManager.ContainsItem(Topic1, 0);
    }
}