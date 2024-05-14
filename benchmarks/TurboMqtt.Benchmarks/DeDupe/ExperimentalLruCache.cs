// -----------------------------------------------------------------------
// <copyright file="ExperimentalLruCache.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using TurboMqtt.Utility;

namespace TurboMqtt.Benchmarks.DeDupe;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

internal sealed class ExperimentalSimpleLruCache
{
    public ExperimentalSimpleLruCache(int capacity) : this(capacity, TimeSpan.FromSeconds(5))
    {
    }

    public ExperimentalSimpleLruCache(int capacity, TimeSpan timeToLive)
    {
        Capacity = capacity;
        TimeToLive = timeToLive;
        _queue = new Queue<(ushort packetId, Deadline expiration)>(capacity);
        _bitSet = new BitArray(ushort.MaxValue + 1);
    }

    public TimeSpan TimeToLive { get; }

    public int Capacity { get; }

    public int Count => _queue.Count;

    private readonly Queue<(ushort packetId, Deadline expiration)> _queue;
    private readonly BitArray _bitSet;

    public bool Contains(ushort key)
    {
        return _bitSet[key];
    }

    public void Add(ushort key, Deadline deadline)
    {
        if (_queue.Count >= Capacity)
        {
            EvictExpired();
        }
        
        if (_queue.Count >= Capacity)
        {
            // If still full, remove the oldest item
            var oldest = _queue.Dequeue();
            _bitSet[oldest.packetId] = false;
        }

        _queue.Enqueue((key, deadline));
        _bitSet[key] = true;
    }

    public void Add(ushort key)
    {
        Add(key, Deadline.FromNow(TimeToLive));
    }

    public int EvictExpired()
    {
        var evicted = 0;
        var initialCount = _queue.Count;

        while (_queue.Count > 0)
        {
            var (packetId, expiration) = _queue.Peek();
            if (expiration.IsOverdue)
            {
                _queue.Dequeue();
                _bitSet[packetId] = false;
                evicted++;
            }
            else
            {
                break;
            }
        }

        // If queue size changed, ensure capacity
        if (_queue.Count < initialCount)
        {
            _queue.TrimExcess();
        }

        return evicted;
    }

    public void Clear()
    {
        _queue.Clear();
        _queue.TrimExcess();
        _bitSet.SetAll(false);
    }

    public void Remove(ushort key)
    {
        _bitSet[key] = false;
    }
}
