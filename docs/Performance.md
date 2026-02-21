# TurboMqtt Performance

## v1.0 Benchmark Update

**Updated for v1.0 on Linux Ubuntu 24.04, .NET 10.0, BenchmarkDotNet v0.15.8 (i9-9900K).**
Original Windows/.NET 8 results are archived below for historical reference.

---

## Current Benchmarks (v1.0)

### MQTT 3.1.1 Results

For detailed results including raw per-launch data, see [performance/mqtt311-benchmarks.md](performance/mqtt311-benchmarks.md).

| QoSLevel    | PayloadSizeBytes | Mean      | StdDev    | Req/sec     |
|------------ |----------------- |----------:|----------:|------------:|
| AtMostOnce  | 10 B             |  3.313 μs | 0.446 μs  |  301,798    |
| AtMostOnce  | 1 KB             |  3.220 μs | 1.276 μs  |  310,516    |
| AtMostOnce  | 2 KB             |  3.731 μs | 1.424 μs  |  268,033    |
| AtLeastOnce | 10 B             |  3.878 μs | 1.875 μs  |  257,854    |
| AtLeastOnce | 1 KB             |  3.904 μs | 0.996 μs  |  256,154    |
| AtLeastOnce | 2 KB             |  3.544 μs | 1.084 μs  |  282,199    |
| ExactlyOnce | 10 B             |  8.488 μs | 1.942 μs  |  117,816    |
| ExactlyOnce | 1 KB             | 10.964 μs | 4.984 μs  |   91,209    |
| ExactlyOnce | 2 KB             |  9.971 μs | 3.272 μs  |  100,289    |
| ExactlyOnce | 8 KB             | 13.513 μs | 3.425 μs  |   74,005    |

### MQTT 5.0 Results

For detailed results, see [performance/mqtt5-benchmarks.md](performance/mqtt5-benchmarks.md).

| QoSLevel    | PayloadSizeBytes | Mean     | StdDev    | Req/sec     |
|------------ |----------------- |---------:|----------:|------------:|
| AtMostOnce  | 10 B             | 3.243 μs | 0.460 μs  |  308,333    |
| AtMostOnce  | 1 KB             | 3.175 μs | 1.073 μs  |  314,928    |
| AtLeastOnce | 10 B             | 3.810 μs | 1.262 μs  |  262,493    |
| AtLeastOnce | 1 KB             | 3.866 μs | 0.998 μs  |  258,697    |
| ExactlyOnce | 10 B             | 8.862 μs | 1.830 μs  |  112,846    |
| ExactlyOnce | 1 KB             | 9.694 μs | 1.771 μs  |  103,161    |

### MQTT 5.0 vs 3.1.1 Comparison

| QoSLevel    | PayloadSizeBytes | MQTT 3.1.1 (req/s) | MQTT 5.0 (req/s) | Delta       |
|------------ |----------------- |-------------------:|------------------:|-------------|
| AtMostOnce  | 10 B             | 301,798            | 308,333           | +2.2% ✅    |
| AtMostOnce  | 1 KB             | 310,516            | 314,928           | +1.4% ✅    |
| AtLeastOnce | 10 B             | 257,854            | 262,493           | +1.8% ✅    |
| AtLeastOnce | 1 KB             | 256,154            | 258,697           | +1.0% ✅    |

> **Verdict: no throughput regression.** MQTT 5.0 shows slight performance improvements
> despite larger CONNECT/CONNACK frames and expanded property sets. The encoding/decoding
> overhead is negligible in the steady-state publish/subscribe path.

---

## Benchmark Reproduction

To reproduce these benchmarks locally:

```bash
# Run all benchmarks
dotnet run -c Release --project benchmarks/TurboMqtt.Benchmarks

# Run only MQTT 3.1.1 end-to-end benchmarks
dotnet run -c Release --project benchmarks/TurboMqtt.Benchmarks -- --filter '*Mqtt311EndToEndTcp*'

# Run only MQTT 5.0 end-to-end benchmarks
dotnet run -c Release --project benchmarks/TurboMqtt.Benchmarks -- --filter '*Mqtt5EndToEndTcp*'
```

See [benchmarks/README.md](../benchmarks/README.md) for detailed reproduction instructions.

---

## Historical Results (v0.x - Windows/.NET 8)

### MQTT 3.1.1 Benchmarks (Windows/.NET 8)

Via [`Mqtt311End2EndTcpBenchmarks.cs`](../benchmarks/TurboMqtt.Benchmarks/Mqtt311/Mqtt311End2EndTcpBenchmarks.cs)

```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.22631.3593/23H2/2023Update/SunValley3)
12th Gen Intel Core i7-1260P, 1 CPU, 16 logical and 12 physical cores
.NET SDK 8.0.204
  [Host]     : .NET 8.0.4 (8.0.424.16909), X64 RyuJIT AVX2
  Job-VHKMUG : .NET 8.0.4 (8.0.424.16909), X64 RyuJIT AVX2

InvocationCount=1  LaunchCount=10  RunStrategy=Monitoring
UnrollFactor=1  WarmupCount=10

```
| Method                    | QoSLevel    | PayloadSizeBytes | ProtocolVersion | Mean      | Error     | StdDev    | Median    | Req/sec    |
|-------------------------- |------------ |----------------- |---------------- |----------:|----------:|----------:|----------:|-----------:|
| **PublishAndReceiveMessages** | **AtMostOnce**  | **10**               | **V3_1_1**          |  **4.857 μs** | **0.7428 μs** | **2.1902 μs** |  **4.139 μs** | **205,881.88** |
| **PublishAndReceiveMessages** | **AtMostOnce**  | **1024**             | **V3_1_1**          |  **4.564 μs** | **0.5076 μs** | **1.4968 μs** |  **4.346 μs** | **219,088.41** |
| **PublishAndReceiveMessages** | **AtMostOnce**  | **2048**             | **V3_1_1**          |  **4.984 μs** | **0.3386 μs** | **0.9983 μs** |  **4.661 μs** | **200,642.09** |
| **PublishAndReceiveMessages** | **AtMostOnce**  | **8192**             | **V3_1_1**          | **12.807 μs** | **2.0964 μs** | **6.1811 μs** | **10.064 μs** |  **78,080.79** |
| **PublishAndReceiveMessages** | **AtLeastOnce** | **10**               | **V3_1_1**          | **24.877 μs** | **1.4839 μs** | **4.3754 μs** | **24.051 μs** |  **40,197.85** |
| **PublishAndReceiveMessages** | **AtLeastOnce** | **1024**             | **V3_1_1**          | **24.040 μs** | **0.9618 μs** | **2.8358 μs** | **23.976 μs** |  **41,598.02** |
| **PublishAndReceiveMessages** | **AtLeastOnce** | **2048**             | **V3_1_1**          | **21.982 μs** | **2.3840 μs** | **7.0293 μs** | **23.538 μs** |  **45,490.96** |
| **PublishAndReceiveMessages** | **AtLeastOnce** | **8192**             | **V3_1_1**          | **31.632 μs** | **2.0415 μs** | **6.0194 μs** | **32.232 μs** |  **31,613.79** |
| **PublishAndReceiveMessages** | **ExactlyOnce** | **10**               | **V3_1_1**          | **42.540 μs** | **2.3121 μs** | **6.8172 μs** | **41.198 μs** |  **23,507.18** |
| **PublishAndReceiveMessages** | **ExactlyOnce** | **1024**             | **V3_1_1**          | **41.950 μs** | **2.3413 μs** | **6.9034 μs** | **39.199 μs** |  **23,838.15** |
| **PublishAndReceiveMessages** | **ExactlyOnce** | **2048**             | **V3_1_1**          | **45.286 μs** | **2.3360 μs** | **6.8878 μs** | **44.192 μs** |  **22,081.81** |
| **PublishAndReceiveMessages** | **ExactlyOnce** | **8192**             | **V3_1_1**          | **47.743 μs** | **3.0797 μs** | **9.0805 μs** | **47.786 μs** |  **20,945.61** |


Every benchmark includes 100% of this overhead:

1. `Publisher` --> `Broker`
2. `Broker` --> `Publisher` (at QoS > 0)
3. `Subscriber` --> `Broker`
4. `Broker` --> `Subscriber` (this is primarily what we are interested in)

In other words, this benchmark __is significantly more comprehensive that just measuring raw receive overhead__, especially with `QualityOfService.AtLeastOnce` (1) and `QualityOfService.ExactlyOnce` (2), which require 4x and 16x the amount of end-to-end communication that `QualityOfService.AtMostOnce` (0) does. Therefore, if you're interested in pure receiving or publishing rates, you will get significantly higher values than what this benchmark records.

### Why design the benchmark this way?

We designed the benchmark to include _everything_ in order to make it `git clone` + `dotnet run -c Release` runnable right out of the box. That's the standard for best developer experience, but that means making some comrpomises on accuracy.

### Why the `10b` message size?

We stuck with a relatively low message size because doing anything larger is mostly a matter of scaling `Socket` buffer sizes, and when we perform end-to-end benchmarking with real brokers like [EMQX](https://www.emqx.io/) larger message sizes create memory pressure + availability problems for the broker itself. You can run these yourselves at larger sizes.

But the TL;DR; is: big messages are largely I/O bound problem - the whole purpose of TurboMqtt is make sure your publishing / receive message processing rates aren't CPU bound. Smaller message sizes make that easier to observe.

### Data with Real Brokers

What sort of performance can you expect from real brokers?

![TurboMqtt reading messages off of EMQX via MQTT 3.1.1](img/emqx-mqtt3111.png)

Using some of our application-specific stress tests, we've observed processing rates of ~70k msg/s (as fast as the stress testers could go against a single EMQX instance) running on `QualityOfService.AtLeastOnce` (QoS=`1`) - which is significantly faster than our benchmark data (see, we told you!)

Testing with larger packet sizes, such as 8kb packets, we've observed rates of around 35k msg/s, which translates to roughly 280 mb/s with a single client receiving messages. At those sizes you'll start to run into problems with your message broker long before TurboMqtt has any problems.
