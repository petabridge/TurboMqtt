// -----------------------------------------------------------------------
// <copyright file="PacketDeDuplicationBenchmarks.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using BenchmarkDotNet.Attributes;
using TurboMqtt.Utility;

namespace TurboMqtt.Benchmarks.DeDupe;

public class PacketDeDuplicationBenchmarks
{
    [Params(0, 1000, 5000)] public int BufferSize { get; set; }
    
    private TopicCacheManager<ushort> _cacheManager = new();
}