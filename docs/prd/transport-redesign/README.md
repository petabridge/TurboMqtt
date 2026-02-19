# PRD: Transport Layer Redesign

**Status:** Draft
**Date:** 2026-02-19
**Phase:** 2.5 (between MQTT 3.1.1 Hardening and MQTT 5.0 Implementation)

---

## 1. Problem Statement

TurboMqtt's TCP transport layer (`TcpTransportActor`, `TcpTransport`, `ClientStreamOwner`) has accumulated 12+ race conditions, blocks TLS support, and performs unnecessary heap allocations on every inbound read. These issues must be resolved before adding MQTT 5.0 protocol complexity.

### 1.1 Current Architecture

```
Socket.ReceiveAsync → Pipe.Writer → Pipe.Reader → new byte[] COPY → Channel → Akka.Streams
                                                                         ↑
Socket.SendAsync ← Channel ← Akka.Streams                              |
                                                                   UnsharedMemoryOwner
                                                                   (no-op Dispose)
```

**Key components:**

| Component | File | Responsibility |
|-----------|------|----------------|
| `TcpTransportActor` | `src/TurboMqtt/IO/Tcp/TcpTransportActor.cs` | Owns Socket, Pipe, Channels; runs 3 background tasks |
| `TcpTransport` | `src/TurboMqtt/IO/Tcp/TcpTransport.cs` | `IMqttTransport` facade over actor state |
| `TcpConnectionManager` | `src/TurboMqtt/IO/Tcp/TcpConnectionManager.cs` | Actor that creates `TcpTransportActor` children |
| `ClientStreamOwner` | `src/TurboMqtt/Client/ClientStreamOwner.cs` | Owns client lifecycle, reconnection, stream creation |
| `ClientStreamInstance` | `src/TurboMqtt/Client/ClientStreamInstance.cs` | Creates Akka.Streams graphs for encode/decode |
| `MqttClient` | `src/TurboMqtt/Client/IMqttClient.cs` | Public API, holds `IMqttTransport` reference |

### 1.2 Catalogued Race Conditions

| # | Location | Description | Severity |
|---|----------|-------------|----------|
| 1 | `TcpTransportActor.BecomeRunning()` | Three background tasks (`DoWriteToPipeAsync`, `ReadFromPipeAsync`, `DoWriteToSocketAsync`) launched with `#pragma warning disable CS4014` — fire-and-forget with no tracking, no `Task.WhenAll`, no completion self-tell | High |
| 2 | `TcpTransportActor.CleanUpGracefully()` | Uses `_ = CleanUpGracefully(true)` — fire-and-forget async from actor message handler. The `Task.Delay(100)` is a timing hack that may not be sufficient | High |
| 3 | `TcpTransportActor.CleanUpGracefully()` | Sends `PoisonPill.Instance` to self at end, but `CleanUpGracefully` can be invoked multiple times from `DoClose`, `ReadFinished`, and `ConnectionUnexpectedlyClosed` messages — no idempotency guard | High |
| 4 | `TcpTransportActor.Running()` | All three message types (`DoClose`, `ReadFinished`, `ConnectionUnexpectedlyClosed`) fire-and-forget `CleanUpGracefully(true)` — concurrent invocations race against each other | High |
| 5 | `ClientStreamOwner.PostStop()` | Calls `_currentTransport?.AbortAsync()` as fire-and-forget — no await, no tracking | Medium |
| 6 | `ClientStreamOwner.Running()` `ServerDisconnect` | Calls `_ = _currentTransport?.AbortAsync()` fire-and-forget, then `Context.Stop(_streamInstanceOwner)` — stop may execute before abort completes | Medium |
| 7 | `ClientStreamOwner.Running()` `TransportFailedToConnect` | Same fire-and-forget `_ = _currentTransport?.AbortAsync()` pattern | Medium |
| 8 | `ClientStreamOwner.Running()` `StreamTerminated` | `DoReconnect` is fire-and-forget (`_ = DoReconnect(...)`) inside a `RunTask` — the RunTask itself is awaited, but `DoReconnect` inside it is not | High |
| 9 | `MqttClient.SwapTransport()` | Plain field assignment `_transport = newTransport` — not thread-safe if `PublishAsync` or `IsConnected` reads `_transport` concurrently | Medium |
| 10 | `MqttClient.PublishAsync()` / `IsConnected` | TOCTOU: checks `_transport.Status != ConnectionStatus.Connected` then proceeds to write — status can change between check and write | Low |
| 11 | `ClientStreamInstance.NotifyOnDisconnect()` | Fire-and-forget `async Task` — if `disconnectPromise.Task` never completes, this task leaks | Low |
| 12 | `TcpTransportActor.ReadFromPipeAsync()` | `catch (OperationCanceledException)` does not `return` — after `_closureSelf.Tell(ReadFinished.Instance)`, execution falls through to next loop iteration and may `ReadAsync` again on a cancelled token | Medium |
| 13 | `TcpTransportActor.DisposeSocket()` / `PostStop()` | `ShutDownCts.Cancel()` is called synchronously (not `CancelAsync()`) — may throw if CTS is already disposed. Called from `FullShutdown` which is called from `PostStop`, racing with `CleanUpGracefully` which calls `CancelAsync()` | Medium |

### 1.3 GC Pressure from Unpooled Allocations

In `TcpTransportActor.ReadFromPipeAsync()` (line 445):

```csharp
var newMemory = new Memory<byte>(new byte[buffer.Length]);
var unshared = new UnsharedMemoryOwner<byte>(newMemory);
buffer.CopyTo(newMemory.Span);
```

Every inbound read allocates a fresh `byte[]` on the heap and wraps it in an `UnsharedMemoryOwner<T>` whose `Dispose()` is a no-op. This means:
- **Full buffer copy** on every read (Pipe → heap array)
- **No pooling** — allocations hit GC (Gen 0 pressure at 193k msg/sec)
- **No real disposal** — `UnsharedMemoryOwner.Dispose()` does nothing, so memory lifetime is tied to GC rather than deterministic cleanup

The fix: use `MemoryPool<byte>.Shared.Rent()` to produce a real `IMemoryOwner<byte>` that returns to the pool on `Dispose()`.

### 1.4 No TLS Support Path

`TcpTransportActor` creates a raw `Socket` and calls `Socket.ReceiveAsync()` / `Socket.SendAsync()` directly. There is no `Stream` abstraction layer where `SslStream` could be inserted. TLS requires:

1. `Socket` → `NetworkStream` → `SslStream` wrapping
2. Reading/writing via `Stream.ReadAsync()` / `Stream.WriteAsync()` instead of `Socket` directly
3. TLS handshake must complete before data flows to the Pipe

The `IStreamProvider` abstraction (Phase 2.5-A) solves this by inserting a `Stream` layer between the Socket and the Pipe.

---

## 2. Rejected Alternatives

### 2.1 Akka.Streams.Dsl.Tcp

`Akka.Streams.Dsl.Tcp.OutgoingConnection()` returns `Flow<ByteString, ByteString, ...>`. This was considered and rejected because:

- **ByteString is immutable and copies on concat** — fundamentally incompatible with TurboMqtt's pooled `IMemoryOwner<byte>` / `Span<T>` memory model
- The entire encoder/decoder pipeline is built around `MemoryPool<byte>.Rent()` + `Span` slicing
- Switching to ByteString would force copies at both TCP and decoder layers
- Would break `BatchWeighted` frame packing optimization (which works on `Memory<byte>` slices)
- Estimated **15-30% throughput regression** from double-copying
- Adding `SslStream` as a `BidiFlow` on top adds yet another buffer layer
- Adapter stages between `ByteString` and `Memory<byte>` would negate any simplification

### 2.2 PR #182: Direct IDuplexPipe to Akka.Streams

PR #182 attempted to replace the `Channel` intermediary with `IDuplexPipe` connected directly to Akka.Streams `GraphStage`s. After 50+ commits, it was abandoned because:

- **Pipes expect external completion signaling** — the consumer calls `PipeReader.Complete()` / `PipeWriter.Complete()` to signal lifecycle
- **Akka.Streams expects completion to flow through graph topology** — a `Source` completes, and completion propagates downstream through the graph
- These two lifecycle models are fundamentally incompatible
- `PipeReader.ReadAsync()` inside a `GraphStage.OnPull()` requires `async` in a context that doesn't naturally support it
- Backpressure mapping between Pipe's `PauseWriterThreshold` and Akka.Streams demand signals is non-trivial
- Completion signaling required complex coordination that was fragile and non-deterministic

### 2.3 Why Channels Work

`System.Threading.Channels` has a simple completion model:
- `Writer.TryComplete()` signals no more data
- `Reader.Completion` is a `Task` that completes when the writer is done
- `TryWrite()` / `TryRead()` are non-blocking and return `bool`

This maps cleanly to both Akka.Streams (via `ChannelSource`/`ChannelSink`) and the transport actor (direct `TryWrite`/`TryRead` in background tasks). The Channel stays as the boundary.

---

## 3. Recommended Approach: Hybrid (Option C)

Keep `Channel<(IMemoryOwner<byte>, int)>` as the boundary between transport and Akka.Streams. Fix everything **below** that boundary.

```
Current:  Socket → Pipe → FULL COPY (new byte[]) → Channel → Akka.Streams
Proposed: Stream → Pipe → pooled copy (Rent())   → Channel → Akka.Streams
             ^
             +-- NetworkStream (plain TCP)
             +-- SslStream(NetworkStream) (TLS)
             +-- custom Stream (QUIC future)
```

**Key architectural insight:** TLS belongs at the `Stream` level, not at the Channel or Akka.Streams level. By abstracting `Stream` creation behind `IStreamProvider`, we can support TCP, TLS, and future transports (QUIC) without changing the Channel boundary or Akka.Streams graphs.

---

## 4. IStreamProvider Abstraction

### 4.1 Interface Design

```csharp
/// <summary>
/// Abstracts the creation of a connected Stream for the transport layer.
/// Implementations handle protocol-specific setup (TCP, TLS, QUIC).
/// </summary>
internal interface IStreamProvider : IAsyncDisposable
{
    /// <summary>
    /// Creates and connects the underlying transport, returning a Stream
    /// suitable for reading and writing MQTT frames.
    /// </summary>
    /// <param name="ct">Cancellation token for the connect operation.</param>
    /// <returns>A connected, ready-to-use Stream.</returns>
    Task<Stream> ConnectAsync(CancellationToken ct = default);

    /// <summary>
    /// The underlying Socket (for buffer size configuration, linger, etc.)
    /// May be null for non-socket transports.
    /// </summary>
    Socket? UnderlyingSocket { get; }
}
```

### 4.2 Implementations

**`TcpStreamProvider`** — Plain TCP:
1. Creates `Socket` with configured `AddressFamily`, `NoDelay`, `LingerState`, buffer sizes
2. Resolves DNS, calls `Socket.ConnectAsync()`
3. Returns `new NetworkStream(socket, ownsSocket: true)`

**`TlsStreamProvider`** — TLS over TCP (Phase 2.5-C):
1. Delegates to `TcpStreamProvider` for socket creation and connection
2. Wraps returned `NetworkStream` with `SslStream`
3. Calls `SslStream.AuthenticateAsClientAsync()` with configured certificates and validation
4. Returns the `SslStream`

### 4.3 How It Integrates

`TcpTransportActor` constructor takes an `IStreamProvider` instead of creating a `Socket` directly:

```
TcpTransportActor(IStreamProvider streamProvider, int maxFrameSize)
```

The actor calls `streamProvider.ConnectAsync()` during the `DoConnect` handler, receives a `Stream`, and uses `Stream.ReadAsync()` / `Stream.WriteAsync()` in its background tasks instead of `Socket.ReceiveAsync()` / `Socket.SendAsync()`.

---

## 5. Transport Actor FSM Design

### 5.1 State Machine

Replace the ad-hoc state management (mix of `ConnectionStatus` enum, `Become()` calls, and `CleanUpGracefully()`) with an explicit FSM:

```
NotStarted ──CreateTcpTransport──> Created
Created ──DoConnect──> Connecting
Connecting ──ConnectResult(ok)──> Connected
Connecting ──ConnectResult(fail)──> Stopped
Connected ──DoClose──> Draining
Connected ──ReadFinished──> Closing
Connected ──ConnectionUnexpectedlyClosed──> Aborted
Draining ──OutboundFlushed──> Closing
Draining ──timeout──> Closing
Closing ──BackgroundTasksCompleted──> Stopped
Aborted ──BackgroundTasksCompleted──> Stopped
```

### 5.2 Key State Behaviors

**Connected:**
- Three background tasks running (`DoWriteToPipeAsync`, `ReadFromPipeAsync`, `DoWriteToSocketAsync`)
- All three tracked via `Task.WhenAll` with `ContinueWith` self-tell `BackgroundTasksCompleted`

**Draining:**
- Outbound channel writer completed — no new writes accepted
- Wait for outbound stream to flush pending data
- After flush (or timeout), transition to `Closing`

**Closing:**
- Cancel the `ShutDownCts` to stop background tasks
- Wait for `BackgroundTasksCompleted` self-tell
- Dispose stream provider, complete channels
- Send `PoisonPill` to self (exactly once)

**Aborted:**
- Immediate cancel of `ShutDownCts`
- No drain wait
- Same `BackgroundTasksCompleted` → `Stopped` path

### 5.3 Idempotency

Each state only accepts its valid messages. Duplicate `DoClose`, `ReadFinished`, or `ConnectionUnexpectedlyClosed` messages in states that don't handle them are simply ignored (or logged at debug level). This eliminates races #3 and #4 from the catalogue.

---

## 6. Lifecycle Coordination

### 6.1 Background Task Tracking

Replace fire-and-forget with tracked tasks:

```csharp
private void BecomeConnected()
{
    Become(Connected);

    var readTask = DoWriteToPipeAsync(State.ShutDownCts.Token);
    var pipeTask = ReadFromPipeAsync(State.ShutDownCts.Token);
    var writeTask = DoWriteToSocketAsync(State.ShutDownCts.Token);

    Task.WhenAll(readTask, pipeTask, writeTask).ContinueWith(_ =>
    {
        _closureSelf.Tell(BackgroundTasksCompleted.Instance);
    });
}
```

### 6.2 PostStop Ordering

`ClientStreamOwner.PostStop()` must follow a deterministic sequence:

1. Complete outbound channel writer (stops new data entering stream)
2. Abort transport (cancels background tasks, disposes stream)
3. Complete inbound channel writer (terminates consumer)
4. Signal `_trueDeath` TCS (unblocks `WhenTerminated`)
5. Tell parent `ClientDied`

### 6.3 Reconnect State Machine

Replace fire-and-forget `DoReconnect` with message-driven reconnection in `ClientStreamOwner`:

1. `StreamTerminated` received → enter `Reconnecting` behavior via `Become(Reconnecting)`
2. `Reconnecting` state uses `RunTask` to await transport creation + `ConnectAsync`
3. On success, self-tell `ReconnectSuccess` → `Become(Running)`
4. On failure, self-tell `ReconnectFailed` → decrement retries, either retry or shutdown

This eliminates the nested fire-and-forget async inside `RunTask` (race #8).

---

## 7. TLS Integration Approach

### 7.1 Configuration

```csharp
public sealed record MqttClientTlsOptions
{
    /// <summary>
    /// Enable TLS for the connection.
    /// </summary>
    public bool UseTls { get; init; }

    /// <summary>
    /// Client certificates for mutual TLS authentication.
    /// </summary>
    public X509CertificateCollection? ClientCertificates { get; init; }

    /// <summary>
    /// Custom server certificate validation callback.
    /// When null, uses default system validation.
    /// </summary>
    public RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; init; }

    /// <summary>
    /// The TLS/SSL protocols to use. Defaults to system default (TLS 1.2+).
    /// </summary>
    public SslProtocols EnabledSslProtocols { get; init; } = SslProtocols.None; // system default

    /// <summary>
    /// Target host name for TLS SNI. Defaults to MqttClientTcpOptions.Host.
    /// </summary>
    public string? TargetHost { get; init; }
}
```

### 7.2 Factory Method

```csharp
public interface IMqttClientFactory
{
    Task<IMqttClient> CreateTcpClient(MqttClientConnectOptions options, MqttClientTcpOptions tcpOptions);

    // New: TLS variant
    Task<IMqttClient> CreateTlsTcpClient(
        MqttClientConnectOptions options,
        MqttClientTcpOptions tcpOptions,
        MqttClientTlsOptions tlsOptions);
}
```

### 7.3 Stream Layer

TLS wrapping happens entirely inside `TlsStreamProvider`:

```
Socket → NetworkStream → SslStream.AuthenticateAsClientAsync() → return SslStream
```

No changes needed to `TcpTransportActor`, Channels, or Akka.Streams graphs. The actor reads and writes a `Stream` — it does not know or care whether that Stream is a `NetworkStream` or `SslStream`.

---

## 8. Pooled Memory on Read Path

### 8.1 Current (wasteful)

```csharp
// TcpTransportActor.ReadFromPipeAsync() — current code
var newMemory = new Memory<byte>(new byte[buffer.Length]);
var unshared = new UnsharedMemoryOwner<byte>(newMemory);
buffer.CopyTo(newMemory.Span);
_readsFromTransport.Writer.TryWrite((unshared, newMemory.Length));
```

### 8.2 Proposed (pooled)

```csharp
// TcpTransportActor.ReadFromPipeAsync() — proposed
var owner = MemoryPool<byte>.Shared.Rent((int)buffer.Length);
buffer.CopyTo(owner.Memory.Span);
_readsFromTransport.Writer.TryWrite((owner, (int)buffer.Length));
```

The copy is still necessary (Pipe buffers are reused after `AdvanceTo`), but the allocation is pooled. Downstream consumers already call `IMemoryOwner<byte>.Dispose()` which now returns the buffer to the pool instead of being a no-op.

---

## 9. Implementation Phases

See `IMPLEMENTATION_PLAN.md` Phase 2.5 for detailed task breakdown. Summary:

| Phase | Goal | Dependencies | Risk |
|-------|------|--------------|------|
| 2.5-A | Extract `IStreamProvider`, pool read allocations | None | Medium |
| 2.5-B | Fix 12+ race conditions with FSM + task tracking | None | Low |
| 2.5-C | Add TLS via `TlsStreamProvider` | 2.5-A | Low |
| 2.5-D | Formalize FSM, graceful drain, connect timeout | 2.5-A + 2.5-B | Medium |

```
2.5-A (Stream Abstraction)  <-->  2.5-B (Race Fixes)  [parallel]
         |                              |
    2.5-C (TLS)                    2.5-D (Hardening)  [after both A+B]
```

---

## 10. Success Criteria

- All existing TCP E2E tests pass unchanged after each phase
- Container tests pass against EMQX after each phase
- BenchmarkDotNet before/after confirms no throughput regression (baseline: 193k msg/sec QoS 0)
- New TLS container tests pass against EMQX with TLS enabled
- Zero new compiler warnings
- Race condition catalogue items verified fixed via targeted tests
