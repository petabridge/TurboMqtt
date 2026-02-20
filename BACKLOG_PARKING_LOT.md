# Backlog Parking Lot

> Items parked here need a human decision before they can be worked on.
> RALPH loops do NOT pick up items from this file -- only from `IMPLEMENTATION_PLAN.md`.
>
> Each item includes: what it is, where it came from, and what decision is needed.

---

## Items Awaiting Decision

### v1.0 Release to NuGet
- **Source:** Planning session 2026-02-19, Phase 4 parking lot
- **Issue:** The project is at v0.2.0 (last release June 2024). A v1.0 release signals production readiness and API stability.
- **Decision needed:** Define the quality bar for 1.0. Specifically:
  - Must MQTT 5.0 be included in 1.0, or can 1.0 ship with MQTT 3.1.1 only?
  - What API stability guarantees are being made? (SemVer strict? Extend-only?)
  - Is TLS support required for 1.0?
  - What performance benchmarks must be met?
- **Blocked on:** Phases 1-3 of `IMPLEMENTATION_PLAN.md`
- **Date parked:** 2026-02-19

### API stability review before 1.0
- **Source:** Planning session 2026-02-19, Phase 4 parking lot
- **Issue:** The public API surface (`IMqttClient`, `MqttClientConnectOptions`, `MqttClientFactory`, channel-based consumer APIs) has not been formally reviewed for stability. Before cutting 1.0, the public API should be audited for:
  - Naming consistency
  - Extend-only compatibility (can new features be added without breaking existing consumers?)
  - Correct use of `internal` vs `public` visibility
  - Whether `IAsyncDisposable` patterns are consistent
- **Decision needed:** Who performs the review? What is the acceptance criteria?
- **Blocked on:** MQTT 5.0 implementation (Phase 3) since it adds significant public API surface
- **Date parked:** 2026-02-19

### Address customer question #277
- **Source:** https://github.com/petabridge/TurboMqtt/issues/277
- **Issue:** A user question is open. Needs triage to determine if it is a bug, feature request, or documentation gap.
- **Decision needed:** Read the issue, classify it, and either fix it, add it to the implementation plan, or close it with an answer.
- **Date parked:** 2026-02-19

### MQTT over QUIC (epic #68)
- **Source:** `PROJECT_CONTEXT.md` roadmap item 3
- **Issue:** QUIC transport is on the long-term roadmap. This would require a new transport actor (replacing `TcpTransportActor`) and potentially new stream stage behavior due to QUIC's multiplexed stream model.
- **Decision needed:** When to start (post-1.0?), whether to use `System.Net.Quic` directly or a wrapper library, and whether QUIC changes the backpressure model.
- **Blocked on:** .NET QUIC API stability, MQTT over QUIC standard finalization
- **Date parked:** 2026-02-19

### TLS support (MQTT 3.1.1 and 5.0)
- **Source:** Was Task 2.7 and Task 3.12 in `IMPLEMENTATION_PLAN.md`; parked 2026-02-19
- **Issue:** TLS support exists in-flight on the `tls-support2` branch. The branch has not been reviewed for correctness or compatibility with the current `dev` branch. Task 3.12 (MQTT 5.0 TLS benchmarks) is blocked on this work.
- **Decision needed:**
  - Evaluate `tls-support2` branch: merge as-is, merge with modifications, or rewrite?
  - Is TLS required before a 1.0 release, or is it a post-1.0 feature?
- **Subtasks when unparked:**
  - Review `tls-support2` for correctness against current `dev`
  - Integrate TLS transport: `MqttClientConnectOptions` (certificate, server name, skip-validation for testing)
  - Container test: connect to EMQX over TLS (port 8883), publish/subscribe at QoS 0 and QoS 1
  - Unit tests: TLS option validation
  - `PROJECT_CONTEXT.md` protocol support table: change TLS from "In-flight" to "Implemented"
  - MQTT 5.0 TLS benchmarks (`Mqtt5TlsTcpBenchmarks.cs`): QoS 0/1, payloads 10 and 1024 bytes, TLS overhead quantified
- **Date parked:** 2026-02-19

### AOT compatibility
- **Source:** `PROJECT_CONTEXT.md` key constraints
- **Issue:** AOT compilation support is blocked on Akka.NET v1.6 which has not shipped yet. The project currently uses reflection-heavy Akka.NET patterns that are not AOT-friendly.
- **Decision needed:** Wait for Akka.NET 1.6, or investigate partial AOT compatibility (trimming warnings, source generators for serialization)?
- **Blocked on:** Akka.NET v1.6 release
- **Date parked:** 2026-02-19

### Add test gate to release workflow
- **Source:** RALPH run 20260219-215639, adversarial review of Task 1.1 (commit 4bb4612)
- **Issue:** The `release.yaml` workflow builds, signs, and publishes to NuGet.org but does not include a `dotnet test` step. This matches the old Azure DevOps pipeline. The assumption is PR validation already ran tests. Risk: a tag pushed from an untested commit could publish broken packages.
- **Decision needed:** Accept the current pattern (test in PR only) or add a test step to `release.yaml`? Adding tests costs ~2 minutes per release but prevents publishing broken packages.
- **Date parked:** 2026-02-19

### Double DISCONNECT injection in Draining→Closing path
- **Source:** Adversarial review 20260220-000928, finding F-1
- **Issue:** `TcpTransportActor.BecomeClosing()` unconditionally injects a DISCONNECT packet into the reads channel, but when called from the `Draining` handler after `OutboundFlushed`, a DISCONNECT was already injected at line 546. Two packets enter the reads channel on the graceful drain path.
- **Decision needed:** Guard `BecomeClosing()` to skip injection if prior state was Draining, or remove the injection from the Draining handler and rely on BecomeClosing.
- **Date parked:** 2026-02-20

### Remove dead `CompareAndSetStatus` method
- **Source:** Adversarial review 20260220-000928, finding F-2
- **Issue:** `TcpTransportActor.ConnectionState.CompareAndSetStatus()` was added for thread-safety but is never called. All status transitions use `Volatile.Write` via the `Status` setter.
- **Decision needed:** Remove the method or use it where appropriate (e.g., in `DisposeStreamProvider` status assignment).
- **Date parked:** 2026-02-20

### Propagate `ConnectTimeout` to reconnect CTS
- **Source:** Adversarial review 20260220-000928, finding F-4
- **Issue:** `ClientStreamOwner.BeginReconnect()` hardcodes `TimeSpan.FromSeconds(5)` for the reconnect CTS. The new `MqttClientTcpOptions.ConnectTimeout` property is not propagated to the reconnect path.
- **Decision needed:** Should reconnect timeout match `ConnectTimeout`, be separately configurable, or remain hardcoded?
- **Date parked:** 2026-02-20

### Add isolated actor test for `ClientStreamOwner.Reconnecting` behavior
- **Source:** Adversarial review 20260220-000928, finding B-3
- **Issue:** The `Reconnecting` state in `ClientStreamOwner` is only tested via FakeMqttTcpServer E2E. An isolated TestKit test with TestProbe would verify the message flow (ReconnectSuccess/ReconnectFailed) more reliably and run faster.
- **Date parked:** 2026-02-20

### Clean up pre-existing `#pragma warning disable CS4014` in MqttClient.ConnectAsync
- **Source:** Adversarial review 20260220-000928, finding G-4
- **Issue:** Two instances of fire-and-forget `AbortConnectionAsync()` in `ConnectAsync` error paths use pragma suppression. Should properly track the Task or use a discard with a comment.
- **Date parked:** 2026-02-20

### Annotate `_transport` field in `IMqttClient.cs` with thread-safety comment
- **Source:** Adversarial review 20260220-000928, finding F-5
- **Issue:** The `_transport` field in `MqttClient` (line 150 of `IMqttClient.cs`) is a bare `private IMqttTransport _transport;` with no annotation. All reads must go through the `Transport` property (which uses `Volatile.Read`) to ensure visibility across threads. A missing comment risks future developers reading the field directly, introducing a race condition.
- **Decision needed:** Add a comment on the field declaration (e.g., `// Reads must use the Transport property (Volatile.Read); writes use Interlocked.Exchange in SwapTransport`) or enforce access through a Roslyn analyzer.
- **Date parked:** 2026-02-20

### IMPLEMENTATION_PLAN.md Phase 2.5 checkbox desync
- **Source:** Adversarial review 20260220-000928, finding R-2 (also noted in review-after-iter-03.md)
- **Issue:** `IMPLEMENTATION_PLAN.md` Phase 2.5 section (Tasks 2.5-A through 2.5-D) still shows `- [ ]` for all Done-when items, while `IMPLEMENTATION_PLAN_PHASE2_5.md` shows all `- [x]`. These should be synchronized.
- **Decision needed:** Sync during PR merge to `dev`, or as part of Task 1.7?
- **Date parked:** 2026-02-20
