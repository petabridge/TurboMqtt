// -----------------------------------------------------------------------
// <copyright file="TopicCacheManagerCRUDBenchmarks.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using TurboMqtt.Utility;

namespace TurboMqtt.Benchmarks.DeDupe;

[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class TopicCacheManagerCrudBenchmarks
{
    [Params(1,2,3)] public int ActiveTopics { get; set; }
    
    public readonly TimeSpan Expiry = TimeSpan.Zero;
    private TopicCacheManager<ushort> _cacheManager = null!;
    
    private const string Topic1 = "test0";
    private const string Topic2 = "test1";
    private const string Topic3 = "test2";
    
    private const string UnusedTopic = "unused";
    
    [IterationSetup]
    public void Setup()
    {
        _cacheManager = new TopicCacheManager<ushort>(1000, Expiry);
        
        // pre-populate the cache with zero, which is an illegal packetId so it won't get used again
        switch (ActiveTopics)
        {
            case 1:
                _cacheManager.AddItem(Topic1, 0);
                break;
            case 2:
                _cacheManager.AddItem(Topic1, 0);
                _cacheManager.AddItem(Topic2, 0);
                break;
            case 3:
                _cacheManager.AddItem(Topic1, 0);
                _cacheManager.AddItem(Topic2, 0);
                _cacheManager.AddItem(Topic3, 0);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(ActiveTopics));
        }
    }
    
    [BenchmarkCategory("Add"), Benchmark(Baseline = true)]
    public void AddHit()
    {
        _cacheManager.AddItem(Topic1, 0);
    }
    
    [BenchmarkCategory("Add"), Benchmark]
    public void AddMiss()
    {
        _cacheManager.AddItem(UnusedTopic, 1);
    }
    
    [BenchmarkCategory("Add"), Benchmark]
    public void AddMissTopic()
    {
        _cacheManager.AddItem(UnusedTopic, 1);
    }
    
    [BenchmarkCategory("Remove"), Benchmark]
    public void Remove()
    {
        _cacheManager.RemoveItem(Topic1, 0);
    }
    
    [BenchmarkCategory("Contains"), Benchmark(Baseline = true)]
    public void ContainsHit()
    {
        _cacheManager.ContainsItem(Topic1, 0);
    }
    
    [BenchmarkCategory("Contains"), Benchmark]
    public void ContainsMiss()
    {
        _cacheManager.ContainsItem(Topic1, 1);
    }
    
    [BenchmarkCategory("Contains"), Benchmark]
    public void ContainsMissTopic()
    {
        _cacheManager.ContainsItem(UnusedTopic, 1);
    }
}