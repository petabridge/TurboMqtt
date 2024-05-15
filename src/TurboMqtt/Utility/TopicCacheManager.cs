// -----------------------------------------------------------------------
// <copyright file="TopicCacheManager.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.PacketTypes;

namespace TurboMqtt.Utility;

/// <summary>
/// Used for managing a cache of items associated with a specific topic.
/// </summary>
/// <remarks>
/// Used to help track packet ids for <see cref="PublishPacket"/> messages per-topic.
/// </remarks>
/// <typeparam name="T">The type of identifier we're caching - usually a <see cref="NonZeroUInt16"/></typeparam>
internal sealed class TopicCacheManager<T> where T : notnull
{
    private readonly Dictionary<string, SimpleLruCache<T>> _topicCaches;
    private readonly int _defaultCapacity;
    private readonly TimeSpan _defaultExpiry;

    public TopicCacheManager(int defaultCapacity, TimeSpan defaultExpiry)
    {
        _topicCaches = new Dictionary<string, SimpleLruCache<T>>();
        _defaultCapacity = defaultCapacity;
        _defaultExpiry = defaultExpiry;
    }

    public SimpleLruCache<T> GetCacheForTopic(string topic)
    {
        if (!_topicCaches.TryGetValue(topic, out var cache))
        {
            cache = new SimpleLruCache<T>(_defaultCapacity, _defaultExpiry);
            _topicCaches[topic] = cache;
        }
        return cache;
    }

    public void AddItem(string topic, T item)
    {
        SimpleLruCache<T> cache = GetCacheForTopic(topic);
        cache.Add(item);
    }

    public bool ContainsItem(string topic, T item)
    {
        SimpleLruCache<T> cache = GetCacheForTopic(topic);
        return cache.Contains(item);
    }
    
    public void RemoveItem(string topic, T item)
    {
        SimpleLruCache<T> cache = GetCacheForTopic(topic);
        cache.Remove(item);
    }
    
    public void ClearCache(string topic)
    {
        if (_topicCaches.TryGetValue(topic, out var cache))
        {
            cache.Clear();
        }
    }
    
    public void RemoveCache(string topic)
    {
        _topicCaches.Remove(topic);
    }
    
    public int EvictExpiredItems(string topic)
    {
        if (_topicCaches.TryGetValue(topic, out var cache))
        {
            return cache.EvictExpired();
        }
        return 0;
    }
    
    public int EvictAllExpiredItems()
    {
        int totalEvicted = 0;
        foreach (var cache in _topicCaches.Values)
        {
            totalEvicted += cache.EvictExpired();
        }
        return totalEvicted;
    }
}