# TurboMqtt MQTT 3.1.1 Performance Benchmarks

Full production benchmarks for the MQTT 3.1.1 pipeline, recorded for the v1.0 release quality gate.

**Machine:** Intel Core i9-9900K CPU 3.60GHz (Coffee Lake), 1 CPU, 8 logical and 8 physical cores
**OS:** Linux Ubuntu 24.04.4 LTS (Noble Numbat)
**Runtime:** .NET 10.0.3, X64 RyuJIT x86-64-v3
**BenchmarkDotNet:** v0.15.8, `RunStrategy=Monitoring`, `LaunchCount=10`, `WarmupCount=10`
**Benchmark source:** [`Mqtt311End2EndTcpBenchmarks.cs`](../../benchmarks/TurboMqtt.Benchmarks/Mqtt311/Mqtt311End2EndTcpBenchmarks.cs)

> Each benchmark iteration publishes and receives 1,000 messages end-to-end through an in-process
> `FakeMqttTcpServer`. The per-operation cost includes the full publish → broker → subscriber round-trip
> (2× for QoS 0, 4× for QoS 1, 16× for QoS 2), so the Req/sec column represents the **pair throughput**
> (one send + one receive counted as 2 operations via `OperationsPerInvoke = 1000 * 2`).

---

## MQTT 3.1.1 TCP

Via [`Mqtt311EndToEndTcpBenchmarks`](../../benchmarks/TurboMqtt.Benchmarks/Mqtt311/Mqtt311End2EndTcpBenchmarks.cs)

```
BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Core i9-9900K CPU 3.60GHz (Coffee Lake), 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.103
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v3
  Job-TMRHBV : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v3

InvocationCount=1  LaunchCount=10  RunStrategy=Monitoring
UnrollFactor=1  WarmupCount=10
```

| Method                    | QoSLevel    | PayloadSizeBytes | Mean      | Error     | StdDev    | Req/sec     |
|-------------------------- |------------ |----------------- |----------:|----------:|----------:|------------:|
| PublishAndReceiveMessages | AtMostOnce  | 10               |  3.313 μs | 0.151 μs  | 0.446 μs  |  301,798    |
| PublishAndReceiveMessages | AtMostOnce  | 1024             |  3.220 μs | 0.433 μs  | 1.276 μs  |  310,516    |
| PublishAndReceiveMessages | AtMostOnce  | 2048             |  3.731 μs | 0.483 μs  | 1.424 μs  |  268,033    |
| PublishAndReceiveMessages | AtLeastOnce | 10               |  3.878 μs | 0.636 μs  | 1.875 μs  |  257,854    |
| PublishAndReceiveMessages | AtLeastOnce | 1024             |  3.904 μs | 0.338 μs  | 0.996 μs  |  256,154    |
| PublishAndReceiveMessages | AtLeastOnce | 2048             |  3.544 μs | 1.639 μs  | 1.084 μs  |  282,199    |
| PublishAndReceiveMessages | ExactlyOnce | 10               |  8.488 μs | 0.659 μs  | 1.942 μs  |  117,816    |
| PublishAndReceiveMessages | ExactlyOnce | 1024             | 10.964 μs | 1.690 μs  | 4.984 μs  |   91,209    |
| PublishAndReceiveMessages | ExactlyOnce | 2048             |  9.971 μs | 1.110 μs  | 3.272 μs  |  100,289    |
| PublishAndReceiveMessages | ExactlyOnce | 8192             | 13.513 μs | 5.178 μs  | 3.425 μs  |   74,005    |

> **8 KB benchmarks for QoS 0 and QoS 1** timed out with `OperationCanceledException` (30-second CTS timeout).
> This is a known benchmark infrastructure limitation with large payloads on in-process fake servers,
> not a production regression. See similar behavior in the MQTT 5.0 benchmarks. Large-payload
> performance is I/O-bound and will be measured separately against a real EMQX broker.

---

## Benchmark Design Notes

- Benchmarks use an in-process `FakeMqttTcpServer`, not a real broker. This enables `git clone && dotnet run -c Release` reproducibility without external dependencies.
- The `OperationsPerInvoke = 1000 * 2` setting accounts for both the publish and receive sides of each message, so 1,000 messages yield 2,000 operations per invocation.
- `RunStrategy=Monitoring` with `LaunchCount=10` / `WarmupCount=10` produces statistically robust results but is intentionally more expensive than a micro-benchmark run.
- For real-broker throughput data see [Performance.md](../Performance.md#data-with-real-brokers).
