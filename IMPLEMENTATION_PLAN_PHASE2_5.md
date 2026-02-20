# Implementation Plan — Phase 2.5: Transport Layer Redesign

> Scoped plan file for RALPH runs targeting Phase 2.5 only.
> Full plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)
> PRD: [docs/prd/transport-redesign/README.md](docs/prd/transport-redesign/README.md)
>
> **Format:** Tasks use `### Task X.Y:` headers with `Done when:` checklists.
> RALPH picks the FIRST incomplete task (has unchecked `- [ ]` items).

---

### FIX: Add missing F-5 PARK item to BACKLOG_PARKING_LOT.md

**Source:** Adversarial review 20260220-000928 iter-06, finding R-1
**Surface area:** documentation
**Verification:** L0

The prior review's finding F-5 (`_transport` field visibility in `IMqttClient.cs`) was dispositioned as PARK but not added to the parking lot.

Done when:
- [x] `BACKLOG_PARKING_LOT.md` has an entry for F-5: `_transport` field in `IMqttClient.cs` should be annotated with a comment noting that reads must go through the `Transport` property (which uses `Volatile.Read`)

---

### FIX: Add logging for failed resubscribe during reconnect

**Source:** Adversarial review 20260220-000928, finding F-3
**Surface area:** cross-cutting
**Verification:** L1

The empty `if (!subscribeResp.IsSuccess) { }` block in `BeginReconnect()` silently swallows
subscription failures during reconnect. At minimum, log the failure.

Done when:
- [x] `ClientStreamOwner.cs` `BeginReconnect()` method: add `_log.Warning(...)` inside the `if (!subscribeResp.IsSuccess)` block reporting the failure reason and number of topics
- [x] Builds with zero warnings

---

## Phase 2.5: Transport Layer Redesign

> Goal: Fix 12+ race conditions in the transport/lifecycle layer, eliminate GC pressure
> from unpooled read allocations, introduce a `Stream` abstraction to enable TLS, and
> formalize the transport actor state machine. This must happen before MQTT 5.0 because
> the transport layer needs to be solid before adding protocol complexity.
>
> **PRD:** [docs/prd/transport-redesign/README.md](docs/prd/transport-redesign/README.md)

### Task 2.5-A: Extract Stream abstraction and pool read allocations

**PRD:** [docs/prd/transport-redesign/README.md](docs/prd/transport-redesign/README.md) §4, §8
**Surface area:** cross-cutting
**Verification:** L2

Introduce `IStreamProvider` + `TcpStreamProvider`, refactor `TcpTransportActor` to use
`Stream.ReadAsync`/`Stream.WriteAsync` instead of `Socket.ReceiveAsync`/`Socket.SendAsync`,
and replace `new byte[]` allocations in `ReadFromPipeAsync` with `MemoryPool<byte>.Shared.Rent()`.

Done when:
- [x] `IStreamProvider` interface exists in `src/TurboMqtt/IO/Tcp/IStreamProvider.cs`
- [x] `TcpStreamProvider` implementation exists in `src/TurboMqtt/IO/Tcp/TcpStreamProvider.cs`
- [x] `TcpStreamProvider.ConnectAsync()` creates Socket, resolves DNS, connects, returns `NetworkStream`
- [x] `TcpTransportActor` constructor takes `IStreamProvider` instead of creating Socket directly
- [x] `DoWriteToPipeAsync` reads from `Stream.ReadAsync()` instead of `Socket.ReceiveAsync()`
- [x] `DoWriteToSocketAsync` writes to `Stream.WriteAsync()` instead of `Socket.SendAsync()`
- [x] `ReadFromPipeAsync` uses `MemoryPool<byte>.Shared.Rent()` instead of `new byte[buffer.Length]`
- [x] `UnsharedMemoryOwner` no longer used on the read path (may still be used elsewhere)
- [x] `TcpTransport.cs` updated to pass `IStreamProvider` through
- [x] `TcpConnectionManager.cs` updated to create appropriate `IStreamProvider`
- [x] All existing TCP unit tests pass unchanged
- [x] All container tests pass against EMQX
- [x] New unit tests for `TcpStreamProvider` (connect, DNS resolution, socket configuration)
- [x] BenchmarkDotNet before/after confirms no throughput regression (baseline: 193k msg/sec QoS 0)
- [x] Builds with zero warnings

### Task 2.5-B: Fix transport race conditions

**PRD:** [docs/prd/transport-redesign/README.md](docs/prd/transport-redesign/README.md) §1.2, §5, §6
**Surface area:** cross-cutting
**Verification:** L2

Fix the 12+ identified race conditions in shutdown, reconnection, and transport swap.
Can be developed in parallel with Task 2.5-A.

Done when:
- [x] `TcpTransportActor` uses explicit `Become` states: `NotStarted → Created → Connecting → Connected → Draining → Closing → Stopped` (and `Aborted` short-circuit)
- [x] Background tasks in `BecomeRunning()` tracked with `Task.WhenAll` + `ContinueWith` self-tell `BackgroundTasksCompleted`
- [x] `CleanUpGracefully` replaced with state-driven transitions — no more fire-and-forget async
- [x] Duplicate `DoClose`/`ReadFinished`/`ConnectionUnexpectedlyClosed` messages in non-handling states are ignored
- [x] `MqttClient.SwapTransport()` uses `Interlocked.Exchange` + `volatile` field
- [x] TOCTOU on `IsConnected` in `PublishAsync` eliminated — rely on `TryWrite` returning false
- [x] `ClientStreamOwner.PostStop()` follows deterministic ordering: complete outbound → abort transport → complete inbound → signal death
- [x] `ClientStreamOwner` reconnect uses message-driven `Reconnecting` behavior (no fire-and-forget `DoReconnect`)
- [x] `ReadFromPipeAsync` catch block includes `return` after `Tell(ReadFinished.Instance)`
- [x] `DisposeSocket` CTS disposal is safe (no double-cancel race with `CleanUpGracefully`)
- [x] All existing E2E tests pass
- [x] New test: concurrent disconnect + publish does not deadlock or crash
- [x] New test: rapid sequential reconnects (3+ in < 1 second) complete without error
- [x] New test: server kills connection during QoS 2 exchange — client reconnects and retransmits
- [x] New test: disconnect while large publish in flight — verifies graceful drain
- [x] Builds with zero warnings

### Task 2.5-C: Add TLS support via TlsStreamProvider

**PRD:** [docs/prd/transport-redesign/README.md](docs/prd/transport-redesign/README.md) §7
**Surface area:** cross-cutting
**Verification:** L2
**Depends on:** Task 2.5-A

Implement TLS/SSL support. This is the payoff of the `IStreamProvider` abstraction.

Done when:
- [x] `TlsStreamProvider` exists in `src/TurboMqtt/IO/Tcp/TlsStreamProvider.cs`
- [x] `TlsStreamProvider.ConnectAsync()` creates Socket → `NetworkStream` → `SslStream`, completes TLS handshake
- [x] `MqttClientTlsOptions` public options class exists in `src/TurboMqtt/Client/MqttClientTlsOptions.cs`
- [x] `MqttClientTlsOptions` supports: `ClientCertificates`, `ServerCertificateValidationCallback`, `EnabledSslProtocols`, `TargetHost`
- [x] `IMqttClientFactory.CreateTlsTcpClient()` factory method added
- [x] `TcpMqttTransportManager` accepts optional TLS options and creates appropriate `IStreamProvider`
- [x] Container test: connect to EMQX over TLS (port 8883) and publish/subscribe at QoS 0
- [x] Container test: connect to EMQX over TLS and publish/subscribe at QoS 1
- [x] Container test: TLS with custom `ServerCertificateValidationCallback` for self-signed certs
- [x] All existing TCP tests still pass (no regression)
- [x] `PROJECT_CONTEXT.md` protocol support table updated: TLS status changed from "In-flight" to "Implemented"
- [x] Builds with zero warnings

### Task 2.5-D: Transport lifecycle hardening

**PRD:** [docs/prd/transport-redesign/README.md](docs/prd/transport-redesign/README.md) §5, §6
**Surface area:** cross-cutting
**Verification:** L2
**Depends on:** Tasks 2.5-A + 2.5-B

Formalize the transport state machine and graceful drain to production quality.

Done when:
- [x] Full FSM with explicit state transitions and structured logging at each transition
- [x] `ConnectionState` shared mutable state replaced with actor messages or thread-safe wrappers
- [x] Graceful drain: `Draining` state where outbound flushes before DISCONNECT is sent
- [x] Connect timeout with cancellation propagation (configurable, default 10s)
- [x] Actor test: verify all state transitions with TestProbe (`NotStarted → Created → Connecting → Connected → Draining → Closing → Stopped`)
- [x] Actor test: verify `Aborted` short-circuit path
- [x] Test: disconnect while large publish in flight — outbound flushes before close
- [x] Test: connect timeout fires when broker is unreachable
- [x] All E2E tests pass
- [x] Builds with zero warnings

---

## Dependency Graph

```
2.5-A (Stream abstraction) <--> 2.5-B (race fixes)  [can run in parallel]
2.5-A --> 2.5-C (TLS depends on IStreamProvider)
2.5-A + 2.5-B --> 2.5-D (hardening depends on both)
```
