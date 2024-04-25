// -----------------------------------------------------------------------
// <copyright file="Mqtt311End2EndTcpBenchmarks.cs" company="Petabridge, LLC">
//      Copyright (C) 2024 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using BenchmarkDotNet.Attributes;

namespace TurboMqtt.Benchmarks.Mqtt311;

public class Mqtt311End2EndTcpBenchmarks
{
    [Params(QualityOfService.AtMostOnce, QualityOfService.AtLeastOnce, QualityOfService.ExactlyOnce)]
    public QualityOfService QoSLevel { get; set; }
    
    [Params(10, 1024, 8*1024)]
    public int PayloadSizeBytes { get; set; }
    
    public const int PacketCount = 100_000;
}