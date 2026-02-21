# Implementation Plan

> This file tracks all active implementation work. RALPH loops consume tasks
> from this file in order. Each task must have objective "Done when" criteria.
>
> **Format:** Tasks use `### Task X.Y:` headers with `Done when:` checklists.
> RALPH picks the FIRST incomplete task (has unchecked `- [ ]` items).

---

## Open GitHub Issues — Filed During RALPH Run 20260220-202420

Issues filed by Task 2.5 (MQTT 3.1.1 code review). These are tracked in GitHub
and do not need to be resolved before merging this branch.

| Issue | Title | Area |
|-------|-------|------|
| [#344](https://github.com/petabridge/TurboMqtt/issues/344) | `ConnectFlags.Decode` — reserved bit 0 not validated [MQTT-3.1.2-3] | Protocol compliance |
| [#345](https://github.com/petabridge/TurboMqtt/issues/345) | `ConnectFlags.Decode` — WillQoS not validated ≤ 2 [MQTT-3.1.2-14] | Protocol compliance |
| [#346](https://github.com/petabridge/TurboMqtt/issues/346) | Decoder — fixed header reserved bits not validated for SUBSCRIBE/UNSUBSCRIBE/PUBREL [MQTT-2.2.2] | Protocol compliance |
| [#347](https://github.com/petabridge/TurboMqtt/issues/347) | Bit mask off-by-one in subscription options decoding | Bug |
| [#348](https://github.com/petabridge/TurboMqtt/issues/348) | `ConnectPacket` — duplicate `Flags` and `ConnectFlags` properties | Code quality |
| [#349](https://github.com/petabridge/TurboMqtt/issues/349) | MQTT 5.0 size estimators — `=` instead of `+=` for `ComputeUserPropertiesSize` | Bug |
| [#350](https://github.com/petabridge/TurboMqtt/issues/350) | `Mqtt311EncoderOptimized` — no input buffer size validation | Safety |

## Open GitHub Issues — Filed During RALPH Run 20260221-020516

Issues filed during after-action review. Organized into Phases 4–6 below.

| Issue | Title | Phase |
|-------|-------|-------|
| [#356](https://github.com/petabridge/TurboMqtt/issues/356) | Double DISCONNECT injection in Draining→Closing path | 4 |
| [#357](https://github.com/petabridge/TurboMqtt/issues/357) | Propagate ConnectTimeout to reconnect CTS | 4 |
| [#362](https://github.com/petabridge/TurboMqtt/issues/362) | MqttLastWill.DelayInterval should be uint, not NonZeroUInt16 | 4 |
| [#363](https://github.com/petabridge/TurboMqtt/issues/363) | UserProperties should support duplicate keys per MQTT 5.0 spec | 4 |
| [#365](https://github.com/petabridge/TurboMqtt/issues/365) | Pre-existing flaky HeartbeatFailure test — port binding conflict | 4 |
| [#367](https://github.com/petabridge/TurboMqtt/issues/367) | ReceiveMaximum quota should be shared across QoS 1 and QoS 2 actors | 4 |
| [#368](https://github.com/petabridge/TurboMqtt/issues/368) | Investigate MqttPacketSizeEstimator underestimation edge cases | 4 |
| [#372](https://github.com/petabridge/TurboMqtt/issues/372) | ExactlyOncePublishRetryActor missing DequeueBuffered() on PubRec failure path | 4 |
| [#358](https://github.com/petabridge/TurboMqtt/issues/358) | Add isolated actor test for ClientStreamOwner.Reconnecting behavior | 5 |
| [#359](https://github.com/petabridge/TurboMqtt/issues/359) | Establish await using convention for IMqttClient in tests | 5 |
| [#360](https://github.com/petabridge/TurboMqtt/issues/360) | Add no-credentials negative auth test | 5 |
| [#361](https://github.com/petabridge/TurboMqtt/issues/361) | Add dedicated regression test for 1-char MQTT topic name | 5 |
| [#364](https://github.com/petabridge/TurboMqtt/issues/364) | File GitHub issue for RetainHandling bit-mask bug fix (tracking) | 5 |
| [#366](https://github.com/petabridge/TurboMqtt/issues/366) | Add empty ClientId test for MQTT 5.0 CONNECT | 5 |
| [#369](https://github.com/petabridge/TurboMqtt/issues/369) | Add unit tests for MqttClient.PublishAsync broker limit validation | 5 |
| [#370](https://github.com/petabridge/TurboMqtt/issues/370) | Add deterministic encoder test for ReceiveMaximum > 0 | 5 |
| [#353](https://github.com/petabridge/TurboMqtt/issues/353) | Define v1.0 release criteria and quality bar | 6 |
| [#354](https://github.com/petabridge/TurboMqtt/issues/354) | API stability review before 1.0 release | 6 |
| [#355](https://github.com/petabridge/TurboMqtt/issues/355) | Add test gate to release.yaml workflow | 6 |
| [#371](https://github.com/petabridge/TurboMqtt/issues/371) | Run full production benchmarks for MQTT 5.0 before PR merge | 6 |

---

## Phase 4: Bug Fixes & Spec Compliance

> Correctness fixes that must land before any 1.0 release. Ordered so that
> non-breaking fixes come first, followed by breaking data model changes, then
> the larger architectural fix (shared ReceiveMaximum quota).

### Task 4.1: Fix missing DequeueBuffered() on PubRec failure path

**PRD:** https://github.com/petabridge/TurboMqtt/issues/372
**Surface area:** domain
**Verification:** L2

In `ExactlyOncePublishRetryActor`, when a PubRec arrives with a non-success
reason code, the pending packet is removed and the sender is notified, but
`DequeueBuffered()` is not called. This permanently leaks a ReceiveMaximum
quota slot. The QoS 1 actor handles this correctly on all paths.

Key file: `src/TurboMqtt/Protocol/Pub/ExactlyOncePublishRetryActor.cs` lines 100-108.

Done when:
- [x] `DequeueBuffered()` is called after `_pendingPackets.Remove` in the PubRec failure handler (line 103 area)
- [x] Akka.Hosting.TestKit test exercises: send N+1 publishes where N = ReceiveMaximum, have the broker reply with a failing PubRec for one, verify the buffered publish is promoted and eventually completes
- [x] Existing `ExactlyOncePublishRetryActor` tests still pass
- [x] Builds with zero warnings
- [x] All existing tests pass

### Task 4.2: Fix double DISCONNECT injection in Draining-to-Closing transition

**PRD:** https://github.com/petabridge/TurboMqtt/issues/356
**Surface area:** cross-cutting
**Verification:** L2

`TcpTransportActor.BecomeClosing()` unconditionally injects a DISCONNECT
packet into the reads channel (line 569). When called from the `Draining`
handler after `OutboundFlushed`, a DISCONNECT was already injected at line 537.
Two DISCONNECT packets enter the reads channel on the graceful drain path.

Key file: `src/TurboMqtt/IO/Tcp/TcpTransportActor.cs`.

Done when:
- [x] Only one DISCONNECT packet is injected into `_readsFromTransport` on the Draining -> Closing path (guard added to `BecomeClosing` or injection removed from `Draining.OutboundFlushed` handler)
- [x] The Connected -> Closing path (no draining) still injects exactly one DISCONNECT
- [x] The Aborted path still injects exactly one DISCONNECT
- [x] Akka.Hosting.TestKit test verifies the graceful drain path produces exactly one DISCONNECT in the reads channel
- [x] Builds with zero warnings
- [x] All existing tests pass

### Task 4.3: Propagate ConnectTimeout to reconnect CTS

**PRD:** https://github.com/petabridge/TurboMqtt/issues/357
**Surface area:** cross-cutting
**Verification:** L1

`ClientStreamOwner.BeginReconnect()` hardcodes `TimeSpan.FromSeconds(5)` for
the reconnect CTS (line 555). This ignores the user-configured
`MqttClientTcpOptions.ConnectTimeout`. The reconnect timeout should use the
configured value or a separate configurable property.

Key file: `src/TurboMqtt/Client/ClientStreamOwner.cs`.

Done when:
- [x] `BeginReconnect()` uses a configurable timeout instead of the hardcoded 5 seconds
- [x] The timeout is sourced from `MqttClientConnectOptions` (new property or existing timeout)
- [x] Unit test verifies the reconnect CTS uses the configured timeout value
- [x] Builds with zero warnings
- [x] All existing tests pass

### Task 4.4: Fix flaky HeartbeatFailure test port binding conflict

**PRD:** https://github.com/petabridge/TurboMqtt/issues/365
**Surface area:** cross-cutting
**Verification:** L2

`TcpMqtt311HeartbeatFailureEnd2EndSpecs` hardcodes port 21887. When multiple
test runs execute in parallel or the port is held by a previous run, the test
fails with `SocketException: Address already in use`.

Key file: `tests/TurboMqtt.Tests/End2End/TcpMqtt311HeartbeatFailureEnd2EndSpecs.cs`.

Done when:
- [x] `FakeMqttTcpServer` uses an ephemeral port (bind to port 0, read back assigned port)
- [x] `TcpMqtt311HeartbeatFailureEnd2EndSpecs` uses the dynamically assigned port
- [x] Test passes reliably on at least 10 consecutive local runs
- [x] Builds with zero warnings
- [x] All existing tests pass

### Task 4.5: Investigate and fix MqttPacketSizeEstimator underestimation edge cases

**PRD:** https://github.com/petabridge/TurboMqtt/issues/368
**Surface area:** domain
**Verification:** L1

FsCheck roundtrip tests intermittently fail with "Destination is too short" in
`Mqtt5Encoder`, indicating the size estimator returns values smaller than the
actual encoded size for certain property combinations. The `=` vs `+=` bug was
fixed, but additional edge cases remain.

Key file: `src/TurboMqtt/Protocol/MqttPacketSizeEstimator.cs`.

Done when:
- [x] FsCheck tests run at 1000+ iterations with no "Destination is too short" failures for all MQTT 5.0 packet types
- [x] Any newly discovered estimator bugs are fixed with deterministic regression tests
- [x] Fixed-seed regression tests added for each discovered edge case
- [x] Builds with zero warnings
- [x] All existing tests pass

### Task 4.6: Change MqttLastWill.DelayInterval from NonZeroUInt16 to uint

**PRD:** https://github.com/petabridge/TurboMqtt/issues/362
**Surface area:** domain
**Verification:** L1

**BREAKING CHANGE.** MQTT 5.0 spec section 3.1.3.2.2 defines Will Delay Interval as a
Four Byte Integer (uint32, range 0-4294967295). `MqttLastWill.DelayInterval`
is currently `NonZeroUInt16` (ushort, 0-65535). The decoder silently truncates
via `(ushort)ReadFourByteInt()`. The `NonZeroUInt16` type also semantically
implies non-zero, but the spec allows 0 (publish immediately).

Key files:
- `src/TurboMqtt/PacketTypes/ConnectPacket.cs` (`MqttLastWill.DelayInterval`)
- `src/TurboMqtt/Client/MqttClientConnectOptions.cs` (`LastWillAndTestament.DelayInterval`)
- `src/TurboMqtt/Protocol/Mqtt5Decoder.cs` (truncating cast)
- `src/TurboMqtt/Protocol/MqttPacketSizeEstimator.cs` (`.Value` access)

Done when:
- [x] `MqttLastWill.DelayInterval` type changed from `NonZeroUInt16` to `uint`
- [x] `LastWillAndTestament.DelayInterval` type changed from `NonZeroUInt16` to `uint`
- [x] Decoder reads full four-byte integer without truncation
- [x] Encoder writes full four-byte integer
- [x] Size estimator correctly accounts for the 4-byte field
- [x] Value of 0 is accepted (no `NonZeroUInt16` constraint)
- [x] FsCheck property test covers roundtrip with values > 65535
- [x] Builds with zero warnings
- [x] All existing tests pass

### Task 4.7: Change UserProperties from IReadOnlyDictionary to support duplicate keys

**PRD:** https://github.com/petabridge/TurboMqtt/issues/363
**Surface area:** cross-cutting
**Verification:** L1

**BREAKING CHANGE.** MQTT 5.0 spec section 3.1.2.11.8 states "The same name is
allowed to appear more than once" for User Properties. All packet types
currently use `IReadOnlyDictionary<string, string>?` which silently drops
duplicate keys.

Affected types (all in `src/TurboMqtt/PacketTypes/`): `ConnectPacket`,
`ConnAckPacket`, `PublishPacket`, `PubAckPacket`, `PubRecPacket`,
`PubRelPacket`, `PubCompPacket`, `SubscribePacket`, `SubAckPacket`,
`UnsubscribePacket`, `UnsubAckPacket`, `DisconnectPacket`, `AuthPacket`,
`MqttLastWill`, and `MqttClientConnectOptions`.

Also affects encoder, decoder, and size estimator code that iterates these collections.

Done when:
- [x] All `UserProperties` and `WillProperties` changed from `IReadOnlyDictionary<string, string>?` to `IReadOnlyList<KeyValuePair<string, string>>?` (or equivalent)
- [x] Encoder iterates the list-based type
- [x] Decoder populates the list-based type
- [x] `ComputeUserPropertiesSize` in `MqttPacketSizeEstimator` iterates the list-based type
- [x] FsCheck generators updated to produce duplicate keys
- [x] Roundtrip test verifies duplicate keys are preserved
- [x] Builds with zero warnings
- [x] All existing tests pass

### Task 4.8: Implement shared ReceiveMaximum quota across QoS 1 and QoS 2 actors

**PRD:** https://github.com/petabridge/TurboMqtt/issues/367
**Surface area:** cross-cutting
**Verification:** L2

MQTT 5.0 section 4.9 requires a shared Receive Maximum quota across both QoS
levels. Current implementation sends `SetReceiveMaximum` to each actor
independently, so total in-flight can reach 2x ReceiveMaximum when both QoS
levels are active.

Key files:
- `src/TurboMqtt/Protocol/Pub/AtLeastOncePublishRetryActor.cs`
- `src/TurboMqtt/Protocol/Pub/ExactlyOncePublishRetryActor.cs`
- `src/TurboMqtt/Client/IMqttClient.cs` (`MqttClient.ApplyBrokerLimits`)

Done when:
- [x] A shared quota mechanism limits total in-flight QoS 1 + QoS 2 publishes to ReceiveMaximum
- [x] When one QoS level frees a slot, the other can use it
- [x] Integration test publishes interleaved QoS 1 and QoS 2 messages against a broker with ReceiveMaximum=5, verifying total in-flight never exceeds 5
- [x] Existing QoS 1 and QoS 2 tests still pass
- [x] Builds with zero warnings
- [x] All existing tests pass

---

## Phase 5: Test Coverage Hardening

> Fill test gaps identified during adversarial code reviews. No production
> code changes except the `await using` convention fix. Ordered from most
> impactful (isolated actor test, broker limit tests) to lighter items.

### Task 5.1: Add isolated actor test for ClientStreamOwner.Reconnecting behavior

**PRD:** https://github.com/petabridge/TurboMqtt/issues/358
**Surface area:** cross-cutting
**Verification:** L2

The Reconnecting state in `ClientStreamOwner` is only tested via E2E tests
with `FakeMqttTcpServer`. An isolated Akka.Hosting.TestKit test with
TestProbes would verify the message flow (ReconnectSuccess / ReconnectFailed /
DoDisconnect during reconnect) more reliably and faster.

Key file: `src/TurboMqtt/Client/ClientStreamOwner.cs`, `Reconnecting` method.

Done when:
- [x] TestKit test covers: ReconnectSuccess -> returns to Running
- [x] TestKit test covers: ReconnectFailed with remaining attempts -> retries
- [x] TestKit test covers: ReconnectFailed with no remaining attempts -> PoisonPill
- [x] TestKit test covers: DoDisconnect while reconnecting -> immediate shutdown
- [x] Builds with zero warnings
- [x] All existing tests pass

### Task 5.2: Add unit tests for MqttClient.PublishAsync broker limit validation

**PRD:** https://github.com/petabridge/TurboMqtt/issues/369
**Surface area:** cross-cutting
**Verification:** L2

`MqttClient.PublishAsync` validates broker-advertised limits (MaximumPacketSize,
MaximumQoS, RetainAvailable) but has no dedicated unit tests. These code paths
are only partially covered by E2E tests.

Key file: `src/TurboMqtt/Client/IMqttClient.cs`, `MqttClient.PublishAsync` lines 437-451.

Done when:
- [x] Test: publish with `RetainRequested=true` when `_brokerRetainAvailable=false` returns failure
- [x] Test: publish with QoS 2 when `_brokerMaximumQoS=QoS1` returns failure
- [x] Test: publish with payload exceeding `_brokerMaximumPacketSize` returns failure
- [x] Test: publish within all limits succeeds
- [x] Builds with zero warnings
- [x] All existing tests pass

### Task 5.2.1: Stabilize flaky DoDisconnect_WhileReconnecting_ShutsDownImmediately test

**Source:** Adversarial review after iter-10, finding F-6 (run 20260221-053113)
**Surface area:** cross-cutting
**Verification:** L2

`ClientStreamOwnerReconnectingSpecs.DoDisconnect_WhileReconnecting_ShutsDownImmediately` fails
intermittently when run as part of the full parallel test suite but passes consistently
in isolation. The `AwaitAssertAsync` polling at 20ms intervals (Phase 3) races with test
parallelism scheduling.

Done when:
- [x] Test passes reliably in 10 consecutive full-suite runs (`dotnet test tests/TurboMqtt.Tests/ -c Release`)
- [x] Fix uses a deterministic signal (e.g., EventFilter on transport FSM log) rather than polling, OR increases timeout with documented justification
- [x] Builds with zero warnings
- [x] All existing tests pass

### Task 5.3: Add deterministic encoder test for ReceiveMaximum > 0

**PRD:** https://github.com/petabridge/TurboMqtt/issues/370
**Surface area:** domain
**Verification:** L1

All existing `Mqtt5EncoderSpecs` CONNECT tests use default ReceiveMaximum=0.
No deterministic test verifies the 20-byte properties block with
ReceiveMaximum included.

Done when:
- [x] Deterministic encode-decode test creates a ConnectPacket with ReceiveMaximum > 0 and verifies roundtrip
- [x] The encoded properties block includes the ReceiveMaximum property identifier and 2-byte value
- [x] Builds with zero warnings
- [x] All existing tests pass

### Task 5.4: Add no-credentials negative auth test

**PRD:** https://github.com/petabridge/TurboMqtt/issues/360
**Surface area:** cross-cutting
**Verification:** L2

The existing test suite validates wrong-password rejection but not
no-credentials-at-all against an auth-enabled EMQX broker
(`EMQX_MQTT__ALLOW_ANONYMOUS=false`).

Done when:
- [x] Container test `ShouldRejectConnectionWithNoCredentials` connects to EMQX without username/password and asserts connect failure
- [x] Test runs in `TurboMqtt.Container.Tests` project
- [x] Builds with zero warnings
- [x] All existing tests pass

### Task 5.5: Add dedicated regression test for 1-char MQTT topic name

**PRD:** https://github.com/petabridge/TurboMqtt/issues/361
**Surface area:** domain
**Verification:** L1

The decoder bug fix (minBytes 2 to 1 for PUBLISH topic name) is covered
probabilistically by FsCheck but lacks a self-documenting deterministic test.

Done when:
- [x] Deterministic test `Decoder_Publish_SingleCharTopic_DecodesSuccessfully` encodes and decodes a PUBLISH with a 1-character topic
- [x] Test covers both MQTT 3.1.1 and MQTT 5.0 decoders
- [x] Builds with zero warnings
- [x] All existing tests pass

### Task 5.6: Add empty ClientId test for MQTT 5.0 CONNECT

**PRD:** https://github.com/petabridge/TurboMqtt/issues/366
**Surface area:** domain
**Verification:** L1

`Mqtt5Decoder.DecodeConnect` allows empty client IDs (overriding base class
throw), but no dedicated test exercises this path.

Done when:
- [x] Deterministic test encodes a CONNECT packet with empty ClientId and verifies successful decode
- [x] Test verifies the decoded ClientId is empty string
- [x] Builds with zero warnings
- [x] All existing tests pass

### Task 5.7: Establish await using convention for IMqttClient in tests

**PRD:** https://github.com/petabridge/TurboMqtt/issues/359
**Surface area:** cross-cutting
**Verification:** L1

Test methods create `IMqttClient` instances without `await using`. Since
`IMqttClient : IAsyncDisposable`, this leaves cleanup to actor system
shutdown, which is nondeterministic.

Done when:
- [x] All test methods that create `IMqttClient` use `await using` pattern
- [x] No test method stores a client in a field without a corresponding dispose in teardown
- [x] Builds with zero warnings
- [x] All existing tests pass

### Task 5.8: Close RetainHandling bit-mask tracking issue

**PRD:** https://github.com/petabridge/TurboMqtt/issues/364
**Surface area:** domain
**Verification:** L0

This is a tracking-only issue. The RetainHandling bit-mask bug was already
fixed in commit 8f94444. This task verifies the fix is tested and closes the
issue.

Done when:
- [x] Verify that existing tests cover RetainHandling decode with all three values (0, 1, 2)
- [x] If not covered, add a deterministic test
- [x] Close issue #364 on GitHub

---

## Phase 6: Release Preparation

> CI hardening, benchmarking, API review, and release criteria. These tasks
> gate the v1.0 release. Tasks 6.1-6.2 are independent; 6.3-6.4 require
> human decisions documented in the issues.

### Task 6.1: Add test gate to release.yaml workflow

**PRD:** https://github.com/petabridge/TurboMqtt/issues/355
**Surface area:** cross-cutting
**Verification:** L0

The release workflow (`release.yaml`) builds, signs, and publishes to NuGet.org
but does not run `dotnet test`. A tag pushed from an untested commit could
publish broken packages.

Key file: `.github/workflows/release.yaml`.

Done when:
- [x] `dotnet test` step added to `release.yaml` after the build step and before the pack step
- [x] Test step runs `dotnet test -c Release tests/TurboMqtt.Tests/` (unit tests only, no containers)
- [x] Workflow fails and does not publish if tests fail
- [x] Builds with zero warnings

### Task 6.2: Run full production benchmarks for MQTT 5.0

**PRD:** https://github.com/petabridge/TurboMqtt/issues/371
**Surface area:** cross-cutting
**Verification:** L3

Dry-run benchmarks showed ~320k req/s QoS0/10B TCP and ~250k req/s QoS0/10B TLS.
Full production runs (launchCount=10, warmupCount=10) should be executed and results
documented before the 1.0 release.

Done when:
- [x] Full BenchmarkDotNet run completed for MQTT 5.0 TCP (QoS 0/1/2, 10B and 1KB payloads)
- [x] Full BenchmarkDotNet run completed for MQTT 5.0 TLS (QoS 0/1/2, 10B and 1KB payloads)
- [x] Results documented in `docs/performance/mqtt5-benchmarks.md`
- [x] No throughput regressions vs MQTT 3.1.1 pipeline
- [x] Builds with zero warnings

### Task 6.3: Define v1.0 release criteria and quality bar

**PRD:** https://github.com/petabridge/TurboMqtt/issues/353
**Surface area:** cross-cutting
**Verification:** L0

**Requires human decision.** This task captures the release criteria discussion.
Questions that must be answered:
- Must MQTT 5.0 be included in 1.0, or can 1.0 ship MQTT 3.1.1 only?
- What API stability guarantees (SemVer strict? Extend-only?)?
- What performance benchmarks must pass?

Done when:
- [x] Release criteria documented in `docs/release/v1.0-criteria.md`
- [x] Criteria covers: protocol scope, API stability promise, perf bar, test pass rate
- [x] Issue #353 closed

### Task 6.5: Remove duplicate ConnectPacket.ConnectFlags property

**PRD:** https://github.com/petabridge/TurboMqtt/issues/348
**Surface area:** domain
**Verification:** L1

**BREAKING CHANGE.** `ConnectPacket` has two properties of the same type: `Flags`
(used by encoders/decoders) and `ConnectFlags` (used by client code). Tests already
exclude `ConnectFlags` from equality comparisons with a comment marking it as a
duplicate. Remove `ConnectFlags` and update all call sites to use `Flags`.

Key files:
- `src/TurboMqtt/PacketTypes/ConnectPacket.cs`
- `src/TurboMqtt/Client/IMqttClient.cs` (sets `ConnectFlags`)

Done when:
- [x] `ConnectPacket.ConnectFlags` property removed
- [x] All references updated to use `Flags`
- [x] The `.Excluding(x => x.ConnectFlags)` exclusion in roundtrip tests removed
- [x] Roundtrip property tests pass with the unified `Flags` property
- [x] Builds with zero warnings
- [x] All existing tests pass

### Task 6.6: Add ConnectFlags reserved bit and WillQoS validation

**PRD:** https://github.com/petabridge/TurboMqtt/issues/344 and https://github.com/petabridge/TurboMqtt/issues/345
**Surface area:** domain
**Verification:** L1

`ConnectFlags.Decode` does not validate: (a) that bit 0 (reserved) must be 0
[MQTT-3.1.2-3]; (b) that WillQoS ≤ 2 when WillFlag is set [MQTT-3.1.2-14].
Malformed packets are silently accepted instead of being rejected.

Key file: `src/TurboMqtt/PacketTypes/ConnectPacket.cs`, `ConnectFlags.Decode` method (line ~141).

Done when:
- [x] Decode throws `ArgumentOutOfRangeException` when bit 0 is 1, with message referencing MQTT-3.1.2-3
- [x] Decode throws `ArgumentOutOfRangeException` when WillFlag=true and WillQoS > 2, with message referencing MQTT-3.1.2-14
- [x] Deterministic unit tests cover both rejection cases
- [x] Existing decode tests still pass
- [x] Builds with zero warnings
- [x] All existing tests pass

### Task 6.7: Validate fixed header reserved bits for SUBSCRIBE/UNSUBSCRIBE/PUBREL

**PRD:** https://github.com/petabridge/TurboMqtt/issues/346
**Surface area:** domain
**Verification:** L1

The decoder extracts only the upper nibble (packet type) from the first fixed
header byte and ignores the lower nibble entirely. Per MQTT-2.2.2, SUBSCRIBE,
UNSUBSCRIBE, and PUBREL must have lower nibble = 0x2. Packets with wrong reserved
bits are silently accepted.

Key file: `src/TurboMqtt/Protocol/Mqtt311Decoder.cs` (line ~60 where `packetType` is extracted).
Also check `Mqtt5Decoder.cs` if it has its own first-byte parsing.

Done when:
- [x] Decoder validates lower nibble = 0x2 for SUBSCRIBE (expected first byte 0x82)
- [x] Decoder validates lower nibble = 0x2 for UNSUBSCRIBE (expected first byte 0xA2)
- [x] Decoder validates lower nibble = 0x2 for PUBREL (expected first byte 0x62)
- [x] Invalid reserved bits cause a decode error (not silent acceptance)
- [x] Deterministic tests verify rejection for each of the three packet types
- [x] Builds with zero warnings
- [x] All existing tests pass

### Task 6.8: Add buffer size validation to Mqtt311EncoderOptimized

**PRD:** https://github.com/petabridge/TurboMqtt/issues/350
**Surface area:** domain
**Verification:** L1

`Mqtt311EncoderOptimized.EncodePacket` has no buffer size guard before writing
packet data, risking `IndexOutOfRangeException` on undersized buffers.
`Mqtt311Encoder.EncodePacket` has the guard (`if (buffer.Length < estimatedSize.TotalSize) throw`).
The optimized encoder should have the same protection.

Key file: `src/TurboMqtt/Protocol/Mqtt311EncoderOptimized.cs` (line ~40, `EncodePacket` method).

Done when:
- [x] `EncodePacket` validates `buffer.Length >= estimatedSize.TotalSize` before writing
- [x] Throws `ArgumentException` with a descriptive message on undersized buffer
- [x] Test verifies the exception is thrown when an undersized buffer is passed
- [x] Builds with zero warnings
- [x] All existing tests pass

### Task 6.4: API stability review before 1.0 release

**PRD:** https://github.com/petabridge/TurboMqtt/issues/354
**Surface area:** cross-cutting
**Verification:** L0

**Requires human decision.** Formal review of the public API surface:
`IMqttClient`, `MqttClientConnectOptions`, `MqttClientFactory`,
channel-based consumer APIs, and all MQTT 5.0 additions.

Done when:
- [x] Public API surface enumerated with `dotnet public-api` or equivalent
- [x] API reviewed for: naming consistency, extend-only compatibility, correct use of `internal` vs `public`, `IAsyncDisposable` consistency
- [x] Breaking changes from Tasks 4.6 and 4.7 reviewed for downstream impact
- [x] API review findings documented in `docs/release/api-review.md`
- [x] Issue #354 closed

### Task 6.9: Make internal-only types internal (API review follow-ups F-3, F-5, F-6)

**Source:** `docs/release/api-review.md` findings F-3, F-5, F-6 (Run 20260221-151320, iter-03)
**Surface area:** domain
**Verification:** L1

Three public types were identified as incorrectly public during the API review:
- `MqttClient` (F-3) — concrete implementation; callers use `IMqttClient`
- `PublishingProtocol.SetReceiveMaximum` (F-5) — internal actor message
- `UShortCounter` (F-6) — internal utility

All three are covered by `InternalsVisibleTo("TurboMqtt.Tests")` so tests still compile.

Key files:
- `src/TurboMqtt/Client/IMqttClient.cs` (`MqttClient`)
- `src/TurboMqtt/Protocol/Pub/PublishingProtocol.cs` (`SetReceiveMaximum`)
- `src/TurboMqtt/Utility/UShortCounter.cs` (`UShortCounter`)

Done when:
- [x] `MqttClient` changed from `public sealed class` to `internal sealed class`
- [x] `PublishingProtocol.SetReceiveMaximum` changed from `public sealed class` to `internal sealed class`
- [x] `UShortCounter` changed from `public sealed class` to `internal sealed class`
- [x] Builds with zero warnings
- [x] All existing tests pass

### Task 6.10: Rename TurbotMqttHostingExtensions to TurboMqttHostingExtensions (F-1)

**Source:** `docs/release/api-review.md` finding F-1 (Run 20260221-151320, iter-03)
**Surface area:** cross-cutting
**Verification:** L1

`TurbotMqttHostingExtensions` has a typo (`Turbot` instead of `Turbo`) in both the class name
and the filename. Must be fixed before v1.0 as this is the last opportunity before SemVer locks it.

Key files:
- `src/TurboMqtt/TurbotMqttHostingExtensions.cs`

Done when:
- [ ] Class renamed from `TurbotMqttHostingExtensions` to `TurboMqttHostingExtensions`
- [ ] File renamed from `TurbotMqttHostingExtensions.cs` to `TurboMqttHostingExtensions.cs`
- [ ] All references to the old class name updated
- [ ] Builds with zero warnings
- [ ] All existing tests pass

---

## Dependency Graph

```
Phase 4 (Bug Fixes & Spec Compliance)
├── Task 4.1 (PubRec DequeueBuffered)       → no dependencies, can start immediately
├── Task 4.2 (Double DISCONNECT)             → no dependencies, can start immediately
├── Task 4.3 (Reconnect timeout)             → no dependencies, can start immediately
├── Task 4.4 (Flaky heartbeat test)          → no dependencies, can start immediately
├── Task 4.5 (Size estimator edge cases)     → no dependencies, can start immediately
├── Task 4.6 (DelayInterval type change)     → no dependencies, can start immediately
├── Task 4.7 (UserProperties type change)    → no dependencies, can start immediately
│   NOTE: Task 4.5 should land BEFORE 4.7 (estimator must handle new type)
└── Task 4.8 (Shared ReceiveMaximum)         → depends on Task 4.1 (same code area)

Phase 5 (Test Coverage Hardening) → starts after Phase 4
├── Task 5.1 (Reconnecting actor test)       → depends on Phase 4 complete
├── Task 5.2 (PublishAsync limit tests)      → depends on Phase 4 complete
├── Task 5.3 (ReceiveMaximum encoder test)   → depends on Phase 4 complete
├── Task 5.4 (No-credentials auth test)      → depends on Phase 4 complete
├── Task 5.5 (Single-char topic test)        → depends on Phase 4 complete
├── Task 5.6 (Empty ClientId test)           → depends on Phase 4 complete
├── Task 5.7 (await using convention)        → depends on Phase 4 complete
└── Task 5.8 (RetainHandling tracking)       → depends on Phase 4 complete

Phase 6 (Release Preparation) → starts after Phase 5
├── Task 6.1 (Release test gate)             → no dependencies within phase
├── Task 6.2 (Production benchmarks)         → depends on all code changes (Phase 4+5)
├── Task 6.3 (Release criteria)              → requires human decision
├── Task 6.5 (Remove duplicate ConnectFlags) → no dependencies, BREAKING CHANGE
├── Task 6.6 (ConnectFlags validation)       → no dependencies
├── Task 6.7 (Fixed header reserved bits)    → no dependencies
├── Task 6.8 (Encoder buffer validation)     → no dependencies
└── Task 6.4 (API review)                    → depends on Tasks 4.6, 4.7, 6.5 (breaking changes)
```
