// -----------------------------------------------------------------------
// <copyright file="MicroBenchmarkConfigh.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;

namespace TurboMqtt.Benchmarks;

/// <summary>
/// Basic BenchmarkDotNet configuration used for microbenchmarks.
/// </summary>
public class MicroBenchmarkConfig : ManualConfig
{
    public MicroBenchmarkConfig()
    {
        AddDiagnoser(MemoryDiagnoser.Default);
        AddExporter(MarkdownExporter.GitHub);
        //AddColumn(new RequestsPerSecondColumn());
    }
}