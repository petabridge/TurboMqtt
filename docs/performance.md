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

## Benchmark Design

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
