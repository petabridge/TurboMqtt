# TurboMqtt Benchmarks

This directory contains BenchmarkDotNet performance benchmarks for TurboMqtt.

## Running Benchmarks

### Prerequisites

- .NET 10.0 SDK or later
- For MQTT broker-based tests: Docker (optional, for EMQX broker testing)

The in-process fake MQTT server benchmarks are self-contained and require no external dependencies.

### Running All Benchmarks

From the repository root:

```bash
dotnet run -c Release --project benchmarks/TurboMqtt.Benchmarks
```

This will run all benchmarks with the configured settings (RunStrategy=Monitoring, LaunchCount=10, WarmupCount=10) which may take 10-30 minutes depending on your hardware.

### Running Specific Benchmarks

To run only a specific benchmark class or method, use the `--filter` argument:

```bash
# MQTT 3.1.1 end-to-end TCP benchmarks
dotnet run -c Release --project benchmarks/TurboMqtt.Benchmarks -- --filter '*Mqtt311EndToEndTcpBenchmarks*'

# MQTT 5.0 end-to-end TCP benchmarks
dotnet run -c Release --project benchmarks/TurboMqtt.Benchmarks -- --filter '*Mqtt5EndToEndTcpBenchmarks*'

# MQTT 5.0 TLS benchmarks
dotnet run -c Release --project benchmarks/TurboMqtt.Benchmarks -- --filter '*Mqtt5TlsTcpBenchmarks*'

# Codec benchmarks (encoding/decoding performance)
dotnet run -c Release --project benchmarks/TurboMqtt.Benchmarks -- --filter '*CodecBenchmarks*'

# Throughput benchmarks
dotnet run -c Release --project benchmarks/TurboMqtt.Benchmarks -- --filter '*ThroughputBenchmarks*'
```

### Quick Benchmarks (Development)

For faster iteration during development, create a custom configuration with fewer launches and warmups:

```csharp
// Add to Program.cs in benchmarks/TurboMqtt.Benchmarks
var config = ManualConfig
    .Create(DefaultConfig.Instance)
    .WithOptions(ConfigOptions.DisableOptimizationsValidator)
    .AddDiagnoser(new MemoryDiagnoser())
    .AddJob(new SimpleJob(1, 3, 3));  // 1 launch, 3 warmup, 3 actual

BenchmarkRunner.Run<Mqtt311EndToEndTcpBenchmarks>(config);
```

## Benchmark Categories

### End-to-End Benchmarks

**Location:** `Mqtt311/Mqtt311End2EndTcpBenchmarks.cs`, `Mqtt5/Mqtt5End2EndTcpBenchmarks.cs`, `Mqtt5/Mqtt5TlsTcpBenchmarks.cs`

These benchmarks measure the full round-trip latency of publishing and receiving messages through a fake in-process MQTT broker. Each benchmark iteration:

1. Publishes N messages (1,000 default) at a specified QoS level
2. Receives the same messages back from the broker
3. Measures the per-operation latency

**Key Metrics:**
- Mean: Average time per message round-trip (in microseconds)
- Req/sec: Throughput in messages per second (accounting for OperationsPerInvoke)
- StdDev: Standard deviation of measurements

**Test Parameters:**
- **QoS Levels:** AtMostOnce (0), AtLeastOnce (1), ExactlyOnce (2)
- **Payload Sizes:** 10 bytes, 1 KB, 2 KB, 8 KB
- **Protocols:** MQTT 3.1.1, MQTT 5.0

### Codec Benchmarks

**Location:** `Mqtt311/Mqtt311EncoderComparisonBenchmarks.cs`, `Mqtt311/Mqtt311PublishCodecBenchmarks.cs`, etc.

These micro-benchmarks measure the encoding and decoding performance of MQTT packets in isolation:

```bash
# Run codec benchmarks only
dotnet run -c Release --project benchmarks/TurboMqtt.Benchmarks -- --filter '*Codec*'
```

### Throughput Benchmarks

**Location:** `Mqtt311/Mqtt311ThroughputBenchmarks.cs`

Measures raw message throughput without the acknowledgment overhead of higher QoS levels.

## Understanding Benchmark Results

### Example Output

```
| Method | QoSLevel   | PayloadSizeBytes | Mean     | Error   | StdDev  | Req/sec   |
|--------|-----------|-----------------|----------|---------|---------|-----------|
| PublishAndReceiveMessages | AtMostOnce | 10  | 3.243 μs | 0.156 μs | 0.460 μs | 308,333  |
```

**Interpretation:**
- **Mean:** On average, a publish→receive round-trip takes 3.243 microseconds
- **Error:** Measurement error of ±0.156 microseconds
- **StdDev:** Standard deviation of 0.460 microseconds (measure of consistency)
- **Req/sec:** About 308k round-trips per second
  - Note: Due to OperationsPerInvoke=2000, this represents 2000 send + 2000 receive operations per invocation

### Performance Expectations

See [docs/Performance.md](../docs/Performance.md) for:
- Baseline numbers on different hardware
- Comparison between QoS levels
- Impact of protocol versions (MQTT 3.1.1 vs 5.0)
- Real broker performance vs in-process fake server

## Benchmark Design Notes

### Why In-Process Fake Server?

TurboMqtt benchmarks use `FakeMqttTcpServer` (in-process) rather than a real broker like EMQX for several reasons:

1. **Reproducibility** — No broker installation or Docker required
2. **Consistency** — Network latency and broker overhead are eliminated
3. **Simplicity** — `git clone && dotnet run -c Release` just works

**Trade-off:** In-process results show TurboMqtt's peak potential, not real-world performance. For real broker measurements, see [docs/Performance.md#data-with-real-brokers](../docs/Performance.md).

### Benchmark Configuration

All end-to-end benchmarks use:

```csharp
[SimpleJob(RunStrategy.Monitoring, launchCount: 10, warmupCount: 10)]
[Config(typeof(MonitoringConfig))]
public class Mqtt311EndToEndTcpBenchmarks
```

**Settings Explanation:**
- `RunStrategy.Monitoring` — Statistical monitoring mode (more robust than throughput)
- `launchCount: 10` — Run 10 independent launches to reduce noise
- `warmupCount: 10` — Warm up the JIT and CPU cache before measurements
- `OperationsPerInvoke = 1000 * 2` — Each iteration publishes and receives 1000 messages

### Operation Counting

For end-to-end benchmarks with OperationsPerInvoke = 2000:
- 1000 publish operations + 1000 receive operations = 2000 operations per invocation
- Req/sec = (iterations × 2000 operations) / total time

This ensures fair comparison with codec benchmarks that measure single encode/decode operations.

## Investigating Regressions

If you suspect a performance regression:

1. **Run baseline benchmarks** on the main branch:
   ```bash
   git checkout main
   dotnet run -c Release --project benchmarks/TurboMqtt.Benchmarks -- --filter '*Mqtt311EndToEndTcp*'
   # Results saved to BenchmarkDotNet.Artifacts/results/
   ```

2. **Switch to your branch** and run the same benchmark:
   ```bash
   git checkout your-branch
   dotnet run -c Release --project benchmarks/TurboMqtt.Benchmarks -- --filter '*Mqtt311EndToEndTcp*'
   ```

3. **Compare results** — BenchmarkDotNet generates an HTML report:
   - Open `benchmarks/TurboMqtt.Benchmarks/bin/Release/net10.0/BenchmarkDotNet.Artifacts/results/index.html`
   - Look for columns: Mean, Error, and StdDev
   - Determine if the difference is within measurement noise (±1-2%)

4. **Profile if needed** — For larger regressions, use:
   - JetBrains Profiler: Profile the benchmark
   - CPU Profiler: Identify hotspots
   - Memory Profiler: Check for allocation differences

## Adding New Benchmarks

1. Create a new benchmark class in the appropriate folder:
   ```csharp
   [SimpleJob(RunStrategy.Monitoring, launchCount: 10, warmupCount: 10)]
   [Config(typeof(MonitoringConfig))]
   public class MyNewBenchmarks
   {
       [Params(param1Values)]
       public ParamType Param1 { get; set; }

       [GlobalSetup]
       public void Setup() { }

       [Benchmark]
       public void MyBenchmark() { }
   }
   ```

2. Ensure the benchmark is discoverable by BenchmarkRunner
3. Run with `--filter '*MyNewBenchmarks*'` to test
4. Document the benchmark's purpose in this README

## Continuous Integration

Benchmarks are not run in CI by default (too slow). To run benchmarks in CI:

1. Add a separate CI job with extended timeout (30+ minutes)
2. Upload results as CI artifacts
3. Compare against baseline results from main branch
4. Fail CI if regressions exceed threshold (e.g., >5%)

## References

- [BenchmarkDotNet Documentation](https://benchmarkdotnet.org/)
- [TurboMqtt Performance Documentation](../docs/Performance.md)
- [MQTT 3.1.1 Specification](http://docs.oasis-open.org/mqtt/mqtt/v3.1.1/mqtt-v3.1.1.html)
- [MQTT 5.0 Specification](https://docs.oasis-open.org/mqtt/mqtt/v5.0/mqtt-v5.0.html)
