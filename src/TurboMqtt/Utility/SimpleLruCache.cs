// -----------------------------------------------------------------------
// <copyright file="SimpleLruCache.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

namespace TurboMqtt.Utility;

/// <summary>
/// For de-duplicating packets
/// </summary>
internal sealed class SimpleLruCache<TKey> where TKey : notnull
{
    public SimpleLruCache(int capacity) : this(capacity, TimeSpan.FromSeconds(5))
    {
    }
    
    public SimpleLruCache(int capacity, TimeSpan timeToLive)
    {
        Capacity = capacity;
        TimeToLive = timeToLive;
        _cache = new Dictionary<TKey, Deadline>(capacity);
    }
    
    public TimeSpan TimeToLive { get; }

    public int Capacity { get; }
    
    public int Count => _cache.Count;

    private readonly Dictionary<TKey, Deadline> _cache;

    public bool Contains(TKey key)
    {
        return _cache.ContainsKey(key);
    }
    
    public void Add(TKey key, Deadline deadline)
    {
        _cache[key] = deadline;
    }
    
    public void Add(TKey key)
    {
        Add(key, Deadline.FromNow(TimeToLive));
    }
    
    public int EvictExpired()
    {
        var expired = _cache.Where(x => x.Value.IsOverdue);
        var evicted = 0;
        foreach (var kvp in expired)
        {
            evicted++;
            _cache.Remove(kvp.Key);
        }

        return evicted;
    }
    
    public void Clear()
    {
        _cache.Clear();
    }
    
    public void Remove(TKey key)
    {
        _cache.Remove(key);
    }
}