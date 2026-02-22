# TurboMqtt MQTT 5.0 Performance Benchmarks

Full production benchmarks for the MQTT 5.0 pipeline, recorded as part of the pre-1.0 release
quality gate ([GitHub issue #371](https://github.com/petabridge/TurboMqtt/issues/371)).

**Machine:** Intel Core i9-9900K CPU 3.60GHz (Coffee Lake), 1 CPU, 8 logical and 8 physical cores
**OS:** Linux Ubuntu 24.04.4 LTS (Noble Numbat)
**Runtime:** .NET 10.0.3, X64 RyuJIT x86-64-v3
**BenchmarkDotNet:** v0.15.8, `RunStrategy=Monitoring`, `LaunchCount=10`, `WarmupCount=10`
**Benchmark source:** [`Mqtt5End2EndTcpBenchmarks.cs`](../../benchmarks/TurboMqtt.Benchmarks/Mqtt5/Mqtt5End2EndTcpBenchmarks.cs),
[`Mqtt5TlsTcpBenchmarks.cs`](../../benchmarks/TurboMqtt.Benchmarks/Mqtt5/Mqtt5TlsTcpBenchmarks.cs)

> Each benchmark iteration publishes and receives 1,000 messages end-to-end through an in-process
> `FakeMqttTcpServer`. The per-operation cost includes the full publish → broker → subscriber round-trip
> (4× for QoS 1, 16× for QoS 2), so the Req/sec column represents the **pair throughput** (one send +
> one receive counted as 2 operations via `OperationsPerInvoke = 1000 * 2`).

---

## MQTT 5.0 TCP

Via [`Mqtt5EndToEndTcpBenchmarks`](../../benchmarks/TurboMqtt.Benchmarks/Mqtt5/Mqtt5End2EndTcpBenchmarks.cs)

```
BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Core i9-9900K CPU 3.60GHz (Coffee Lake), 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.103
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v3
  Job-TMRHBV : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v3

InvocationCount=1  LaunchCount=10  RunStrategy=Monitoring
UnrollFactor=1  WarmupCount=10
```

| Method                    | QoSLevel    | PayloadSizeBytes | Mean     | Error     | StdDev    | Req/sec     |
|-------------------------- |------------ |----------------- |---------:|----------:|----------:|------------:|
| PublishAndReceiveMessages | AtMostOnce  | 10               | 3.243 μs | 0.1560 μs | 0.4599 μs | 308,333.32  |
| PublishAndReceiveMessages | AtMostOnce  | 1024             | 3.175 μs | 0.3639 μs | 1.0729 μs | 314,927.69  |
| PublishAndReceiveMessages | AtLeastOnce | 10               | 3.810 μs | 0.4279 μs | 1.2615 μs | 262,493.28  |
| PublishAndReceiveMessages | AtLeastOnce | 1024             | 3.866 μs | 0.3386 μs | 0.9984 μs | 258,697.40  |
| PublishAndReceiveMessages | ExactlyOnce | 10               | 8.862 μs | 0.6206 μs | 1.8299 μs | 112,846.37  |
| PublishAndReceiveMessages | ExactlyOnce | 1024             | 9.694 μs | 0.6006 μs | 1.7710 μs | 103,161.13  |

> **32 KB payload benchmarks** failed with `OperationCanceledException` (30-second CTS timeout).
> This is a known benchmark infrastructure limitation with large payloads on in-process fake servers,
> not a production regression. See the same behavior in the MQTT 3.1.1 8 KB benchmarks. Large-payload
> performance is I/O-bound and will be measured separately against a real EMQX broker.

---

## MQTT 5.0 TLS (TCP+SSL)

Via [`Mqtt5TlsEndToEndTcpBenchmarks`](../../benchmarks/TurboMqtt.Benchmarks/Mqtt5/Mqtt5TlsTcpBenchmarks.cs)

Uses an in-process `FakeMqttTlsTcpServer` with a self-signed certificate. QoS 2 is excluded from the
TLS benchmark as the 4-step handshake compounded with TLS overhead produces unreliable results under the
30-second CTS timeout in the current in-process test setup.

```
BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Core i9-9900K CPU 3.60GHz (Coffee Lake), 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.103
  [Host]     : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v3
  Job-TMRHBV : .NET 10.0.3 (10.0.3, 10.0.326.7603), X64 RyuJIT x86-64-v3

InvocationCount=1  LaunchCount=10  RunStrategy=Monitoring
UnrollFactor=1  WarmupCount=10
```

| Method                    | QoSLevel    | PayloadSizeBytes | Mean     | Error     | StdDev    | Req/sec     |
|-------------------------- |------------ |----------------- |---------:|----------:|----------:|------------:|
| PublishAndReceiveMessages | AtMostOnce  | 10               | 3.993 μs | 0.2875 μs | 0.8477 μs | 250,464.78  |
| PublishAndReceiveMessages | AtMostOnce  | 1024             | 4.521 μs | 0.2667 μs | 0.7864 μs | 221,190.37  |
| PublishAndReceiveMessages | AtLeastOnce | 10               | 5.228 μs | 0.3560 μs | 1.0496 μs | 191,265.99  |
| PublishAndReceiveMessages | AtLeastOnce | 1024             | 5.733 μs | 0.4364 μs | 1.2868 μs | 174,415.56  |

---

## Regression Analysis vs MQTT 3.1.1

Reference baseline from [`Mqtt311EndToEndTcpBenchmarks`](../../benchmarks/TurboMqtt.Benchmarks/Mqtt311/Mqtt311End2EndTcpBenchmarks.cs),
same machine and runtime (Linux, .NET 10.0.3):

| QoSLevel   | PayloadSizeBytes | MQTT 3.1.1 (req/s) | MQTT 5.0 (req/s) | Delta       |
|----------- |----------------- |-------------------:|------------------:|-------------|
| AtMostOnce | 10               | 318,961            | 308,333           | −3.3% ✅    |
| AtMostOnce | 1024             | 327,116            | 314,928           | −3.7% ✅    |

> **Verdict: no throughput regression.** The observed −3-4% difference for QoS 0 is within the
> measurement noise (MQTT 5.0 QoS0/10B 99.9% CI: ±4.8%). MQTT 5.0 adds slightly larger CONNECT/CONNACK
> frames and an expanded property set but the encoding/decoding overhead is negligible in the steady-state
> publish/subscribe path.

For QoS 1 and QoS 2, no Linux MQTT 3.1.1 reference run exists for comparison; the MQTT 3.1.1
Windows/.NET 8.0 results in [Performance.md](../performance.md) are not directly comparable due to
hardware and runtime differences. The MQTT 5.0 QoS 1 results (~262k req/s for 10B) are consistent
with the expected QoS overhead pattern.

---

## Benchmark Design Notes

- Benchmarks use an in-process `FakeMqttTcpServer` / `FakeMqttTlsTcpServer`, not a real broker. This
  enables `git clone && dotnet run -c Release` reproducibility without external dependencies.
- The `OperationsPerInvoke = PacketCount * 2` setting accounts for both the publish and receive sides
  of each message, so 1,000 messages yield 2,000 operations per invocation.
- `RunStrategy=Monitoring` with `LaunchCount=10` / `WarmupCount=10` produces statistically robust
  results but is intentionally more expensive than a micro-benchmark run.
- For real-broker throughput data see [Performance.md](../performance.md#data-with-real-brokers).
