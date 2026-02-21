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

### Propagate `ConnectTimeout` to reconnect CTS
- **Source:** Adversarial review 20260220-000928, finding F-4
- **Issue:** `ClientStreamOwner.BeginReconnect()` hardcodes `TimeSpan.FromSeconds(5)` for the reconnect CTS. The new `MqttClientTcpOptions.ConnectTimeout` property is not propagated to the reconnect path.
- **Decision needed:** Should reconnect timeout match `ConnectTimeout`, be separately configurable, or remain hardcoded?
- **Date parked:** 2026-02-20

### Add isolated actor test for `ClientStreamOwner.Reconnecting` behavior
- **Source:** Adversarial review 20260220-000928, finding B-3
- **Issue:** The `Reconnecting` state in `ClientStreamOwner` is only tested via FakeMqttTcpServer E2E. An isolated TestKit test with TestProbe would verify the message flow (ReconnectSuccess/ReconnectFailed) more reliably and run faster.
- **Date parked:** 2026-02-20

### Establish `await using` convention for IMqttClient in tests
- **Source:** Adversarial review 20260220-202420, finding B-1
- **Issue:** `ShouldRejectConnectionWithInvalidPassword` (and other tests) create `IMqttClient` instances without calling `DisposeAsync()`. `IMqttClient : IAsyncDisposable`. Currently relies on TestKit actor system shutdown for cleanup. Consistent pattern across repo but not ideal.
- **Decision needed:** Should all test methods use `await using` for clients? Would require updating existing tests too.
- **Date parked:** 2026-02-20

### Add "no credentials" negative auth test
- **Source:** Adversarial review 20260220-202420, finding B-2
- **Issue:** Task 2.8 tests wrong-password rejection but not no-credentials-at-all against the auth-enabled broker. A `ShouldRejectConnectionWithNoCredentials` test would validate the `EMQX_MQTT__ALLOW_ANONYMOUS=false` enforcement.
- **Date parked:** 2026-02-20

### Dedicated regression test for 1-char MQTT topic name
- **Source:** Adversarial review 20260220-202420, finding F-3
- **Issue:** The decoder bug fix (minBytes 2→1 for PUBLISH topic name, commit 21120a6) is covered probabilistically by FsCheck property tests but lacks a self-documenting deterministic test like `Decoder_Publish_SingleCharTopic_DecodesSuccessfully`.
- **Date parked:** 2026-02-20

### Task 2.4 Windows validation
- **Source:** Adversarial review 20260220-202420, finding F-1
- **Issue:** "All tests pass on both Linux and Windows" criterion was checked but only Linux was verified in the RALPH run. PR CI will validate Windows when the PR is created.
- **Date parked:** 2026-02-20

### MqttLastWill.DelayInterval should be uint, not NonZeroUInt16
- **Source:** Adversarial review 20260220-202420 iter-10, finding F-5
- **Issue:** MQTT 5.0 spec §3.1.3.2.2 defines Will Delay Interval as a Four Byte Integer (uint32, 0–4294967295). `MqttLastWill.DelayInterval` is `NonZeroUInt16` (ushort, 0–65535). The `Mqtt5Decoder` at line 567 does `(ushort)ReadFourByteInt()` which silently truncates. Also, `NonZeroUInt16` semantically implies non-zero but the spec allows 0 (publish immediately).
- **Decision needed:** Change `MqttLastWill.DelayInterval` from `NonZeroUInt16` to `uint?` (or `uint`). This is a breaking change to the data model.
- **Date parked:** 2026-02-20

### UserProperties should support duplicate keys per MQTT 5.0 spec
- **Source:** Adversarial review 20260220-202420 iter-10, finding F-6
- **Issue:** All packet types use `IReadOnlyDictionary<string, string>?` for User Properties. MQTT 5.0 §3.1.2.11.8 says "The same name is allowed to appear more than once." `Dictionary<string, string>` silently drops duplicate keys.
- **Decision needed:** Change to `IReadOnlyList<KeyValuePair<string, string>>?` or similar across all packet types. This is a breaking change.
- **Date parked:** 2026-02-20

### DisconnectPacket missing ReasonString property
- **Source:** Adversarial review 20260220-202420 iter-10, finding F-7
- **Issue:** Per MQTT 5.0 §3.14.2.2.2, DISCONNECT can include Reason String (0x1F). The `DisconnectPacket` class does not have a `ReasonString` property, so encoder/decoder cannot support it.
- **Decision needed:** Add `string? ReasonString` to `DisconnectPacket` and update encoder/decoder to handle it.
- **Date parked:** 2026-02-20

### File GitHub issue for RetainHandling bit-mask bug fix
- **Source:** Adversarial review 20260220-202420 iter-10, finding F-9
- **Issue:** Commit `8f94444` fixed `ToSubscriptionOptions` decoding RetainHandling from wrong bits (3-4 instead of 4-5). Bug fix is correct but no GitHub issue filed. Phase 2 code review filed issues #344-350 for similar findings.
- **Date parked:** 2026-02-20

### Pre-existing flaky HeartbeatFailure test
- **Source:** Adversarial review 20260220-202420 iter-10, finding F-8
- **Issue:** `TcpMqtt311HeartbeatFailureEnd2EndSpecs.ShouldAutomaticallyReconnectandSubscribeAfterHeartbeatFailure` fails with `SocketException: Address already in use`. Confirmed pre-existing — fails identically on base commit. Port binding issue on test machine.
- **Decision needed:** Fix `FakeMqttTcpServer` to use ephemeral ports, or mark test with known-flaky annotation, or fix the port conflict.
- **Date parked:** 2026-02-20

### Empty ClientId MQTT 5.0 CONNECT test
- **Source:** Adversarial review 20260220-202420 iter-10, finding B-3
- **Issue:** `Mqtt5Decoder.DecodeConnect` allows empty client IDs (overriding base class throw). No dedicated test exercises this. Will be covered by Task 3.4 FsCheck generators.
- **Date parked:** 2026-02-20

### RALPH flight recorder log drift enforcement
- **Source:** Diagnostics review 20260220-202420, ISSUE-1 and ISSUE-2
- **Issue:** Iterations 05, 09, and 10 in run 20260220-202420 dropped required RALPH flight recorder sections (surface area, verification level, skills consulted, deviations, follow-ups, done-when table). iter-05 covers the highest-stakes E2E work yet has the weakest structural record. Pattern correlates with end-of-session fatigue.
- **Decision needed:** Create a `ralph-flight-recorder` skill or template that enforces all required sections. Alternatively, add a pre-commit structural validation step to the RALPH loop.
- **Date parked:** 2026-02-20

### Extract `mqtt5-codec-patterns` skill
- **Source:** Diagnostics review 20260220-202420, ISSUE-7
- **Issue:** Three consecutive iterations (3.0, 3.2, 3.3) implemented the same MQTT 5.0 property read/write loop pattern (VBI property length prefix, per-identifier switch dispatch, compact ACK forms) without proposing a skill. This violates the CLAUDE.md rule "If a workflow is repeated 3+ times, extract it into a repo skill or script."
- **Decision needed:** Create the skill before the next MQTT 5.0 codec-related task (Task 3.4 or later), or defer if the pattern is considered sufficiently documented in the existing code.
- **Date parked:** 2026-02-20

