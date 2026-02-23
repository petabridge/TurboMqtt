# Project Context

## What Is TurboMqtt

TurboMqtt is a high-performance MQTT client library for .NET, built on Akka.NET and Akka.Streams. It aims to be the **fastest, most resource-efficient MQTT client in the .NET ecosystem** - a streaming-first solution for large-scale IoT workloads.

Published as a NuGet package under Apache 2.0 by [Petabridge](https://petabridge.com/).

## Who It's For

.NET developers building IoT applications that need:

- 100k+ msg/s throughput from a single client
- Backpressure-aware message consumption via `System.Threading.Channels`
- Automatic QoS 1/2 acknowledgment and retry
- OpenTelemetry observability out of the box
- Fault-tolerant connections via Akka.NET supervision

## Architecture

### Core Stack

- **Akka.NET actors** for connection lifecycle, heartbeat, ACK tracking, and publish retries
- **Akka.Streams** for encode/decode/receive pipelines with `BatchWeighted` frame packing
- **System.Threading.Channels** for lock-free producer/consumer bridging to user code
- **System.Buffers.MemoryPool\<byte\>** for pooled memory allocation; `ReadOnlyMemory<byte>` payloads

### Actor Hierarchy

```
ActorSystem
├── turbomqtt-clients (ClientManagerActor)
│   ├── tcp (TcpConnectionManager)
│   │   └── tcp-transport-N (TcpTransportActor)
│   └── mqttclient-{clientId}-N (ClientStreamOwner)
│       ├── heartbeat (HeartBeatActor)
│       ├── acks (ClientAcksActor)
│       ├── publish-qos1 (AtLeastOncePublishRetryActor)
│       ├── publish-qos2 (ExactlyOncePublishRetryActor)
│       └── stream (ClientStreamInstance)
```

### Protocol Support

| Protocol | Status |
|----------|--------|
| MQTT 3.1.1 | Implemented |
| MQTT 5.0 | Implemented (encoder/decoder, User Properties, server-initiated DISCONNECT, auth; validated against EMQX 5.x) |
| MQTT over QUIC | Roadmap only |
| TLS | Implemented (via `TlsStreamProvider` + `MqttClientTlsOptions`) |

### Key Constraints

- Akka.NET is a core architectural dependency (current version: 1.5.60)
- AOT compatibility is a longer-term goal, blocked on Akka.NET v1.6
- `TreatWarningsAsErrors` is enabled globally
- `AllowUnsafeBlocks` is enabled for performance-critical code paths
- Single NuGet package target: `net10.0`

## Current State

- **Version**: 1.0.0-beta1 (in-progress; last published release was 0.2.0, June 2024)
- **Priority**: v1.0.0 release validation
- **Known issues**: Signing workflow untested with new certificate (first live test on beta push)

## Roadmap

1. v1.0.0 stable release
2. MQTT over QUIC (epic #68)

## Repository Layout

```
src/TurboMqtt/           # Core library
tests/TurboMqtt.Tests/   # Unit + integration tests (xUnit, FsCheck, Akka.Hosting.TestKit)
tests/TurboMqtt.Container.Tests/  # E2E tests against real brokers via TestContainers
tests/TestContainers.TurboMqtt/   # Custom TestContainers for EMQX
benchmarks/TurboMqtt.Benchmarks/  # BenchmarkDotNet performance tests
samples/                 # DevNullConsumer, BackpressureProducer
scripts/                 # Build, version bump, signing scripts
docs/                    # Performance docs, telemetry docs
```
