using BenchmarkDotNet.Attributes;
using TurboMqtt.Utility;

namespace TurboMqtt.Benchmarks.DeDupe
{
    public class SimpleLruCacheBenchmark
    {
        [Params(100, 1000, 10000)] public int CacheSize { get; set; }
        
        private SimpleLruCache<ushort> _originalCache;
        private ExperimentalSimpleLruCache _optimizedCache;
        private readonly ushort[] _keys;

        public SimpleLruCacheBenchmark()
        {
            _originalCache = new SimpleLruCache<ushort>(CacheSize);
            _optimizedCache = new ExperimentalSimpleLruCache(CacheSize);
            
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

        [Benchmark]
        public void OriginalCache_Add()
        {
            for (int i = 0; i < _keys.Length; i++)
            {
                _originalCache.Add(_keys[i]);
            }
        }

        [Benchmark]
        public void OptimizedCache_Add()
        {
            for (int i = 0; i < _keys.Length; i++)
            {
                _optimizedCache.Add(_keys[i]);
            }
        }

        [Benchmark]
        public void OriginalCache_Contains()
        {
            for (int i = 0; i < _keys.Length; i++)
            {
                _originalCache.Contains(_keys[i]);
            }
        }

        [Benchmark]
        public void OptimizedCache_Contains()
        {
            for (int i = 0; i < _keys.Length; i++)
            {
                _optimizedCache.Contains(_keys[i]);
            }
        }

        [Benchmark]
        public void OriginalCache_EvictExpired()
        {
            _originalCache.EvictExpired();
        }

        [Benchmark]
        public void OptimizedCache_EvictExpired()
        {
            _optimizedCache.EvictExpired();
        }

        [Benchmark]
        public void OriginalCache_Remove()
        {
            for (int i = 0; i < _keys.Length; i++)
            {
                _originalCache.Remove(_keys[i]);
            }
        }

        [Benchmark]
        public void OptimizedCache_Remove()
        {
            for (int i = 0; i < _keys.Length; i++)
            {
                _optimizedCache.Remove(_keys[i]);
            }
        }

        [Benchmark]
        public void OriginalCache_Clear()
        {
            _originalCache.Clear();
        }

        [Benchmark]
        public void OptimizedCache_Clear()
        {
            _optimizedCache.Clear();
        }
    }
}