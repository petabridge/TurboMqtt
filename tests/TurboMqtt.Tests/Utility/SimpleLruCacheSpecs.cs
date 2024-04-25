// -----------------------------------------------------------------------
// <copyright file="CircularHashSetSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.Utility;

namespace TurboMqtt.Tests.Utility;

public class SimpleLruCacheSpecs
{
    [Fact]
    public void SimpleLruCache_should_evict_oldest_item_when_capacity_reached()
    {
        var cache = new SimpleLruCache<int>(capacity: 3);
        cache.Add(1, Deadline.Now); // should be removed
        cache.Add(2);
        cache.Add(3);
        cache.Add(4);

        cache.Count.Should().Be(3);
        cache.Contains(1).Should().BeFalse();
        cache.Contains(2).Should().BeTrue();
        cache.Contains(3).Should().BeTrue();
        cache.Contains(4).Should().BeTrue();
    }
    
    // add a spec to demonstrate that we can evict expired items
    [Fact]
    public void SimpleLruCache_should_evict_expired_items()
    {
        var cache = new SimpleLruCache<int>(capacity: 10);
        cache.Add(1, Deadline.Now);
        cache.Add(2, Deadline.Now);
        cache.Add(3, Deadline.Now);
        
        var evicted = cache.EvictExpired();

        evicted.Should().Be(3);
        cache.Count.Should().Be(0);
    }
    

    [Fact]
    public void SimpleLruCache_should_remove_item_when_key_removed()
    {
        var cache = new SimpleLruCache<int>(capacity: 3);
        cache.Add(1);
        cache.Add(2);
        cache.Add(3);

        cache.Remove(2);

        cache.Count.Should().Be(2);
        cache.Contains(2).Should().BeFalse();
    }

    [Fact]
    public void SimpleLruCache_should_clear_all_items()
    {
        var cache = new SimpleLruCache<int>(capacity: 3);
        cache.Add(1);
        cache.Add(2);
        cache.Add(3);

        cache.Clear();

        cache.Count.Should().Be(0);
    }
}