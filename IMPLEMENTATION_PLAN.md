# Implementation Plan

> This file tracks all active implementation work. RALPH loops consume tasks
> from this file in order. Each task must have objective "Done when" criteria.
>
> **Format:** Tasks use `### Task X.Y:` headers with `Done when:` checklists.
> RALPH picks the FIRST incomplete task (has unchecked `- [ ]` items).

---

## Phase 1: Infrastructure and Modernization

> Goal: Unblock releases by migrating CI/CD to GitHub Actions, upgrade to .NET 10,
> and bring all package dependencies to current versions. This phase has zero
> functional changes to TurboMqtt itself -- it is purely infrastructure.

### Task 1.1: Create GitHub Actions release workflow

**PRD:** https://github.com/petabridge/TurboMqtt/issues/326
**Surface area:** cross-cutting
**Verification:** L1

Replace the Azure DevOps pipeline (`.azure/build_release.yaml`) with a GitHub Actions
workflow that triggers on git tag push, builds, signs with `dotnet sign`, publishes
to NuGet.org, and creates a GitHub Release with release notes and `.nupkg` artifacts.

Done when:
- [x] New workflow file `.github/workflows/release.yaml` exists
- [x] Workflow triggers on `v*` tag push to `dev` or `main`
- [x] Workflow runs `build.ps1` to extract version and release notes
- [x] Workflow runs `dotnet pack -c Release -o ./bin/nuget`
- [x] Workflow uses `dotnet sign` (not SignClient) for NuGet package signing
- [x] Workflow pushes `.nupkg` to NuGet.org using a repository secret `NUGET_API_KEY`
- [x] Workflow creates a GitHub Release with title `TurboMqtt vX.Y.Z`, body from `RELEASE_NOTES.md`, and `.nupkg` attached
- [x] `.azure/build_release.yaml` is deleted
- [x] `.config/dotnet-tools.json` no longer references SignClient
- [x] `TOOLING.md` CI/CD table updated to reflect GitHub Actions release pipeline

### Task 1.2: Fix broken GitHub Release creation

**PRD:** https://github.com/petabridge/TurboMqtt/issues/74
**Surface area:** cross-cutting
**Verification:** L1

The previous Azure DevOps `GitHubRelease@0` task used an incorrect `repositoryName`
format (full URL instead of `owner/repo`). This is resolved by Task 1.1's new
workflow. Verify the fix explicitly.

Done when:
- [x] GitHub Release creation uses `gh release create` or `softprops/action-gh-release` with correct `petabridge/TurboMqtt` repository reference
- [x] A dry-run or manual test confirms the release step does not fail with repository name errors
- [x] Issue #74 can be closed (add comment referencing the PR)

### Task 1.3: Upgrade to .NET 10

**PRD:** .NET 10 modernization (no GitHub issue)
**Surface area:** cross-cutting
**Verification:** L1

Update the SDK, TFMs, and all framework-coupled packages from .NET 8 to .NET 10.

Done when:
- [x] `global.json` SDK version updated to `10.0.100` (or latest stable `10.0.x`), `rollForward` remains `latestMinor`
- [x] `src/TurboMqtt/TurboMqtt.csproj` TFM changed from `net8.0` to `net10.0`
- [x] All test project TFMs changed from `net8.0` to `net10.0`
- [x] `benchmarks/TurboMqtt.Benchmarks/TurboMqtt.Benchmarks.csproj` TFM changed to `net10.0`
- [x] Sample project TFMs changed to `net10.0`
- [x] `System.IO.Pipelines` version updated from `8.0.0` to `10.0.x` in `Directory.Packages.props`
- [x] `Microsoft.SourceLink.GitHub` updated to latest stable in `Directory.Packages.props`
- [x] `pr_validation.yaml` workflow installs .NET 10 SDK via `actions/setup-dotnet`
- [x] `release.yaml` workflow (from Task 1.1) installs .NET 10 SDK
- [x] `dotnet build -c Release` succeeds with zero warnings on .NET 10
- [x] `dotnet test tests/TurboMqtt.Tests/ -c Release` passes
- [x] `PROJECT_CONTEXT.md` "Key Constraints" updated to reflect `net10.0` target

### Task 1.4: Update Akka.NET packages to latest

**PRD:** Package modernization (no GitHub issue)
**Surface area:** cross-cutting
**Verification:** L1

Update Akka.NET and Akka.Hosting to the latest stable versions.

Done when:
- [x] `AkkaVersion` in `Directory.Packages.props` updated to latest stable (currently 1.5.48, check NuGet for latest)
- [x] `AkkaHostingVersion` in `Directory.Packages.props` updated to latest stable (currently 1.5.55, check NuGet for latest)
- [x] `dotnet build -c Release` succeeds with zero warnings
- [x] `dotnet test tests/TurboMqtt.Tests/ -c Release` passes
- [x] No new deprecation warnings from Akka.NET API changes

### Task 1.5: Update OpenTelemetry packages to latest

**PRD:** Package modernization (no GitHub issue)
**Surface area:** cross-cutting
**Verification:** L1

Update all OpenTelemetry packages. Note: the OTEL .NET SDK had breaking changes
between 1.x and 2.x (namespace reorganization, removal of some extension methods).
This may require source changes.

Done when:
- [x] `OtelVersion` in `Directory.Packages.props` updated to latest stable (currently 1.10.0, check NuGet for latest)
- [x] If OTEL 2.x is adopted, any breaking API changes in `src/TurboMqtt/` are resolved
- [x] `dotnet build -c Release` succeeds with zero warnings
- [x] `dotnet test tests/TurboMqtt.Tests/ -c Release` passes
- [x] OpenTelemetry metrics and traces still function (verify sample app compiles)

### Task 1.6: Update test and tooling packages to latest

**PRD:** Package modernization (no GitHub issue)
**Surface area:** cross-cutting
**Verification:** L1

Update remaining packages: xunit, FluentAssertions, Testcontainers, BenchmarkDotNet,
FsCheck, Microsoft.NET.Test.Sdk, coverlet, and other test/tooling dependencies.

Done when:
- [x] All packages in `Directory.Packages.props` `Test Package Versions` ItemGroup updated to latest stable
- [x] `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Hosting` updated to `10.0.x` *(note: already completed in Task 1.5/iter-05 due to OTEL transitive dependency)*
- [x] Revert `NuGetAuditLevel=high` in `Directory.Build.props` (added in Task 1.3 for OTEL vulnerability, resolved by Task 1.5 OTEL 1.15.0 update; confirm `dotnet build -c Release` produces zero audit warnings after removal) *(source: RALPH run 20260219-215639 CLEANUP item)*
- [x] `FsCheck` and `FsCheck.Xunit` updated to latest 2.x stable (or 3.x if compatible) *(FsCheck 3.x requires C# LINQ API migration to FsCheck.Fluent; staying on 2.16.6 which is already latest 2.x)*
- [x] `BenchmarkDotNet` updated to latest stable *(0.15.8 — already at latest as of this iteration)*
- [x] `Testcontainers` updated to latest stable *(4.10.0; also synced Testcontainers.ActiveMq from 3.8.0 → 4.10.0)*
- [x] `dotnet build -c Release` succeeds with zero warnings across all projects
- [x] `dotnet test tests/TurboMqtt.Tests/ -c Release` passes
- [x] `TOOLING.md` package version table updated

### Task 1.7: Update PROJECT_CONTEXT.md and TOOLING.md for Phase 1

**PRD:** Documentation (no GitHub issue)
**Surface area:** cross-cutting
**Verification:** L0

Done when:
- [x] `PROJECT_CONTEXT.md` version updated to reflect 0.3.0-beta (or whatever version is chosen for this release cycle)
- [x] `PROJECT_CONTEXT.md` "Key Constraints" reflects `net10.0` and current Akka version
- [x] `TOOLING.md` reflects all updated tool/package versions
- [x] `TOOLING.md` Build table `.NET SDK` version updated from `8.0.400` to `10.0.100` *(source: RALPH run 20260219-215639 CLEANUP item -- stale after Task 1.3 .NET 10 upgrade)*
- [x] `TOOLING.md` CI/CD section describes GitHub Actions release pipeline (not Azure DevOps)
- [x] `Directory.Build.props` copyright year updated to 2025

---

## Phase 2: MQTT 3.1.1 Production Hardening

> Goal: Raise confidence in the existing MQTT 3.1.1 implementation through
> comprehensive property-based testing, error path coverage, codec review,
> TLS support, and fixing known flaky tests. This is the gate to "production ready"
> for the 3.1.1 protocol (epic #66).

### Task 2.1: Add FsCheck generators for all 14 MQTT 3.1.1 packet types

**PRD:** https://github.com/petabridge/TurboMqtt/issues/66
**Surface area:** domain
**Verification:** L1

Currently `PacketGenerators.cs` only has `ConnectPacketArb()` and `PublishPacketArb()`.
Add generators for the remaining 12 packet types: ConnAck, PubAck, PubRec, PubRel,
PubComp, Subscribe, SubAck, Unsubscribe, UnsubAck, PingReq, PingResp, Disconnect.
Update `PacketArb()` to include all 14 generators.

Done when:
- [x] `PacketGenerators.cs` has an `Arbitrary<MqttPacket>` generator for each of the 14 MQTT 3.1.1 packet types
- [x] Each generator produces valid packets with randomized field values within spec constraints
- [x] `PacketArb()` uses `Gen.OneOf(...)` over all 14 generators
- [x] All generators compile and produce non-null packets when sampled (add a smoke test if needed)

### Task 2.2: Expand ConnectPacket generator to cover Will, Username, Password

**PRD:** https://github.com/petabridge/TurboMqtt/issues/66
**Surface area:** domain
**Verification:** L1

The current `ConnectPacketArb()` only generates `ClientId`, `CleanSession`, and
`KeepAliveSeconds`. CONNECT packets also carry optional Will (topic, message, QoS,
retain), Username, and Password fields that are controlled by `ConnectFlags`.

Done when:
- [x] `ConnectPacketArb()` randomly generates packets with and without Will messages
- [x] Will topic, Will message payload, Will QoS (0/1/2), and Will retain are randomized when Will is present
- [x] Username and Password fields are randomly included or omitted
- [x] `ConnectFlags` bits are consistent with the fields present (e.g., `HasWill=true` when Will topic is set)
- [x] Existing roundtrip codec tests still pass

### Task 2.3: Add roundtrip encode/decode property tests for all packet types

**PRD:** https://github.com/petabridge/TurboMqtt/issues/66
**Surface area:** domain
**Verification:** L1

Use the generators from Task 2.1 to create property-based roundtrip tests:
encode a packet with `Mqtt311Encoder`, decode it with `Mqtt311Decoder`, and assert
structural equality. Currently only `TestPacketReassembly` exists as a property test.

Done when:
- [x] A property test class exists that tests roundtrip encode/decode for each of the 14 packet types individually
- [x] Each property test asserts that decoded packet fields match the original generated packet
- [x] A combined property test encodes a random packet from `PacketArb()`, decodes it, and asserts equality
- [x] All property tests pass with default FsCheck iteration count (100)
- [x] `TestPacketReassembly` property test updated to use the full `PacketArb()` (all 14 types) *(added `Arbitrary = new[] { typeof(PacketGenerators) }` attribute + per-type Classify labels; also fixed PUBLISH decoder bug: minBytes was 2, must be 1 per MQTT §4.7.3)*

### Task 2.4: Add error path and boundary condition tests

**PRD:** https://github.com/petabridge/TurboMqtt/issues/66
**Surface area:** domain
**Verification:** L1

Test defensive behavior of the encoder and decoder against invalid or adversarial input.

Done when:
- [x] Test: decoder rejects packets with invalid packet type byte (0x00, 0xFF)
- [x] Test: decoder handles truncated packets (fewer bytes than remaining length indicates)
- [x] Test: decoder handles packets where remaining length exceeds maximum (256 MB MQTT limit)
- [x] Test: decoder handles remaining length encoded with more than 4 bytes
- [x] Test: encoder/decoder roundtrip with maximum-size payload (close to 256 MB or a practical test limit)
- [x] Test: decoder handles PUBLISH with QoS 3 (invalid, reserved value)
- [x] Test: decoder handles CONNECT with invalid protocol name or version byte
- [x] Test: partial frame delivery across multiple buffers (extend `TestPacketReassembly` to all types)
- [x] All tests pass on both Linux and Windows

### Task 2.5: Code review MQTT 3.1.1 encoder/decoder and file issues

**PRD:** https://github.com/petabridge/TurboMqtt/issues/66
**Surface area:** domain
**Verification:** L0

Perform a line-by-line review of `Mqtt311Encoder.cs`, `Mqtt311EncoderOptimized.cs`,
`Mqtt311Decoder.cs`, and `MqttPacketSizeEstimator.cs` (MQTT 3.1.1 paths only).
Compare against the OASIS MQTT 3.1.1 specification.

Done when:
- [x] Review covers: fixed header encoding, remaining length encoding/decoding, all packet type encode/decode paths, size estimation accuracy
- [x] Any specification violations filed as GitHub issues with label `bug` and referenced section of MQTT 3.1.1 spec *(#344 §3.1.2.1, #345 §3.1.2.6, #346 §2.2.2)*
- [x] Any potential buffer overflows, off-by-one errors, or unsafe memory patterns filed as GitHub issues *(#347 bit mask off-by-one, #350 missing buffer guard)*
- [x] Any discrepancies between `Mqtt311Encoder` and `Mqtt311EncoderOptimized` filed as issues *(#350 missing buffer size validation in optimized encoder)*
- [x] Summary of findings documented in the PR description or a comment on issue #66 *(https://github.com/petabridge/TurboMqtt/issues/66#issuecomment-3937110252)*

### Task 2.6: Fix flaky ShouldConnectAndDisconnect test

**PRD:** https://github.com/petabridge/TurboMqtt/issues/99
**Surface area:** cross-cutting
**Verification:** L2

The container test `ShouldConnectAndDisconnect` is flaky. Diagnose the root cause
(likely timing/race condition in actor lifecycle or TCP connection teardown) and fix it.

Done when:
- [x] Root cause identified and documented in issue #99 comment *(https://github.com/petabridge/TurboMqtt/issues/99#issuecomment-3937124391)*
- [x] Fix applied (may involve timeout adjustments, actor lifecycle ordering, or test harness changes) *(PrepareDisconnect message + _userDisconnectRequested guard in ClientStreamOwner; commit 7fd069f, PR #343)*
- [x] `dotnet test tests/TurboMqtt.Container.Tests/ -c Release` passes the test 10 consecutive times locally *(verified: 10/10 runs pass, 2 tests per run - TCP and TLS variants)*
- [x] No `[Skip]` attribute or equivalent workaround -- the test runs normally
- [x] Issue #99 can be closed *(closed 2026-02-20)*

### Task 2.7: TLS support

**Replaced by Phase 2.5-C** (Transport Layer Redesign). The `tls-support2` branch approach
is superseded by the `IStreamProvider` + `TlsStreamProvider` design in Phase 2.5. See
[docs/prd/transport-redesign/README.md](docs/prd/transport-redesign/README.md) for rationale.

Done when:
- [x] Superseded by Phase 2.5-C — no action needed in Phase 2


### Task 2.8: Add MQTT 3.1.1 E2E tests with authentication enabled

**PRD:** https://github.com/petabridge/TurboMqtt/issues/66
**Surface area:** cross-cutting
**Verification:** L2

The current EMQX container tests use anonymous connections. Add tests that exercise
MQTT username/password authentication against the broker.

Done when:
- [x] EMQX fixture configured with at least one username/password credential
- [x] Container test: successful connect with valid username/password
- [x] Container test: connect rejected with invalid username/password (expect ConnAck with appropriate return code)
- [x] Container test: publish and subscribe work over authenticated connection at QoS 0 and QoS 1
- [x] All new tests pass with `dotnet test tests/TurboMqtt.Container.Tests/ -c Release`

---

## Phase 2.5: Transport Layer Redesign

> Goal: Fix 12+ race conditions in the transport/lifecycle layer, eliminate GC pressure
> from unpooled read allocations, introduce a `Stream` abstraction to enable TLS, and
> formalize the transport actor state machine. This must happen before MQTT 5.0 because
> the transport layer needs to be solid before adding protocol complexity.
>
> **PRD:** [docs/prd/transport-redesign/README.md](docs/prd/transport-redesign/README.md)
>
> **Prerequisites:** Phase 2 tasks 2.1-2.6 should be complete (codec hardening provides
> the test infrastructure to verify transport changes don't regress behavior).

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

## Review Fixes

### FIX: Correct EmqxAuthFixture XML docstring

**Source:** Adversarial review 20260220-202420 iter-05, finding F-2
**Surface area:** documentation
**Verification:** L0

The XML summary comment on `EmqxAuthFixture` has two factual errors from the debugging journey:
1. Says `EMQX_MANAGEMENT__API_KEY__BOOTSTRAP_FILE` — should be `EMQX_API_KEY__BOOTSTRAP_FILE`
2. Says file format `{AppID}:{ApiKey}:{ApiSecret}` — should be `{ApiKey}:{ApiSecret}:{Role}`

Done when:
- [x] XML summary at `EmqxAuthFixture.cs` lines 22-28 corrected to match the actual env var name (`EMQX_API_KEY__BOOTSTRAP_FILE`) and file format (`{ApiKey}:{ApiSecret}:{Role}`)
- [x] Builds with zero warnings

---

## Phase 3: MQTT 5.0 Implementation

> Goal: Implement a functional MQTT 5.0 encoder and decoder, integrate them into
> the client pipeline, and validate against a real MQTT 5.0 broker (EMQX).
> This phase corresponds to epic #67.
>
> **PRD:** [docs/prd/mqtt5/README.md](docs/prd/mqtt5/README.md) — detailed spec-to-code mapping
>
> **Prerequisites:** Phase 2 tasks 2.1-2.5 should be complete so the property-based
> testing infrastructure can be reused for MQTT 5.0 codec validation. Phase 2.5
> (Transport Layer Redesign) should be complete so MQTT 5.0 builds on a solid transport.

### Task 3.0: Build MQTT 5.0 property encoding/decoding infrastructure

**PRD:** [docs/prd/mqtt5/README.md](docs/prd/mqtt5/README.md) §1
**Surface area:** domain
**Verification:** L1

Create shared property writer/reader helpers that the encoder and decoder will use.
MQTT 5.0 properties are typed key-value pairs (28 identifiers across 7 data types).
The size estimator (`MqttPacketSizeEstimator.EstimateMqtt5PacketSize()`) already
handles all property types — the writer/reader must be consistent with it.

Done when:
- [x] `Mqtt5PropertyIdentifiers.cs` exists with constants for all 28 property identifiers *(27 per OASIS spec Table 2-4; PRD count off by one due to 0x09 listed in both UTF-8 String and Binary Data rows)*
- [x] `Mqtt5PropertyWriter.cs` exists with static methods: `WriteByte`, `WriteTwoByteInt`, `WriteFourByteInt`, `WriteVariableByteInt`, `WriteUtf8String`, `WriteStringPair`, `WriteBinaryData` — all using `ref Span<byte>`
- [x] `Mqtt5PropertyReader.cs` exists with matching static read methods using `ref ReadOnlySpan<byte>`
- [x] Unit test: each property type roundtrips (write then read)
- [x] Unit test: Variable Byte Integer boundary values (0, 127, 128, 16383, 16384, 2097151, 2097152, 268435455)
- [x] Unit test: UTF-8 String handles empty, ASCII, and multi-byte characters
- [x] Unit test: unknown property identifier in reader returns error (not crash)
- [x] FsCheck property: random property values roundtrip through write/read
- [x] Builds with zero warnings

### Task 3.1: Add missing MQTT 5.0 fields to ConnAckPacket

**PRD:** [docs/prd/mqtt5/README.md](docs/prd/mqtt5/README.md) §3
**Surface area:** domain
**Verification:** L1

`ConnAckPacket.cs` is missing 13 MQTT 5.0 properties that the broker sends.
These must be added before the decoder can populate them.

Done when:
- [x] `ConnAckPacket.cs` has: `SessionExpiryInterval`, `AssignedClientIdentifier`, `ServerKeepAlive`, `AuthenticationMethod`, `AuthenticationData`, `ResponseInformation`, `ServerReference`, `TopicAliasMaximum`, `MaximumQoS`, `RetainAvailable`, `WildcardSubscriptionAvailable`, `SubscriptionIdentifiersAvailable`, `SharedSubscriptionAvailable`
- [x] `ConnAckReasonCode` enum has all MQTT 5.0 reason codes (OASIS Table 3-1) *(already complete — 22 reason codes present)*
- [x] `SubscribePacket` has `SubscriptionIdentifier` (uint?) property added *(changed from NonZeroUInt16 to uint? to match VBI spec range and optionality)*
- [x] `PubAckPacket` has `UserProperties` field added (currently missing)
- [x] `PubAckPacket.ReasonString` changed from computed to stored property
- [x] `MqttPacketSizeEstimator.EstimateConnAckPacketSizeMqtt5()` updated to account for new properties
- [x] Builds with zero warnings

### Task 3.2: Implement Mqtt5Encoder

**PRD:** [docs/prd/mqtt5/README.md](docs/prd/mqtt5/README.md) §2-10, §12.1
**Surface area:** domain
**Verification:** L1

Create `Mqtt5Encoder.cs` in `src/TurboMqtt/Protocol/` that encodes all 15 MQTT 5.0
packet types using the property writer from Task 3.0. Follow the same static-method,
`ref Span<byte>` pattern as `Mqtt311Encoder`. Key difference: after Variable Header
fields, write Property Length (VBI) + property key-value pairs before the Payload.

Done when:
- [x] `Mqtt5Encoder.cs` exists with `EncodePacket` matching `Mqtt311Encoder.EncodePacket` signature
- [x] All 15 packet types handled (Connect, ConnAck, Publish, PubAck, PubRec, PubRel, PubComp, Subscribe, SubAck, Unsubscribe, UnsubAck, PingReq, PingResp, Disconnect, Auth)
- [x] CONNECT encoding includes: Protocol Level 5, Connect Properties, Will Properties
- [x] PUBLISH encoding includes all V5 properties (Topic Alias, Message Expiry, User Properties, etc.)
- [x] ACK packets use compact form when Reason Code is Success and no properties
- [x] SUBSCRIBE encoding includes V5 Subscription Options byte (No Local, Retain As Published, Retain Handling)
- [x] Auth packet encoding handles `AuthenticationMethod`, `AuthenticationData`, `ReasonString`, `UserProperties`
- [x] Builds with zero warnings
- [x] Unit tests verify encoding of each packet type against hand-computed expected bytes

### Task 3.3: Implement Mqtt5Decoder

**PRD:** [docs/prd/mqtt5/README.md](docs/prd/mqtt5/README.md) §2-10, §12.2
**Surface area:** domain
**Verification:** L1

Create `Mqtt5Decoder.cs` in `src/TurboMqtt/Protocol/` that decodes all 15 MQTT 5.0
packet types using the property reader from Task 3.0. Follow `Mqtt311Decoder` pattern:
stateful class with `_remainder` for partial frame handling.

Done when:
- [ ] `Mqtt5Decoder.cs` exists with `TryDecode` matching `Mqtt311Decoder` patterns
- [ ] All 15 packet types decoded
- [ ] CONNACK decoding populates all 13+ V5 properties (from Task 3.1)
- [ ] PUBLISH decoding populates V5 properties (Topic Alias, User Properties, etc.)
- [ ] ACK packet decoding handles compact form (no Reason Code byte) and full form
- [ ] Auth packet decoding populates all fields
- [ ] Server-initiated DISCONNECT decoding handles all V5 reason codes
- [ ] Builds with zero warnings
- [ ] Unit tests decode known byte sequences into correct packet fields

### Task 3.4: Add FsCheck generators and roundtrip property tests for MQTT 5.0

**PRD:** [docs/prd/mqtt5/README.md](docs/prd/mqtt5/README.md) §2-10
**Surface area:** domain
**Verification:** L1

Extend the property-based testing infrastructure to cover MQTT 5.0 packets. This
reuses the pattern established in Phase 2 tasks 2.1-2.3 but with MQTT 5.0 specific
fields (reason codes, user properties, etc.).

Done when:
- [ ] FsCheck generators exist for all 15 MQTT 5.0 packet types (including Auth)
- [ ] Generators randomize MQTT 5.0 specific fields: reason codes, user properties, session expiry, receive maximum, etc.
- [ ] Roundtrip property test: encode with `Mqtt5Encoder`, decode with `Mqtt5Decoder`, assert structural equality
- [ ] All property tests pass with default FsCheck iteration count (100)
- [ ] Error path tests: malformed property lengths, unknown property identifiers, oversized packets

### Task 3.5: Wire Mqtt5Encoder/Decoder into Akka.Streams pipeline

**PRD:** [docs/prd/mqtt5/README.md](docs/prd/mqtt5/README.md) §12.3
**Surface area:** cross-cutting
**Verification:** L1

Integrate the new encoder/decoder into the existing Akka.Streams encode/decode/receive
flows so that `MqttProtocolVersion.V5_0` uses `Mqtt5Encoder` and `Mqtt5Decoder` instead
of throwing `NotSupportedException`.

Files to change:
- `MqttEncodingFlows.cs` — add `Mqtt5Encoding()` method
- `MqttDecodingFlows.cs` — add `Mqtt5Decoding()` method
- `MqttClientStreams.cs` — add `Mqtt5OutboundPacketSink()` and `Mqtt5InboundMessageSource()`
- `ClientStreamInstance.cs` — add `case MqttProtocolVersion.V5_0:` in `ConfigureMqttStreams()` (line 157)
- `IMqttClientFactory.cs` — remove `AssertMqtt311()` guard

Done when:
- [ ] `MqttClientFactory.CreateTcpClient()` succeeds with `MqttProtocolVersion.V5_0`
- [ ] Stream stages select encoder/decoder based on protocol version
- [ ] `ClientStreamInstance.ConfigureMqttStreams()` has working V5.0 case
- [ ] `Mqtt311Encoder`/`Mqtt311Decoder` remain unchanged and are still used for `V3_1_1`
- [ ] Builds with zero warnings
- [ ] Existing MQTT 3.1.1 tests still pass (no regression)

### Task 3.6: Enforce broker-advertised limits from CONNACK

**PRD:** [docs/prd/mqtt5/README.md](docs/prd/mqtt5/README.md) §3, §11
**Surface area:** cross-cutting
**Verification:** L1

When the client receives CONNACK with V5 properties, store the broker's limits and
enforce them. See PRD §11 for Receive Maximum flow control details.

Done when:
- [ ] Broker's `ReceiveMaximum` limits in-flight QoS 1/2 publishes (throttle `AtLeastOncePublishRetryActor` and `ExactlyOncePublishRetryActor`)
- [ ] Broker's `MaximumPacketSize` validated before sending outbound packets
- [ ] Broker's `MaximumQoS` prevents publishing at higher QoS than supported
- [ ] Broker's `RetainAvailable` prevents setting retain flag if unsupported
- [ ] Broker's `ServerKeepAlive` overrides client-requested keep alive in `HeartBeatActor`
- [ ] Broker's `AssignedClientIdentifier` overwrites client ID when provided
- [ ] Unit tests: retry actor queues publishes beyond Receive Maximum, resumes on ACK
- [ ] Integration test: with Receive Maximum = 2, 5 publishes are sent 2 at a time

### Task 3.7: Implement MQTT 5.0 Auth packet flow

**PRD:** [docs/prd/mqtt5/README.md](docs/prd/mqtt5/README.md) §10
**Surface area:** cross-cutting
**Verification:** L1

Implement enhanced authentication (challenge-response) and background re-authentication.

Done when:
- [ ] `IMqtt5AuthHandler` interface created (see PRD §10 for proposed API)
- [ ] `MqttClientConnectOptions.AuthHandler` property added
- [ ] `Mqtt5AuthHandler` state machine manages: AwaitingConnAck → InChallenge → Authenticated
- [ ] Client sends AUTH as part of CONNECT flow when AuthHandler is set
- [ ] Client handles incoming AUTH (Reason Code 0x18) with challenge-response
- [ ] Re-authentication triggered when broker sends AUTH with Reason Code 0x19
- [ ] Auth failure triggers connection teardown
- [ ] Unit tests: state machine transitions through all happy and failure paths
- [ ] Builds with zero warnings

### Task 3.8: Handle server-initiated DISCONNECT and update public API

**PRD:** [docs/prd/mqtt5/README.md](docs/prd/mqtt5/README.md) §9, §13
**Surface area:** cross-cutting
**Verification:** L1

In MQTT 5.0, the server can send DISCONNECT to the client (new behavior vs 3.1.1).
Also update the public API surface for V5 features.

Done when:
- [ ] Inbound DISCONNECT from server triggers graceful cleanup (actor hierarchy shutdown)
- [ ] Server DISCONNECT Reason Code and Reason String logged and emitted as OpenTelemetry event
- [ ] `MqttClientConnectOptions` has all V5 connection properties (uncomment TODOs + add new)
- [ ] `MqttMessage` (channel consumer type) exposes: UserProperties, ContentType, ResponseTopic, CorrelationData, SubscriptionIdentifiers, PayloadFormatIndicator, MessageExpiryInterval
- [ ] DisconnectReasonCode enum has all MQTT 5.0 server-sent reason codes
- [ ] Builds with zero warnings

### Task 3.9: Add MQTT 5.0 E2E container tests

**PRD:** [docs/prd/mqtt5/README.md](docs/prd/mqtt5/README.md) §14
**Surface area:** cross-cutting
**Verification:** L2

Create container tests that exercise the full MQTT 5.0 pipeline against a real
EMQX broker.

Done when:
- [ ] Container test: MQTT 5.0 connect and disconnect
- [ ] Container test: MQTT 5.0 publish and subscribe at QoS 0
- [ ] Container test: MQTT 5.0 publish and subscribe at QoS 1
- [ ] Container test: MQTT 5.0 publish and subscribe at QoS 2
- [ ] Container test: MQTT 5.0 connection with User Properties on CONNECT
- [ ] Container test: MQTT 5.0 publish with User Properties, verify received on subscriber
- [ ] Container test: Server-initiated disconnect handled correctly
- [ ] All tests pass with `dotnet test tests/TurboMqtt.Container.Tests/ -c Release`

### Task 3.10: Add MQTT 5.0 E2E container tests with authentication

**PRD:** [docs/prd/mqtt5/README.md](docs/prd/mqtt5/README.md) §10, §14
**Surface area:** cross-cutting
**Verification:** L2

Test MQTT 5.0 authentication against EMQX.

Done when:
- [ ] Container test: MQTT 5.0 connect with username/password authentication
- [ ] Container test: MQTT 5.0 connect rejected with invalid credentials
- [ ] Container test: MQTT 5.0 publish and subscribe work over authenticated connection
- [ ] All tests pass with `dotnet test tests/TurboMqtt.Container.Tests/ -c Release`

### Task 3.11: Add MQTT 5.0 TCP benchmarks

**PRD:** [docs/prd/mqtt5/README.md](docs/prd/mqtt5/README.md) §14
**Surface area:** cross-cutting
**Verification:** L3

Create BenchmarkDotNet benchmarks for MQTT 5.0 TCP throughput, comparable to the
existing `Mqtt311End2EndTcpBenchmarks`.

Done when:
- [ ] `Mqtt5End2EndTcpBenchmarks.cs` exists in `benchmarks/TurboMqtt.Benchmarks/Mqtt5/`
- [ ] Benchmarks cover QoS 0, QoS 1, and QoS 2 at multiple payload sizes (10, 1024, 32768 bytes)
- [ ] Benchmarks produce `Req/sec` metric comparable to MQTT 3.1.1 results
- [ ] Codec microbenchmarks exist: `Mqtt5ConnectCodecBenchmarks.cs`, `Mqtt5PublishCodecBenchmarks.cs`
- [ ] Benchmark results documented in PR description
- [ ] No throughput regression on MQTT 3.1.1 benchmarks (run both and compare)

### Task 3.12: Add MQTT 5.0 TLS benchmarks

**PRD:** [docs/prd/mqtt5/README.md](docs/prd/mqtt5/README.md) §14
**Surface area:** cross-cutting
**Verification:** L3

Add TCP+TLS benchmarks for MQTT 5.0, building on the TLS support from Phase 2.5
(Task 2.5-C) and MQTT 5.0 benchmarks from Task 3.11.

Done when:
- [ ] `Mqtt5TlsTcpBenchmarks.cs` exists in `benchmarks/TurboMqtt.Benchmarks/Mqtt5/`
- [ ] Benchmarks cover QoS 0 and QoS 1 over TLS at payload sizes 10 and 1024 bytes
- [ ] TLS overhead quantified relative to plain TCP benchmarks from Task 3.11
- [ ] Benchmark results documented in PR description


---

## Dependency Graph

```
Phase 1 (all tasks independent of each other, but ordered for clean progression):
  1.1 --> 1.2 (release workflow must exist before verifying release creation)
  1.3 (can run in parallel with 1.1)
  1.4, 1.5, 1.6 (depend on 1.3 for TFM compatibility)
  1.7 (depends on all of 1.1-1.6)

Phase 2 (depends on Phase 1 completing):
  2.1 --> 2.2 (generator expansion depends on base generators)
  2.1 --> 2.3 (property tests depend on generators)
  2.4 (independent, can run in parallel with 2.1-2.3)
  2.5 (independent, can run in parallel)
  2.6 (independent, can run in parallel)
  2.7 (superseded by Phase 2.5-C)
  2.8 (independent, can run in parallel)

Phase 2.5 (depends on Phase 2 tasks 2.1-2.6 completing):
  2.5-A (Stream abstraction) <--> 2.5-B (race fixes)  [can run in parallel]
  2.5-A --> 2.5-C (TLS depends on IStreamProvider)
  2.5-A + 2.5-B --> 2.5-D (hardening depends on both)

Phase 3 (depends on Phase 2 tasks 2.1-2.5 for testing infrastructure + Phase 2.5):
  3.0 (property infrastructure, first task)
  3.1 (packet field additions, can parallel with 3.0)
  3.0 + 3.1 --> 3.2 (encoder uses property writer + needs complete packet types)
  3.0 + 3.1 --> 3.3 (decoder uses property reader + needs complete packet types)
  3.2 + 3.3 --> 3.4 (property tests need both encoder and decoder)
  3.2 + 3.3 --> 3.5 (pipeline wiring needs both)
  3.5 --> 3.6 (broker limit enforcement needs pipeline)
  3.5 --> 3.7 (auth flow needs pipeline)
  3.5 --> 3.8 (server disconnect + public API needs pipeline)
  3.5 --> 3.9 (E2E tests need pipeline)
  3.7 --> 3.10 (auth E2E needs auth flow)
  3.9 --> 3.11 (benchmarks need working E2E)
  3.11 + 2.5-C --> 3.12 (TLS benchmarks need both TLS and MQTT 5.0 benchmarks)
```
