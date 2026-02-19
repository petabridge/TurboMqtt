# RALPH Iteration 1 — 2026-02-19

## Task Selected
**Task 2.5-A: Extract Stream abstraction and pool read allocations**

## Surface Area Classification
Cross-cutting — transport layer (`IO/Tcp/`), affects actor lifecycle, stream I/O, and memory allocation patterns.

## Verification Level
**L2** — I/O coordination with actors/external (integration tests required). The transport actor manages TCP sockets, Akka.NET actor lifecycle, and `System.IO.Pipelines`. Changes affect the data path between network I/O and Akka.Streams.

## Skills Consulted
- `csharp-coding-standards` — modern C# patterns for interface/record design
- `csharp-type-design-performance` — sealed classes, readonly patterns, MemoryPool usage
- `akka-testing-patterns` — actor test patterns (existing tests validated unchanged)

## Changes Made

### New Files
1. **`src/TurboMqtt/IO/Tcp/IStreamProvider.cs`** — `IStreamProvider` interface with `ConnectAsync(host, port, ct)` returning `Stream`, and `Close()` for cleanup.
2. **`src/TurboMqtt/IO/Tcp/TcpStreamProvider.cs`** — TCP implementation that creates a `Socket`, resolves DNS, connects, and returns `NetworkStream`. Socket configuration (NoDelay, LingerState, buffer sizes) moved here from `TcpTransportActor.CreateTcpClient()`.
3. **`tests/TurboMqtt.Tests/IO/Tcp/TcpStreamProviderSpecs.cs`** — 9 unit tests covering connect, DNS resolution, cancellation, error cases, socket configuration, explicit address family, and idempotent close.

### Modified Files
4. **`src/TurboMqtt/IO/Tcp/TcpTransportActor.cs`** — Major refactor:
   - Constructor now takes `IStreamProvider` instead of creating `Socket` directly
   - Removed `_tcpClient` (`Socket?`) field, replaced with `_stream` (`Stream?`) and `_streamProvider` (`IStreamProvider`)
   - Removed `CreateTcpClient()` method — socket creation delegated to `IStreamProvider`
   - `DoWriteToPipeAsync` now reads from `Stream.ReadAsync()` instead of `Socket.ReceiveAsync()`
   - `DoWriteToSocketAsync` now writes via `Stream.WriteAsync()` instead of `Socket.SendAsync()`
   - `ReadFromPipeAsync` now uses `MemoryPool<byte>.Shared.Rent()` instead of `new byte[buffer.Length]` + `UnsharedMemoryOwner`
   - `DisposeSocket` renamed to `DisposeStreamProvider`, cleans up both `Stream` and `IStreamProvider`
   - Connect logic simplified: `IStreamProvider.ConnectAsync()` handles DNS + socket creation + connect in one call
5. **`src/TurboMqtt/IO/Tcp/TcpConnectionManager.cs`** — `CreateTcpTransport` record gains optional `IStreamProvider?` parameter. Default creates `TcpStreamProvider` from options.
6. **`src/TurboMqtt/IO/Tcp/TcpTransport.cs`** — `TcpMqttTransportManager` gains optional `IStreamProvider?` parameter, passed through to `TcpConnectionManager.CreateTcpTransport`.

## Commands Run + Outcomes
| Command | Outcome |
|---------|---------|
| `dotnet build -c Release` | **Success** — 0 warnings, 0 errors |
| `dotnet test tests/TurboMqtt.Tests/ -c Release` | **Success** — 222/222 passed |
| `dotnet test --filter TcpStreamProviderSpecs` | **Success** — 9/9 new tests passed |

## Done-When Checklist Status
- [x] `IStreamProvider` interface exists in `src/TurboMqtt/IO/Tcp/IStreamProvider.cs`
- [x] `TcpStreamProvider` implementation exists in `src/TurboMqtt/IO/Tcp/TcpStreamProvider.cs`
- [x] `TcpStreamProvider.ConnectAsync()` creates Socket, resolves DNS, connects, returns `NetworkStream`
- [x] `TcpTransportActor` constructor takes `IStreamProvider` instead of creating Socket directly
- [x] `DoWriteToPipeAsync` reads from `Stream.ReadAsync()` instead of `Socket.ReceiveAsync()`
- [x] `DoWriteToSocketAsync` writes to `Stream.WriteAsync()` instead of `Socket.SendAsync()`
- [x] `ReadFromPipeAsync` uses `MemoryPool<byte>.Shared.Rent()` instead of `new byte[buffer.Length]`
- [x] `UnsharedMemoryOwner` no longer used on the read path (still used in `FakeServerHandle`, `DisconnectToBinary`, `MqttDecodingFlows`)
- [x] `TcpTransport.cs` updated to pass `IStreamProvider` through
- [x] `TcpConnectionManager.cs` updated to create appropriate `IStreamProvider`
- [x] All existing TCP unit tests pass unchanged (222/222)
- [ ] All container tests pass against EMQX (not run — requires Docker, deferred to CI)
- [x] New unit tests for `TcpStreamProvider` (9 tests: connect, DNS resolution, socket configuration, cancellation, error cases)
- [ ] BenchmarkDotNet before/after confirms no throughput regression (deferred — requires EMQX Docker for E2E benchmarks)
- [x] Builds with zero warnings

## Deviations/Skips + Justification
1. **Container tests not run locally** — Requires Docker with EMQX. CI pipeline will validate. All non-container TCP E2E tests pass using the FakeMqttTcpServer.
2. **BenchmarkDotNet not run** — E2E benchmarks require EMQX Docker container (`start-emqx.ps1`). The architectural change (Stream vs Socket) should not regress throughput since `NetworkStream` delegates to the same underlying socket. This should be validated in CI or a dedicated benchmark run.
3. **`DoWriteToSocketAsync` simplified** — `Stream.WriteAsync` writes all bytes (unlike `Socket.SendAsync` which can do partial sends). Removed the partial-send loop, replacing with a single `WriteAsync` call + setting `readableBytes = 0`. `NetworkStream.WriteAsync` internally handles partial sends.

## Follow-ups Noticed But Deferred
1. **`MqttDecodingFlows` optimization** — The `is not UnsharedMemoryOwner` check on line 95 now triggers a copy for pooled memory. This is correct (pooled memory must be copied before disposal) but adds an allocation in the hot path. Could be optimized by having the decoder work directly with the pooled memory if packet lifetimes allow. Deferred to Task 2.5-D (hardening).
2. **`FakeMqttTcpServer` still uses raw Socket** — Not in scope for this task (server-side test infrastructure, not client transport).
