#### 0.1.1 May 2nd 2024 ####

TurboMqtt v0.1.1 includes critical bug fixes and massive performance improvements over v0.1.0.

**Bug Fixes and Improvements**

* [Fixed QoS=1 packet handling - was previously treating it like QoS=2](https://github.com/petabridge/TurboMqtt/pull/103).
* [Improved flow control inside `ClientAckHandler`](https://github.com/petabridge/TurboMqtt/pull/105) - result is a massive performance improvement when operating at QoS 1 and 2.
* [Fix OpenTelemetry `TagList` for clientId and MQTT version](https://github.com/petabridge/TurboMqtt/pull/104) - now we can accurately track metrics per clientId via OpenTelemetry.

**Performance**

```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.22631.3447/23H2/2023Update/SunValley3)
12th Gen Intel Core i7-1260P, 1 CPU, 16 logical and 12 physical cores
.NET SDK 8.0.101
  [Host]     : .NET 8.0.1 (8.0.123.58001), X64 RyuJIT AVX2
  Job-FBXRHG : .NET 8.0.1 (8.0.123.58001), X64 RyuJIT AVX2

InvocationCount=1  LaunchCount=10  RunStrategy=Monitoring  
UnrollFactor=1  WarmupCount=10  

```
| Method                    | QoSLevel    | PayloadSizeBytes | ProtocolVersion | Mean      | Error     | StdDev   | Median    | Req/sec    |
|-------------------------- |------------ |----------------- |---------------- |----------:|----------:|---------:|----------:|-----------:|
| **PublishAndReceiveMessages** | **AtMostOnce**  | **10**               | **V3_1_1**          |  **5.175 μs** | **0.6794 μs** | **2.003 μs** |  **4.345 μs** | **193,230.35** |
| **PublishAndReceiveMessages** | **AtLeastOnce** | **10**               | **V3_1_1**          | **26.309 μs** | **1.4071 μs** | **4.149 μs** | **25.906 μs** |  **38,010.35** |
| **PublishAndReceiveMessages** | **ExactlyOnce** | **10**               | **V3_1_1**          | **44.501 μs** | **2.2778 μs** | **6.716 μs** | **42.175 μs** |  **22,471.53** |


[Learn more about TurboMqtt's performance figures here](https://github.com/petabridge/TurboMqtt/blob/dev/docs/Performance.md).