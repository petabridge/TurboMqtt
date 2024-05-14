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
    public readonly TimeSpan Expiry = TimeSpan.Zero;
    private TopicCacheManager<ushort> _cacheManager = null!;
    
    private const string Topic1 = "test1";
    private const string Topic2 = "test2";
    private const string Topic3 = "test3";
    
    [IterationSetup]
    public void Setup()
    {
        _cacheManager = new TopicCacheManager<ushort>(1000, Expiry);
        
        // pre-populate the cache with zero, which is an illegal packetId so it won't get used again
        _cacheManager.AddItem(Topic1, 0);
        _cacheManager.AddItem(Topic2, 0);
        _cacheManager.AddItem(Topic3, 0);
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