using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using TurboMqtt.Utility;

namespace TurboMqtt.Benchmarks.DeDupe;

[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class SimpleLruCacheBenchmark
{
    [Params(100, 1000, 10000)] public int CacheSize { get; set; }
        
    private SimpleLruCache<ushort> _originalCache;
    private ExperimentalSimpleLruCache _optimizedCache;
    private readonly ushort[] _keys;

    public SimpleLruCacheBenchmark()
    {
        _originalCache = new SimpleLruCache<ushort>(CacheSize, TimeSpan.Zero);
        _optimizedCache = new ExperimentalSimpleLruCache(CacheSize, TimeSpan.Zero);
            
        _keys = new ushort[CacheSize];
        for (ushort i = 0; i < _keys.Length; i++)
        {
            _keys[i] = i;
        }
    }

    [GlobalSetup]
    public void Setup()
    {
        foreach (var key in _keys)
        {
            _originalCache.Add(key);
            _optimizedCache.Add(key);
        }
    }

    [BenchmarkCategory("Add"), Benchmark(Baseline = true)]
    public void OriginalCache_Add()
    {
        for (int i = 0; i < _keys.Length; i++)
        {
            _originalCache.Add(_keys[i]);
        }
    }

    [BenchmarkCategory("Add"), Benchmark]
    public void OptimizedCache_Add()
    {
        for (int i = 0; i < _keys.Length; i++)
        {
            _optimizedCache.Add(_keys[i]);
        }
    }

    [BenchmarkCategory("Contains"), Benchmark(Baseline = true)]
    public void OriginalCache_Contains()
    {
        for (int i = 0; i < _keys.Length; i++)
        {
            _originalCache.Contains(_keys[i]);
        }
    }

    [BenchmarkCategory("Contains"), Benchmark]
    public void OptimizedCache_Contains()
    {
        for (int i = 0; i < _keys.Length; i++)
        {
            _optimizedCache.Contains(_keys[i]);
        }
    }

    [BenchmarkCategory("Evict"), Benchmark(Baseline = true)]
    public void OriginalCache_EvictExpired()
    {
        _originalCache.EvictExpired();
    }

    [BenchmarkCategory("Evict"), Benchmark]
    public void OptimizedCache_EvictExpired()
    {
        _optimizedCache.EvictExpired();
    }

    [BenchmarkCategory("Remove"), Benchmark(Baseline = true)]
    public void OriginalCache_Remove()
    {
        for (int i = 0; i < _keys.Length; i++)
        {
            _originalCache.Remove(_keys[i]);
        }
    }

    [BenchmarkCategory("Remove"), Benchmark]
    public void OptimizedCache_Remove()
    {
        for (int i = 0; i < _keys.Length; i++)
        {
            _optimizedCache.Remove(_keys[i]);
        }
    }
}