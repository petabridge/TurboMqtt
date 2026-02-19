# Implementation Plan — Phase 2.5: Transport Layer Redesign

> Scoped plan file for RALPH runs targeting Phase 2.5 only.
> Full plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)
> PRD: [docs/prd/transport-redesign/README.md](docs/prd/transport-redesign/README.md)
>
> **Format:** Tasks use `### Task X.Y:` headers with `Done when:` checklists.
> RALPH picks the FIRST incomplete task (has unchecked `- [ ]` items).

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
- [ ] `IStreamProvider` interface exists in `src/TurboMqtt/IO/Tcp/IStreamProvider.cs`
- [ ] `TcpStreamProvider` implementation exists in `src/TurboMqtt/IO/Tcp/TcpStreamProvider.cs`
- [ ] `TcpStreamProvider.ConnectAsync()` creates Socket, resolves DNS, connects, returns `NetworkStream`
- [ ] `TcpTransportActor` constructor takes `IStreamProvider` instead of creating Socket directly
- [ ] `DoWriteToPipeAsync` reads from `Stream.ReadAsync()` instead of `Socket.ReceiveAsync()`
- [ ] `DoWriteToSocketAsync` writes to `Stream.WriteAsync()` instead of `Socket.SendAsync()`
- [ ] `ReadFromPipeAsync` uses `MemoryPool<byte>.Shared.Rent()` instead of `new byte[buffer.Length]`
- [ ] `UnsharedMemoryOwner` no longer used on the read path (may still be used elsewhere)
- [ ] `TcpTransport.cs` updated to pass `IStreamProvider` through
- [ ] `TcpConnectionManager.cs` updated to create appropriate `IStreamProvider`
- [ ] All existing TCP unit tests pass unchanged
- [ ] All container tests pass against EMQX
- [ ] New unit tests for `TcpStreamProvider` (connect, DNS resolution, socket configuration)
- [ ] BenchmarkDotNet before/after confirms no throughput regression (baseline: 193k msg/sec QoS 0)
- [ ] Builds with zero warnings

### Task 2.5-B: Fix transport race conditions

**PRD:** [docs/prd/transport-redesign/README.md](docs/prd/transport-redesign/README.md) §1.2, §5, §6
**Surface area:** cross-cutting
**Verification:** L2

Fix the 12+ identified race conditions in shutdown, reconnection, and transport swap.
Can be developed in parallel with Task 2.5-A.

Done when:
- [ ] `TcpTransportActor` uses explicit `Become` states: `NotStarted → Created → Connecting → Connected → Draining → Closing → Stopped` (and `Aborted` short-circuit)
- [ ] Background tasks in `BecomeRunning()` tracked with `Task.WhenAll` + `ContinueWith` self-tell `BackgroundTasksCompleted`
- [ ] `CleanUpGracefully` replaced with state-driven transitions — no more fire-and-forget async
- [ ] Duplicate `DoClose`/`ReadFinished`/`ConnectionUnexpectedlyClosed` messages in non-handling states are ignored
- [ ] `MqttClient.SwapTransport()` uses `Interlocked.Exchange` + `volatile` field
- [ ] TOCTOU on `IsConnected` in `PublishAsync` eliminated — rely on `TryWrite` returning false
- [ ] `ClientStreamOwner.PostStop()` follows deterministic ordering: complete outbound → abort transport → complete inbound → signal death
- [ ] `ClientStreamOwner` reconnect uses message-driven `Reconnecting` behavior (no fire-and-forget `DoReconnect`)
- [ ] `ReadFromPipeAsync` catch block includes `return` after `Tell(ReadFinished.Instance)`
- [ ] `DisposeSocket` CTS disposal is safe (no double-cancel race with `CleanUpGracefully`)
- [ ] All existing E2E tests pass
- [ ] New test: concurrent disconnect + publish does not deadlock or crash
- [ ] New test: rapid sequential reconnects (3+ in < 1 second) complete without error
- [ ] New test: server kills connection during QoS 2 exchange — client reconnects and retransmits
- [ ] New test: disconnect while large publish in flight — verifies graceful drain
- [ ] Builds with zero warnings

### Task 2.5-C: Add TLS support via TlsStreamProvider

**PRD:** [docs/prd/transport-redesign/README.md](docs/prd/transport-redesign/README.md) §7
**Surface area:** cross-cutting
**Verification:** L2
**Depends on:** Task 2.5-A

Implement TLS/SSL support. This is the payoff of the `IStreamProvider` abstraction.

Done when:
- [ ] `TlsStreamProvider` exists in `src/TurboMqtt/IO/Tcp/TlsStreamProvider.cs`
- [ ] `TlsStreamProvider.ConnectAsync()` creates Socket → `NetworkStream` → `SslStream`, completes TLS handshake
- [ ] `MqttClientTlsOptions` public options class exists in `src/TurboMqtt/Client/MqttClientTlsOptions.cs`
- [ ] `MqttClientTlsOptions` supports: `ClientCertificates`, `ServerCertificateValidationCallback`, `EnabledSslProtocols`, `TargetHost`
- [ ] `IMqttClientFactory.CreateTlsTcpClient()` factory method added
- [ ] `TcpMqttTransportManager` accepts optional TLS options and creates appropriate `IStreamProvider`
- [ ] Container test: connect to EMQX over TLS (port 8883) and publish/subscribe at QoS 0
- [ ] Container test: connect to EMQX over TLS and publish/subscribe at QoS 1
- [ ] Container test: TLS with custom `ServerCertificateValidationCallback` for self-signed certs
- [ ] All existing TCP tests still pass (no regression)
- [ ] `PROJECT_CONTEXT.md` protocol support table updated: TLS status changed from "In-flight" to "Implemented"
- [ ] Builds with zero warnings

### Task 2.5-D: Transport lifecycle hardening

**PRD:** [docs/prd/transport-redesign/README.md](docs/prd/transport-redesign/README.md) §5, §6
**Surface area:** cross-cutting
**Verification:** L2
**Depends on:** Tasks 2.5-A + 2.5-B

Formalize the transport state machine and graceful drain to production quality.

Done when:
- [ ] Full FSM with explicit state transitions and structured logging at each transition
- [ ] `ConnectionState` shared mutable state replaced with actor messages or thread-safe wrappers
- [ ] Graceful drain: `Draining` state where outbound flushes before DISCONNECT is sent
- [ ] Connect timeout with cancellation propagation (configurable, default 10s)
- [ ] Actor test: verify all state transitions with TestProbe (`NotStarted → Created → Connecting → Connected → Draining → Closing → Stopped`)
- [ ] Actor test: verify `Aborted` short-circuit path
- [ ] Test: disconnect while large publish in flight — outbound flushes before close
- [ ] Test: connect timeout fires when broker is unreachable
- [ ] All E2E tests pass
- [ ] Builds with zero warnings

---

## Dependency Graph

```
2.5-A (Stream abstraction) <--> 2.5-B (race fixes)  [can run in parallel]
2.5-A --> 2.5-C (TLS depends on IStreamProvider)
2.5-A + 2.5-B --> 2.5-D (hardening depends on both)
```
