# TurboMqtt v1.0 API Stability Review

> **Status:** Complete
> **Owner:** Petabridge
> **Related issue:** [#354](https://github.com/petabridge/TurboMqtt/issues/354)
> **Last updated:** 2026-02-21

This document records the pre-v1.0 public API surface review for TurboMqtt. It covers naming
consistency, `internal` vs. `public` scoping, `IAsyncDisposable` consistency, and the breaking
changes introduced in Tasks 4.6 and 4.7.

---

## 1. Public API Surface

All public types in `src/TurboMqtt/` are enumerated below, grouped by namespace.

### 1.1 `TurboMqtt` (root namespace)

| Type | Kind | Notes |
|------|------|-------|
| `TurbotMqttHostingExtensions` | `static class` | ⚠️ See naming note below |
| `MqttMessage` | `sealed record` | User-facing message DTO |
| `QualityOfService` | `enum` | QoS 0/1/2 |
| `MqttPacketType` | `enum` | Internal packet type codes |
| `NonZeroUInt16` | `struct` | Validates packet IDs ≥ 1 |

### 1.2 `TurboMqtt.Client`

| Type | Kind | Notes |
|------|------|-------|
| `IMqttClient` | `interface` | Primary user-facing contract |
| `MqttClient` | `sealed class` | Concrete impl; ⚠️ See scoping note |
| `IMqttClientFactory` | `interface` | Factory contract |
| `MqttClientFactory` | `sealed class` | Concrete impl; ⚠️ See scoping note |
| `MqttClientConnectOptions` | `sealed record` | Connection configuration |
| `LastWillAndTestament` | `sealed record` | User-facing LWT config |
| `MqttClientTcpOptions` | `sealed record` | TCP transport options |
| `MqttClientTlsOptions` | `sealed record` | TLS options |
| `IMqtt5AuthHandler` | `interface` | MQTT 5.0 enhanced auth |

### 1.3 `TurboMqtt.PacketTypes`

| Type | Kind | Notes |
|------|------|-------|
| `MqttPacket` | `abstract class` | Base for all packets |
| `MqttPacketWithId` | `abstract class` | Base for packets with packet IDs |
| `ConnectPacket` | `sealed class` | CONNECT wire type |
| `ConnectFlags` | `struct` | CONNECT flags byte |
| `MqttLastWill` | `sealed class` | LWT in wire format; ⚠️ see naming note |
| `ConnAckPacket` | `sealed class` | CONNACK |
| `PublishPacket` | `sealed class` | PUBLISH |
| `PubAckPacket` | `sealed class` | PUBACK |
| `PubRecPacket` | `sealed class` | PUBREC |
| `PubRelPacket` | `sealed class` | PUBREL |
| `PubCompPacket` | `sealed class` | PUBCOMP |
| `SubscribePacket` | `sealed class` | SUBSCRIBE |
| `TopicSubscription` | `sealed class` | Single topic + options |
| `SubscriptionOptions` | `struct` | Topic subscription options |
| `SubAckPacket` | `sealed class` | SUBACK |
| `UnsubscribePacket` | `sealed class` | UNSUBSCRIBE |
| `UnsubAckPacket` | `sealed class` | UNSUBACK |
| `DisconnectPacket` | `sealed class` | DISCONNECT |
| `AuthPacket` | `sealed class` | AUTH (MQTT 5.0 only) |
| `PingReqPacket` | `sealed class` | PINGREQ (singleton) |
| `PingRespPacket` | `sealed class` | PINGRESP (singleton) |
| `ConnAckReasonCode` | `enum : byte` | CONNACK reason codes |
| `MqttPubAckReasonCode` | `enum : byte` | PUBACK reason codes |
| `PubRecReasonCode` | `enum : byte` | PUBREC reason codes |
| `PubRelReasonCode` | `enum : byte` | PUBREL reason codes |
| `PubCompReasonCode` | `enum : byte` | PUBCOMP reason codes |
| `MqttSubscribeReasonCode` | `enum : byte` | SUBACK reason codes |
| `MqttUnsubscribeReasonCode` | `enum : byte` | UNSUBACK reason codes |
| `DisconnectReasonCode` | `enum : byte` | DISCONNECT reason codes |
| `AuthReasonCode` | `enum : byte` | AUTH reason codes (MQTT 5.0) |
| `PayloadFormatIndicator` | `enum : byte` | Payload encoding hint |
| `RetainHandlingOption` | `enum : byte` | Retain-on-subscribe behavior |

### 1.4 `TurboMqtt.Protocol`

| Type | Kind | Notes |
|------|------|-------|
| `IAckResponse` | `interface` | Base for all response types |
| `IConnectResponse` | `interface` | Connect response |
| `IDisconnectResponse` | `interface` | Disconnect response |
| `ISubscribeResponse` | `interface` | Subscribe response |
| `IUnsubscribeResponse` | `interface` | Unsubscribe response |
| `AckProtocol` | `static class` | Concrete response implementations |
| `AckProtocol.ConnectSuccess` | `sealed class` | |
| `AckProtocol.ConnectFailure` | `sealed class` | |
| `AckProtocol.DisconnectSuccess` | `sealed class` | Singleton |
| `AckProtocol.SubscribeSuccess` | `sealed class` | |
| `AckProtocol.SubscribeFailure` | `sealed class` | |
| `AckProtocol.UnsubscribeSuccess` | `sealed class` | |
| `AckProtocol.UnsubscribeFailure` | `sealed class` | |
| `MqttProtocolVersion` | `enum : byte` | V3_1_1 = 4, V5_0 = 5 |

### 1.5 `TurboMqtt.Protocol.Pub`

| Type | Kind | Notes |
|------|------|-------|
| `IPublishResult` | `interface` | Result of a publish operation |
| `PublishingStatus` | `enum` | Publishing / PubRecReceived / Completed / Failed |
| `PublishingProtocol` | `static class` | Factory for publish results |
| `PublishingProtocol.PublishSuccess` | `sealed class` | Singleton success |
| `PublishingProtocol.PublishFailure` | `sealed class` | Error with reason string |
| `PublishingProtocol.PublishCancelled` | `sealed class` | Cancelled with packet ID |
| `PublishingProtocol.SetReceiveMaximum` | `sealed class` | Internal actor message; ⚠️ should be internal |

### 1.6 `TurboMqtt.Utility`

| Type | Kind | Notes |
|------|------|-------|
| `UShortCounter` | `sealed class` | Thread-safe ushort counter; ⚠️ should be internal |

---

## 2. Findings

### 2.1 Naming Issues

#### F-1: `TurbotMqttHostingExtensions` — typo in class and file name

**Severity:** Low (pre-1.0 breaking change acceptable)

The class is named `TurbotMqttHostingExtensions` (`TurbotMqtt` instead of `TurboMqtt`). The file
is named identically. The extension method `AddTurboMqttClientFactory` is correctly named.

**Recommendation:** Rename the class to `TurboMqttHostingExtensions` before v1.0. This is a
breaking change but is the last opportunity to fix it without a major version bump.

**Action required:** File a fix issue; include in v1.0 release branch.

---

#### F-2: `MqttLastWill` vs. `LastWillAndTestament` — dual naming for LWT

**Severity:** Low

Two public types represent the "Last Will and Testament" concept with different names:
- `MqttLastWill` (in `PacketTypes`) — used inside the wire-format `ConnectPacket`
- `LastWillAndTestament` (in `Client`) — used in `MqttClientConnectOptions`, the user-facing API

The naming is intentionally layered (wire vs. user API) but can confuse callers who see both when
inspecting packet types. `MqttLastWill` should arguably be `internal` since callers use
`LastWillAndTestament` to configure the client, and the library handles the translation.

**Recommendation:** Make `MqttLastWill` (and all other wire-format `PacketTypes`) `internal`. User
code should only touch `Client` namespace types and `MqttMessage`. For v1.0 this is a **breaking
change** — defer to v2.0 unless the decision is made now.

**Decision for v1.0:** Retain `MqttLastWill` as public to avoid API churn mid-cycle. Document that
packet types in `TurboMqtt.PacketTypes` are advanced/low-level and subject to change in future
minor versions (mark with `[Experimental]`).

---

### 2.2 Public Types That Should Be Internal

#### F-3: `MqttClient` concrete class is public

**Severity:** Medium

`MqttClient` is a `public sealed class` that implements the internal `IInternalMqttClient`
interface. It should be `internal` — users obtain an `IMqttClient` from `IMqttClientFactory` and
have no reason to reference the concrete type. Keeping it public creates an unintended API surface:
callers may write `var client = new MqttClient(...)` or use pattern matching on the concrete type,
making future refactoring harder.

**Recommendation:** Make `MqttClient` internal before v1.0. This is a **breaking change** only if
callers reference the concrete type, which the factory pattern discourages.

**Action for v1.0:** Change `public sealed class MqttClient` → `internal sealed class MqttClient`.

---

#### F-4: `MqttClientFactory` concrete class is public

**Severity:** Low

`MqttClientFactory` is public but callers should inject `IMqttClientFactory`. Keeping it public
exposes the `Akka.NET` constructor signature (`ActorSystem`) as API surface.

**Recommendation:** No change for v1.0 — it is common practice to expose the factory concrete
class so callers who don't use DI can `new` it directly. Document that the constructor parameter is
subject to change in future major versions.

---

#### F-5: `PublishingProtocol.SetReceiveMaximum` is public

**Severity:** Low

`SetReceiveMaximum` is an internal actor message used between actors. It should be `internal`.
Users have no reason to create or consume it.

**Recommendation:** Make `SetReceiveMaximum` internal before v1.0.

**Action for v1.0:** Change `public sealed class SetReceiveMaximum` → `internal sealed class SetReceiveMaximum`.

---

#### F-6: `UShortCounter` is public

**Severity:** Low

`UShortCounter` is a thread-safe counter used internally for packet ID generation. Users have no
reason to reference it directly.

**Recommendation:** Make `UShortCounter` internal before v1.0.

**Action for v1.0:** Change `public sealed class UShortCounter` → `internal sealed class UShortCounter`.

---

### 2.3 `IAsyncDisposable` Consistency

| Type | IAsyncDisposable | Notes |
|------|-----------------|-------|
| `IMqttClient` | ✅ yes | Via interface declaration |
| `MqttClient` | ✅ yes | Inherited from `IMqttClient` |
| `IMqttClientFactory` | ❌ no | Factory holds actor refs; does not implement disposal |
| `MqttClientFactory` | ❌ no | See above |

**Assessment:** Acceptable. Factories in .NET commonly are not disposable. The `ActorSystem` that
backs the factory is managed externally (either by Akka.Hosting or the caller). No changes needed.

---

### 2.4 Extend-Only Compatibility

The public API uses the following patterns that are safe for extend-only growth:

| Pattern | Assessment |
|---------|------------|
| `sealed record` for options types | ✅ Safe — callers use `with` init; cannot subclass |
| `sealed class` for packet types | ✅ Safe — prevents inheritance surprises |
| Return-type interfaces (`IMqttClient`, `IPublishResult`, etc.) | ✅ Safe — implementation can change |
| `enum : byte` for reason codes | ⚠️ Adding new values is non-breaking; removing values is breaking |
| `interface` for response types | ✅ Adding default interface members is non-breaking in C# 8+ |
| `static class` for `AckProtocol` | ✅ Adding new nested types is non-breaking |

**Stable, no changes needed** for the core `IMqttClient` / `IMqttClientFactory` contracts.

---

### 2.5 Naming Consistency in `IAckResponse` Implementations

`IAckResponse.Reason` is declared as `string?` (nullable). Most implementations return
`string?` correctly, but:

- `AckProtocol.SubscribeFailure.Reason` returns `string` (non-nullable) — consistent with the
  interface since a non-nullable is assignable to nullable.
- `AckProtocol.UnsubscribeFailure.Reason` returns `string` (non-nullable).

These are technically correct (covariant return types) but are a minor inconsistency in that
implementations don't agree on nullability. **No change needed for v1.0**.

---

## 3. Breaking Changes from Phase 4 (Tasks 4.6 and 4.7)

Both changes were introduced before v1.0 and are therefore **not subject to SemVer**. They are
documented here for traceability. Full rationale is in [v1.0-criteria.md](v1.0-criteria.md).

### Task 4.6: `DelayInterval` type changed from `NonZeroUInt16` to `uint`

**Affected types:**
- `MqttLastWill.DelayInterval` (`PacketTypes` namespace)
- `LastWillAndTestament.DelayInterval` (`Client` namespace)

**Why:** MQTT 5.0 spec §3.1.3.2.2 defines Will Delay Interval as a Four Byte Integer (uint32,
0–4,294,967,295). `NonZeroUInt16` truncated to 65,535 and rejected 0 (which is valid — publish
immediately on disconnect).

**Migration:** Replace `NonZeroUInt16` values with `uint`. Values of 0 are now accepted. Values
above 65,535 are now correctly encoded/decoded.

---

### Task 4.7: `UserProperties` type changed from `IReadOnlyDictionary<string, string>?` to `IReadOnlyList<KeyValuePair<string, string>>?`

**Affected types:** All packet types and `MqttClientConnectOptions`/`LastWillAndTestament` that
carried `UserProperties` or `WillProperties`.

**Why:** MQTT 5.0 spec §3.1.2.11.8 explicitly permits duplicate keys in User Properties.
`IReadOnlyDictionary<string, string>` silently drops duplicate keys.

**Migration:** Replace dictionary literals with list of `KeyValuePair<string, string>` objects, or
use the new `.AsUserProperties()` extension helper (if provided). When reading properties, iterate
with `foreach` rather than `[]` indexer.

---

## 4. Recommended Actions Before v1.0 Tag

The following items are prioritized by severity. Items marked **MUST** block the 1.0 tag.

| # | Severity | Finding | Action | Status |
|---|----------|---------|--------|--------|
| 1 | MUST | F-3: `MqttClient` is public | Make `internal` | Open |
| 2 | MUST | F-5: `SetReceiveMaximum` is public | Make `internal` | Open |
| 3 | MUST | F-6: `UShortCounter` is public | Make `internal` | Open |
| 4 | SHOULD | F-1: `TurbotMqttHostingExtensions` typo | Rename class | Open |
| 5 | MAY | F-2: `MqttLastWill` as public wire type | Mark `[Experimental]` or defer | Deferred |
| 6 | MAY | F-4: `MqttClientFactory` is public | No change (by design) | Accepted |

---

## 5. API Surface Stability Promise for v1.0

Starting from v1.0.0, TurboMqtt follows **strict SemVer** as defined in
[v1.0-criteria.md](v1.0-criteria.md):

- `TurboMqtt.Client` namespace — **stable, versioned**
- `TurboMqtt.Protocol` namespace (response interfaces + reason codes) — **stable, versioned**
- `TurboMqtt.PacketTypes` namespace — **advanced API**; stable for packet construction; internal
  implementation details (encoders, decoders) carry no stability promise
- `TurboMqtt.Protocol.Pub` publishing protocol — **stable** for `IPublishResult` and
  `PublishingStatus`; actor messages are internal-only
- `TurboMqtt.Utility` — **internal**; no stability promise after v1.0 cleanup
