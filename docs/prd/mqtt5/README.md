# PRD: MQTT 5.0 Support for TurboMqtt

**Epic:** https://github.com/petabridge/TurboMqtt/issues/67
**Status:** Draft
**Date:** 2026-02-19

---

## Overview

Implement full MQTT 5.0 protocol support in TurboMqtt, translating every relevant section of the [OASIS MQTT 5.0 Specification](https://docs.oasis-open.org/mqtt/mqtt/v5.0/mqtt-v5.0.html) into concrete code changes, verifiable tests, and acceptance criteria specific to TurboMqtt's Akka.NET/Akka.Streams architecture.

### Scope

**In scope (initial implementation):**
- All 15 MQTT 5.0 packet types encoded and decoded
- All MQTT 5.0 properties on all applicable packet types
- Reason Codes on all ACK/response packets
- User Properties on all packet types that support them
- Enhanced Authentication (Auth packet challenge-response)
- Topic Aliases (client-side)
- Subscription Options (No Local, Retain As Published, Retain Handling)
- Subscription Identifiers
- Flow Control via Receive Maximum
- Session Expiry Interval
- Will Delay Interval
- Message Expiry Interval
- Server-initiated Disconnect handling
- Request/Response pattern support (Response Topic + Correlation Data)

**Deferred (post-initial, separate PRDs):**
- Shared Subscriptions ($share/) — requires broker-side testing infrastructure
- Server Redirect (using Server Reference in CONNACK/DISCONNECT) — requires multi-broker test setup
- Topic Alias mapping optimization (LRU cache, alias reuse strategy)

---

## 1. Properties System (OASIS 2.2.2)

MQTT 5.0 introduces a typed property system. Properties appear after the variable header in most packet types. Each property is identified by a one-byte identifier and has a typed value.

### 1.1 Property Encoding/Decoding Infrastructure

**What exists:** `MqttPacketSizeEstimator.EstimateMqtt5PacketSize()` already handles all property types for size estimation.

**What needs to be built:**

| Component | File | Change |
|-----------|------|--------|
| Property writer | `src/TurboMqtt/Protocol/Mqtt5PropertyWriter.cs` (NEW) | Static methods to write each property type to a `Span<byte>` |
| Property reader | `src/TurboMqtt/Protocol/Mqtt5PropertyReader.cs` (NEW) | Static methods to read each property type from a `ReadOnlySpan<byte>` |
| Property identifiers | `src/TurboMqtt/Protocol/Mqtt5PropertyIdentifiers.cs` (NEW) | Constants for all 28 property identifiers |

**Property types to implement (OASIS Table 2-4):**

| Type | Identifier(s) | Encoding | Writer Method | Reader Method |
|------|---------------|----------|---------------|---------------|
| Byte | 0x01, 0x17, 0x19, 0x24, 0x25, 0x28, 0x29, 0x2A | 1 byte | `WriteByte(ref Span<byte>, byte id, byte value)` | `ReadByte(ref ReadOnlySpan<byte>)` |
| Two Byte Integer | 0x21, 0x22, 0x23 | 2 bytes big-endian | `WriteTwoByteInt(ref Span<byte>, byte id, ushort value)` | `ReadTwoByteInt(ref ReadOnlySpan<byte>)` |
| Four Byte Integer | 0x02, 0x11, 0x18, 0x27 | 4 bytes big-endian | `WriteFourByteInt(ref Span<byte>, byte id, uint value)` | `ReadFourByteInt(ref ReadOnlySpan<byte>)` |
| Variable Byte Integer | 0x0B | 1-4 bytes | `WriteVariableByteInt(ref Span<byte>, byte id, uint value)` | `ReadVariableByteInt(ref ReadOnlySpan<byte>)` |
| UTF-8 String | 0x03, 0x08, 0x09, 0x12, 0x15, 0x1A, 0x1C, 0x1F | Length-prefixed UTF-8 | `WriteUtf8String(ref Span<byte>, byte id, string value)` | `ReadUtf8String(ref ReadOnlySpan<byte>)` |
| UTF-8 String Pair | 0x26 | Two length-prefixed UTF-8 strings | `WriteStringPair(ref Span<byte>, string key, string value)` | `ReadStringPair(ref ReadOnlySpan<byte>)` |
| Binary Data | 0x09, 0x16 | Length-prefixed bytes | `WriteBinaryData(ref Span<byte>, byte id, ReadOnlySpan<byte> data)` | `ReadBinaryData(ref ReadOnlySpan<byte>)` |

**Performance requirements:**
- All property writers use `ref Span<byte>` to advance the write position (same pattern as `Mqtt311Encoder`)
- All property readers use `ref ReadOnlySpan<byte>` to advance the read position
- No heap allocations for property encoding/decoding of primitive types
- User Properties (`IReadOnlyDictionary<string, string>`) may allocate; use pooled dictionaries where possible

**Verifiable tests:**
- [ ] Unit test: Each property type roundtrips correctly (write then read)
- [ ] Unit test: Variable Byte Integer encoding handles boundary values (0, 127, 128, 16383, 16384, 2097151, 2097152, 268435455)
- [ ] Unit test: UTF-8 String encoding handles empty string, ASCII, multi-byte UTF-8
- [ ] Unit test: Binary Data encoding handles empty, 1 byte, max reasonable size
- [ ] FsCheck property: Random property values roundtrip through write/read
- [ ] Unit test: Unknown property identifier in reader returns error (not crash)

---

## 2. CONNECT Packet (OASIS 3.1)

### 2.1 What Exists

`ConnectPacket.cs` already has all MQTT 5.0 properties defined:
- `ReceiveMaximum`, `MaximumPacketSize`, `TopicAliasMaximum`
- `SessionExpiryInterval`, `RequestProblemInformation`, `RequestResponseInformation`
- `AuthenticationMethod`, `AuthenticationData`, `UserProperties`

`MqttLastWill` already has V5 Will properties:
- `ResponseTopic`, `WillCorrelationData`, `ContentType`
- `PayloadFormatIndicator`, `DelayInterval`, `MessageExpiryInterval`, `WillProperties`

`MqttClientConnectOptions` has TODOed V5 fields (lines 35-43) that need to be uncommented.

### 2.2 What Needs to Change

| File | Change |
|------|--------|
| `Mqtt5Encoder.cs` (NEW) | Encode Connect Properties after Variable Header (Protocol Name, Level, Flags, Keep Alive) |
| `Mqtt5Decoder.cs` (NEW) | Decode Connect Properties after Variable Header |
| `MqttClientConnectOptions.cs` | Uncomment V5 will properties (lines 35-43) |

**CONNECT encoding order (MQTT 5.0):**
1. Fixed Header (0x10)
2. Variable Header: Protocol Name ("MQTT"), Protocol Level (5), Connect Flags, Keep Alive
3. **Connect Properties** (NEW): Property Length + properties
4. Payload: Client ID, **Will Properties** (NEW) + Will Topic + Will Payload, Username, Password

**Connect Properties to encode/decode:**

| Property | ID | Type | Source Field |
|----------|----|------|-------------|
| Session Expiry Interval | 0x11 | Four Byte Integer | `ConnectPacket.SessionExpiryInterval` |
| Receive Maximum | 0x21 | Two Byte Integer | `ConnectPacket.ReceiveMaximum` |
| Maximum Packet Size | 0x27 | Four Byte Integer | `ConnectPacket.MaximumPacketSize` |
| Topic Alias Maximum | 0x22 | Two Byte Integer | `ConnectPacket.TopicAliasMaximum` |
| Request Response Information | 0x19 | Byte | `ConnectPacket.RequestResponseInformation` |
| Request Problem Information | 0x17 | Byte | `ConnectPacket.RequestProblemInformation` |
| User Property | 0x26 | UTF-8 String Pair | `ConnectPacket.UserProperties` |
| Authentication Method | 0x15 | UTF-8 String | `ConnectPacket.AuthenticationMethod` |
| Authentication Data | 0x16 | Binary Data | `ConnectPacket.AuthenticationData` |

**Will Properties to encode/decode:**

| Property | ID | Type | Source Field |
|----------|----|------|-------------|
| Will Delay Interval | 0x18 | Four Byte Integer | `MqttLastWill.DelayInterval` |
| Payload Format Indicator | 0x01 | Byte | `MqttLastWill.PayloadFormatIndicator` |
| Message Expiry Interval | 0x02 | Four Byte Integer | `MqttLastWill.MessageExpiryInterval` |
| Content Type | 0x03 | UTF-8 String | `MqttLastWill.ContentType` |
| Response Topic | 0x08 | UTF-8 String | `MqttLastWill.ResponseTopic` |
| Correlation Data | 0x09 | Binary Data | `MqttLastWill.WillCorrelationData` |
| User Property | 0x26 | UTF-8 String Pair | `MqttLastWill.WillProperties` |

**Verifiable tests:**
- [ ] Unit test: CONNECT with no optional V5 properties encodes/decodes correctly (minimal packet)
- [ ] Unit test: CONNECT with all V5 properties set encodes/decodes with field equality
- [ ] Unit test: CONNECT with Will + all Will Properties roundtrips
- [ ] Unit test: CONNECT with Authentication Method + Data roundtrips
- [ ] Unit test: CONNECT with User Properties (multiple pairs) roundtrips
- [ ] Unit test: Protocol Level byte is 5 (not 4) in encoded output
- [ ] FsCheck property: Random ConnectPacket (with V5 fields) roundtrips through Mqtt5Encoder/Mqtt5Decoder
- [ ] E2E: EMQX accepts TurboMqtt MQTT 5.0 CONNECT and returns CONNACK with V5 properties

---

## 3. CONNACK Packet (OASIS 3.2)

### 3.1 What Exists

`ConnAckPacket.cs` has some V5 properties:
- `SessionPresent`, `ReasonCode`, `MaximumPacketSize`, `ReceiveMaximum`
- `UserProperties`, `ReasonString`

### 3.2 Missing Properties

| Property | ID | Type | Needs Adding to ConnAckPacket |
|----------|----|------|------|
| Session Expiry Interval | 0x11 | Four Byte Integer | YES |
| Assigned Client Identifier | 0x12 | UTF-8 String | YES |
| Server Keep Alive | 0x13 | Two Byte Integer | YES |
| Authentication Method | 0x15 | UTF-8 String | YES |
| Authentication Data | 0x16 | Binary Data | YES |
| Response Information | 0x1A | UTF-8 String | YES |
| Server Reference | 0x1C | UTF-8 String | YES |
| Topic Alias Maximum | 0x22 | Two Byte Integer | YES |
| Maximum QoS | 0x24 | Byte | YES |
| Retain Available | 0x25 | Byte | YES |
| Wildcard Subscription Available | 0x28 | Byte | YES |
| Subscription Identifiers Available | 0x29 | Byte | YES |
| Shared Subscription Available | 0x2A | Byte | YES |

### 3.3 What Needs to Change

| File | Change |
|------|--------|
| `ConnAckPacket.cs` | Add 13 missing V5 properties listed above |
| `Mqtt5Encoder.cs` | CONNACK encoding with Properties section |
| `Mqtt5Decoder.cs` | CONNACK decoding with Properties section |
| `MqttPacketSizeEstimator.cs` | `EstimateConnAckPacketSizeMqtt5()` needs updating — currently minimal (line 509) |

**CONNACK reason codes (OASIS Table 3-1):**
Existing `ConnAckReasonCode` enum needs these values if not already present:
- 0x00 Success, 0x80 Unspecified error, 0x81 Malformed Packet, 0x82 Protocol Error
- 0x83 Implementation specific error, 0x84 Unsupported Protocol Version
- 0x85 Client Identifier not valid, 0x86 Bad User Name or Password
- 0x87 Not authorized, 0x88 Server unavailable, 0x89 Server busy
- 0x8A Banned, 0x8C Bad authentication method
- 0x90 Topic Name invalid, 0x95 Packet too large
- 0x97 Quota exceeded, 0x99 Payload format invalid
- 0x9A Retain not supported, 0x9B QoS not supported
- 0x9C Use another server, 0x9D Server moved
- 0x9F Connection rate exceeded

**TurboMqtt-specific behavior:**
- On receiving CONNACK, the client MUST store broker-advertised limits:
  - `ReceiveMaximum` → controls in-flight QoS 1/2 publish window (affects `AtLeastOncePublishRetryActor` and `ExactlyOncePublishRetryActor`)
  - `MaximumPacketSize` → encoder must validate outbound packets don't exceed this
  - `TopicAliasMaximum` → controls topic alias table size
  - `MaximumQoS` → client must not publish at higher QoS than broker supports
  - `RetainAvailable` → client must not set retain flag if broker doesn't support it
  - `ServerKeepAlive` → overrides client-requested keep alive (affects `HeartBeatActor`)

**Where to store broker limits:**
These should be stored on the client session state and propagated to the relevant actors. This likely means extending `ClientStreamOwner` or adding a new `Mqtt5SessionState` class.

**Verifiable tests:**
- [ ] Unit test: CONNACK with all V5 properties decodes correctly from hand-crafted bytes
- [ ] Unit test: CONNACK reason codes map to correct enum values
- [ ] Unit test: CONNACK with Assigned Client Identifier overwrites the client's ID
- [ ] Unit test: CONNACK Server Keep Alive overrides client-requested keep alive
- [ ] FsCheck property: Random ConnAckPacket (with V5 fields) roundtrips through encoder/decoder
- [ ] E2E: TurboMqtt parses EMQX CONNACK V5 properties (Receive Maximum, Topic Alias Maximum, etc.)

---

## 4. PUBLISH Packet (OASIS 3.3)

### 4.1 What Exists

`PublishPacket.cs` already has ALL V5 properties:
- `TopicAlias`, `MessageExpiryInterval`, `PayloadFormatIndicator`
- `ContentType`, `ResponseTopic`, `CorrelationData`
- `UserProperties`, `SubscriptionIdentifiers`

### 4.2 What Needs to Change

| File | Change |
|------|--------|
| `Mqtt5Encoder.cs` | PUBLISH encoding with Properties section after Topic Name + Packet ID |
| `Mqtt5Decoder.cs` | PUBLISH decoding with Properties section |

**PUBLISH properties to encode/decode:**

| Property | ID | Type | Source Field |
|----------|----|------|-------------|
| Payload Format Indicator | 0x01 | Byte | `PublishPacket.PayloadFormatIndicator` |
| Message Expiry Interval | 0x02 | Four Byte Integer | `PublishPacket.MessageExpiryInterval` |
| Topic Alias | 0x23 | Two Byte Integer | `PublishPacket.TopicAlias` |
| Response Topic | 0x08 | UTF-8 String | `PublishPacket.ResponseTopic` |
| Correlation Data | 0x09 | Binary Data | `PublishPacket.CorrelationData` |
| User Property | 0x26 | UTF-8 String Pair | `PublishPacket.UserProperties` |
| Subscription Identifier | 0x0B | Variable Byte Integer | `PublishPacket.SubscriptionIdentifiers` (can appear multiple times) |
| Content Type | 0x03 | UTF-8 String | `PublishPacket.ContentType` |

**TurboMqtt-specific behavior:**
- Topic Alias handling: When the client sends a PUBLISH with a Topic Alias, it must maintain a mapping table. If TopicAlias > 0 and TopicName is non-empty, the encoder establishes the alias. If TopicAlias > 0 and TopicName is empty, the encoder uses the existing alias.
- Inbound Topic Alias: When receiving PUBLISH with a Topic Alias, the decoder must maintain a server-to-client alias table and resolve the topic name.
- **Topic Alias tables should be actor-managed state** (likely on `ClientStreamOwner` or a new `TopicAliasManager`).
- Subscription Identifiers on inbound PUBLISH tell the client which subscription matched. These should be passed through to the `ChannelWriter<MqttMessage>` so user code can route.

**Verifiable tests:**
- [ ] Unit test: PUBLISH with no V5 properties encodes same as MQTT 3.1.1 (minus protocol version differences)
- [ ] Unit test: PUBLISH with all V5 properties roundtrips
- [ ] Unit test: PUBLISH with Topic Alias (establishing alias) encodes correctly
- [ ] Unit test: PUBLISH with Topic Alias (using existing alias, empty topic) encodes correctly
- [ ] Unit test: PUBLISH with multiple Subscription Identifiers roundtrips
- [ ] Unit test: PUBLISH with User Properties (5+ pairs) roundtrips
- [ ] FsCheck property: Random PublishPacket (with V5 fields) roundtrips
- [ ] E2E: TurboMqtt publishes MQTT 5.0 message with User Properties to EMQX and receives them back on subscriber

---

## 5. ACK Packets — PUBACK, PUBREC, PUBREL, PUBCOMP (OASIS 3.4-3.7)

### 5.1 What Exists

All four ACK packet classes already have V5 properties:
- `PubAckPacket`: `ReasonCode` (present), `ReasonString` (computed from enum). **Missing:** `UserProperties`
- `PubRecPacket`: `ReasonCode`, `ReasonString`, `UserProperties` (all present)
- `PubRelPacket`: `ReasonCode`, `ReasonString`, `UserProperties` (all present)
- `PubCompPacket`: `ReasonCode`, `ReasonString`, `UserProperties` (all present)

### 5.2 What Needs to Change

| File | Change |
|------|--------|
| `PubAckPacket.cs` | Add `UserProperties` field |
| `PubAckPacket.cs` | Change `ReasonString` from computed to stored (for broker-provided strings) |
| `Mqtt5Encoder.cs` | Encode V5 ACK properties (Reason Code, Reason String, User Properties) |
| `Mqtt5Decoder.cs` | Decode V5 ACK properties |

**ACK encoding rules (OASIS 3.4.2.1):**
- If Reason Code is 0x00 (Success) AND there are no properties, the ACK can be encoded with just 2 bytes (Packet ID only, no Reason Code byte). This is the compact form.
- If Reason Code is present, encode it as 1 byte after Packet ID.
- If Properties are present, encode Property Length + properties after Reason Code.

**Reason Codes by packet type:**

| Packet | Success | Failure Codes |
|--------|---------|---------------|
| PUBACK | 0x00 | 0x10 No matching subscribers, 0x80 Unspecified, 0x83 Implementation specific, 0x87 Not authorized, 0x90 Topic Name invalid, 0x91 Packet ID in use, 0x97 Quota exceeded, 0x99 Payload format invalid |
| PUBREC | 0x00 | 0x10, 0x80, 0x83, 0x87, 0x90, 0x91, 0x97, 0x99 |
| PUBREL | 0x00 | 0x92 Packet Identifier not found |
| PUBCOMP | 0x00 | 0x92 Packet Identifier not found |

**TurboMqtt-specific behavior:**
- `ClientAcksActor` must interpret V5 Reason Codes. A PUBACK with reason 0x80+ means the publish failed — this should propagate to the caller.
- `AtLeastOncePublishRetryActor` and `ExactlyOncePublishRetryActor` need to understand non-success reason codes and stop retrying (unlike 3.1.1 where PUBACK always means success).

**Verifiable tests:**
- [ ] Unit test: PUBACK compact form (Success, no properties) encodes as just Packet ID (2 bytes)
- [ ] Unit test: PUBACK with Reason Code + Reason String + User Properties roundtrips
- [ ] Unit test: PUBREC/PUBREL/PUBCOMP with V5 properties roundtrip
- [ ] Unit test: All defined Reason Codes for each ACK type have enum values
- [ ] FsCheck property: Random ACK packets (all 4 types) with V5 fields roundtrip
- [ ] Integration test: `ClientAcksActor` treats PUBACK with failure reason code as publish failure
- [ ] Integration test: Retry actors stop retrying on non-retryable reason codes

---

## 6. SUBSCRIBE Packet (OASIS 3.8)

### 6.1 What Exists

`SubscribePacket.cs` has `UserProperties`. `TopicSubscription` has V5 subscription options: `NoLocal`, `RetainAsPublished`, `RetainHandling`.

### 6.2 Missing

| Property | ID | Notes |
|----------|----|-------|
| Subscription Identifier | 0x0B | Missing from `SubscribePacket` — needs adding |

### 6.3 What Needs to Change

| File | Change |
|------|--------|
| `SubscribePacket.cs` | Add `SubscriptionIdentifier` (uint?) property |
| `Mqtt5Encoder.cs` | Encode Subscribe Properties (Subscription Identifier, User Properties) + V5 Subscription Options byte |
| `Mqtt5Decoder.cs` | Decode Subscribe Properties + V5 Subscription Options byte |

**Subscription Options byte (OASIS 3.8.3.1):**
```
Bit 0-1: Maximum QoS (same as 3.1.1)
Bit 2:   No Local (0 = deliver own messages, 1 = don't)
Bit 3:   Retain As Published (0 = use broker retain, 1 = keep original retain flag)
Bit 4-5: Retain Handling (0 = send on subscribe, 1 = send if new, 2 = don't send)
Bit 6-7: Reserved (must be 0)
```

**Verifiable tests:**
- [ ] Unit test: SUBSCRIBE with V5 options byte (No Local, Retain As Published, Retain Handling) encodes correctly
- [ ] Unit test: SUBSCRIBE with Subscription Identifier roundtrips
- [ ] Unit test: SUBSCRIBE with User Properties roundtrips
- [ ] Unit test: Subscription Options byte bit layout matches OASIS spec
- [ ] FsCheck property: Random SubscribePacket with V5 fields roundtrips
- [ ] E2E: TurboMqtt subscribes with MQTT 5.0 and receives Subscription Identifiers on inbound PUBLISH

---

## 7. SUBACK Packet (OASIS 3.9)

### 7.1 What Exists

`SubAckPacket.cs` has `ReasonString` and `UserProperties`.

### 7.2 What Needs to Change

| File | Change |
|------|--------|
| `Mqtt5Encoder.cs` | Encode SubAck Properties (Reason String, User Properties) |
| `Mqtt5Decoder.cs` | Decode SubAck Properties + V5 Reason Codes per topic |

**SUBACK Reason Codes (per topic, OASIS Table 3-8):**
- 0x00 Granted QoS 0, 0x01 Granted QoS 1, 0x02 Granted QoS 2
- 0x80 Unspecified error, 0x83 Implementation specific
- 0x87 Not authorized, 0x8F Topic Filter invalid
- 0x91 Packet Identifier in use, 0x97 Quota exceeded
- 0x9E Shared Subscriptions not supported
- 0xA1 Subscription Identifiers not supported
- 0xA2 Wildcard Subscriptions not supported

**Verifiable tests:**
- [ ] Unit test: SUBACK with V5 reason codes (including failure codes) decodes correctly
- [ ] Unit test: SUBACK with Properties (Reason String, User Properties) roundtrips
- [ ] FsCheck property: Random SubAckPacket with V5 fields roundtrips
- [ ] E2E: TurboMqtt receives SUBACK from EMQX with V5 reason codes

---

## 8. UNSUBSCRIBE / UNSUBACK (OASIS 3.10-3.11)

### 8.1 What Exists

Both `UnsubscribePacket` and `UnsubAckPacket` already have V5 properties (`UserProperties`, `ReasonCodes`, `ReasonString`).

### 8.2 What Needs to Change

| File | Change |
|------|--------|
| `Mqtt5Encoder.cs` | Encode Unsubscribe/UnsubAck Properties |
| `Mqtt5Decoder.cs` | Decode Unsubscribe/UnsubAck Properties + per-topic Reason Codes |

**UNSUBACK Reason Codes (OASIS Table 3-9):**
- 0x00 Success, 0x11 No subscription existed
- 0x80 Unspecified, 0x83 Implementation specific, 0x87 Not authorized
- 0x8F Topic Filter invalid, 0x91 Packet Identifier in use

**Verifiable tests:**
- [ ] Unit test: UNSUBACK with per-topic Reason Codes roundtrips
- [ ] Unit test: UNSUBACK with "No subscription existed" (0x11) reason code handled
- [ ] FsCheck property: Random UnsubscribePacket and UnsubAckPacket roundtrip

---

## 9. DISCONNECT Packet (OASIS 3.14)

### 9.1 What Exists

`DisconnectPacket.cs` has V5 properties: `ReasonCode`, `UserProperties`, `ServerReference`, `SessionExpiryInterval`.

### 9.2 Key MQTT 5.0 Change: Server-Initiated Disconnect

In MQTT 3.1.1, only the client sends DISCONNECT. In MQTT 5.0, the **server can also send DISCONNECT** to the client. This is a significant behavioral change.

### 9.3 What Needs to Change

| File | Change |
|------|--------|
| `Mqtt5Encoder.cs` | Encode Disconnect Properties |
| `Mqtt5Decoder.cs` | Decode Disconnect Properties (especially from server) |
| `ClientStreamOwner.cs` or stream pipeline | Handle inbound DISCONNECT from server (new code path) |

**Disconnect Reason Codes (OASIS Table 3-10):**

Client-sent:
- 0x00 Normal, 0x04 Disconnect with Will Message
- 0x80 Unspecified, 0x83 Implementation specific

Server-sent:
- 0x00 Normal, 0x81 Malformed Packet, 0x82 Protocol Error
- 0x83 Implementation specific, 0x87 Not authorized
- 0x89 Server busy, 0x8B Server shutting down
- 0x8D Keep Alive timeout, 0x8E Session taken over
- 0x90 Topic Name invalid, 0x93 Receive Maximum exceeded
- 0x94 Topic Alias invalid, 0x95 Packet too large
- 0x96 Message rate too high, 0x97 Quota exceeded
- 0x98 Administrative action, 0x99 Payload format invalid
- 0x9A Retain not supported, 0x9B QoS not supported
- 0x9C Use another server, 0x9D Server moved
- 0x9E Shared Subscriptions not supported
- 0x9F Connection rate exceeded, 0xA0 Maximum connect time

**TurboMqtt-specific behavior:**
- Inbound DISCONNECT must trigger the same cleanup path as a connection drop, but should also:
  1. Log the Reason Code and Reason String
  2. Emit an OpenTelemetry event with the disconnect reason
  3. If Reason Code is 0x9C (Use another server) or 0x9D (Server moved), include `ServerReference` in the disconnection event for potential redirect handling
- The current `ClientStreamOwner` likely treats inbound DISCONNECT from the server as unexpected. This needs a new message handler.

**Verifiable tests:**
- [ ] Unit test: Client DISCONNECT with V5 properties encodes correctly
- [ ] Unit test: Server DISCONNECT with all V5 properties decodes correctly
- [ ] Unit test: All defined Disconnect Reason Codes have enum values
- [ ] FsCheck property: Random DisconnectPacket roundtrips
- [ ] Integration test: When broker sends DISCONNECT, client cleans up correctly (actor hierarchy shuts down gracefully)
- [ ] E2E: Force EMQX to disconnect client (e.g., via management API) and verify TurboMqtt handles it

---

## 10. AUTH Packet (OASIS 3.15) — NEW in MQTT 5.0

### 10.1 What Exists

`AuthPacket.cs` is fully defined with all required properties:
- `ReasonCode` (AuthReasonCode): Success (0x00), ContinueAuthentication (0x18), ReAuthenticate (0x19)
- `AuthenticationMethod` (string)
- `AuthenticationData` (ReadOnlyMemory<byte>)
- `UserProperties`, `ReasonString`

### 10.2 What Needs to Be Built

| Component | File | Change |
|-----------|------|--------|
| Auth encoding | `Mqtt5Encoder.cs` | Encode Auth packet (Reason Code + Properties) |
| Auth decoding | `Mqtt5Decoder.cs` | Decode Auth packet |
| Auth flow state machine | `src/TurboMqtt/Client/Mqtt5AuthHandler.cs` (NEW) | Challenge-response state machine |
| Auth integration | `ClientStreamOwner.cs` | Wire auth handler into connection lifecycle |
| Public API | `MqttClientConnectOptions.cs` | Auth method + callback/interface for challenge-response |

**Auth flow (OASIS 4.12):**
1. Client sends CONNECT with Authentication Method + optional Authentication Data
2. Server responds with either:
   - CONNACK (auth succeeded or not required)
   - AUTH with Reason Code 0x18 (Continue Authentication) + challenge data
3. If AUTH received, client responds with AUTH (Reason Code 0x18) + response data
4. Steps 2-3 repeat until server sends CONNACK

**Re-authentication (OASIS 4.12.1):**
1. Client sends AUTH with Reason Code 0x19 (Re-Authenticate) + Authentication Method + Data
2. Server responds with AUTH (0x18) or CONNACK-style acceptance
3. If auth fails, server sends DISCONNECT

**TurboMqtt-specific design:**

The auth flow needs a user-facing interface for providing challenge-response logic. Proposed API:

```csharp
/// <summary>
/// User-provided handler for MQTT 5.0 Enhanced Authentication.
/// </summary>
public interface IMqtt5AuthHandler
{
    /// <summary>
    /// The authentication method name (e.g., "SCRAM-SHA-256").
    /// </summary>
    string AuthenticationMethod { get; }

    /// <summary>
    /// Called with the initial auth data to send with CONNECT.
    /// </summary>
    ReadOnlyMemory<byte> GetInitialAuthData();

    /// <summary>
    /// Called when the broker sends an AUTH challenge.
    /// Return the response data, or throw to abort authentication.
    /// </summary>
    ValueTask<ReadOnlyMemory<byte>> HandleChallengeAsync(
        ReadOnlyMemory<byte> challengeData,
        CancellationToken ct);
}
```

Set on `MqttClientConnectOptions.AuthHandler`.

The `Mqtt5AuthHandler` actor/class manages the state machine:
- State: `AwaitingConnAck` → `InChallenge` → `Authenticated`
- On incoming AUTH (0x18): call `HandleChallengeAsync`, send response AUTH
- On incoming CONNACK: transition to `Authenticated`
- On failure: propagate to `ClientStreamOwner` for connection teardown

**Verifiable tests:**
- [ ] Unit test: AUTH packet with all properties roundtrips through encoder/decoder
- [ ] Unit test: AUTH Reason Codes (Success, Continue, ReAuthenticate) encode/decode correctly
- [ ] Unit test: Auth state machine transitions: initial → challenge → response → success
- [ ] Unit test: Auth state machine handles failure (broker sends DISCONNECT instead of CONNACK)
- [ ] Unit test: Re-authentication flow (client-initiated)
- [ ] Integration test: Mock broker sends AUTH challenge, client responds correctly
- [ ] E2E: Connect to EMQX with enhanced authentication (if EMQX supports the chosen method)

---

## 11. Flow Control — Receive Maximum (OASIS 4.9)

### 11.1 Behavior

The broker's CONNACK `ReceiveMaximum` limits how many QoS 1 and QoS 2 PUBLISH packets the client can have in-flight (unacknowledged) at once. Default is 65535.

### 11.2 TurboMqtt Impact

Currently, `AtLeastOncePublishRetryActor` and `ExactlyOncePublishRetryActor` manage in-flight publishes. They need to respect the broker's Receive Maximum.

| File | Change |
|------|--------|
| `AtLeastOncePublishRetryActor.cs` | Accept Receive Maximum from session state; throttle publishes |
| `ExactlyOncePublishRetryActor.cs` | Accept Receive Maximum from session state; throttle publishes |
| `ClientStreamOwner.cs` | Pass broker Receive Maximum from CONNACK to retry actors |

**Verifiable tests:**
- [ ] Unit test: Retry actor queues publishes beyond Receive Maximum limit
- [ ] Unit test: Retry actor resumes publishing when ACKs arrive (freeing slots)
- [ ] Integration test: With Receive Maximum = 2, 5 publishes are sent 2 at a time
- [ ] E2E: Connect to EMQX which advertises Receive Maximum, verify no more than N in-flight

---

## 12. Encoder and Decoder Architecture

### 12.1 Encoder (`Mqtt5Encoder.cs`)

**Design:** Follow the same pattern as `Mqtt311Encoder.cs`:
- Static class with `EncodePacket(MqttPacket packet, ref Memory<byte> buffer, PacketSize estimatedSize)` entry point
- Switch on `MqttPacketType` to dispatch to type-specific encode methods
- Each type-specific method writes: Fixed Header → Variable Header → **Properties** → Payload
- Properties use the shared `Mqtt5PropertyWriter` helper

**Key difference from 3.1.1:** After the Variable Header fields, a Property Length (Variable Byte Integer) and property key-value pairs must be written before the Payload.

### 12.2 Decoder (`Mqtt5Decoder.cs`)

**Design:** Follow the same pattern as `Mqtt311Decoder.cs`:
- Stateful class with `_remainder` for partial frame handling
- `TryDecode(in ReadOnlyMemory<byte> input, out IReadOnlyList<MqttPacket> packets)` entry point
- Switch on `MqttPacketType` to dispatch to type-specific decode methods
- Each type-specific method reads: Variable Header → **Properties** → Payload
- Properties use the shared `Mqtt5PropertyReader` helper

**Key difference from 3.1.1:** After the Variable Header fields, read a Property Length, then iterate reading properties until Property Length bytes consumed.

### 12.3 Stream Integration

| File | Change |
|------|--------|
| `MqttEncodingFlows.cs` | Add `Mqtt5Encoding()` method (mirrors `Mqtt311Encoding()`) |
| `MqttDecodingFlows.cs` | Add `Mqtt5Decoding()` method (mirrors `Mqtt311Decoding()`) |
| `MqttClientStreams.cs` | Add `Mqtt5OutboundPacketSink()` and `Mqtt5InboundMessageSource()` |
| `ClientStreamInstance.cs` | Add `case MqttProtocolVersion.V5_0:` in `ConfigureMqttStreams()` (line 157) |
| `IMqttClientFactory.cs` | Remove `AssertMqtt311()` guard or make it version-aware |

**Verifiable tests:**
- [ ] Unit test: `Mqtt5Encoding()` flow produces valid encoded bytes for all packet types
- [ ] Unit test: `Mqtt5Decoding()` flow decodes valid bytes into correct packet types
- [ ] Integration test: Full Akka.Streams pipeline encode → decode roundtrip for V5
- [ ] Integration test: `MqttClientFactory.CreateTcpClient()` succeeds with `MqttProtocolVersion.V5_0`
- [ ] Regression test: All existing MQTT 3.1.1 tests still pass unchanged

---

## 13. Public API Changes

### 13.1 `MqttClientConnectOptions`

```csharp
// Uncomment existing TODOs (lines 35-43):
public string? ResponseTopic { get; set; }
public ReadOnlyMemory<byte>? WillCorrelationData { get; set; }
public string? ContentType { get; set; }
public PayloadFormatIndicator PayloadFormatIndicator { get; set; }
public NonZeroUInt16 DelayInterval { get; set; }
public uint MessageExpiryInterval { get; set; }
public IReadOnlyDictionary<string, string>? WillProperties { get; set; }

// NEW V5 connection options:
public uint SessionExpiryInterval { get; set; }
public ushort ReceiveMaximum { get; set; } = 65535;
public uint MaximumPacketSize { get; set; }
public ushort TopicAliasMaximum { get; set; }
public bool RequestResponseInformation { get; set; }
public bool RequestProblemInformation { get; set; } = true;
public IReadOnlyDictionary<string, string>? UserProperties { get; set; }
public IMqtt5AuthHandler? AuthHandler { get; set; }
```

### 13.2 `MqttMessage` (user-facing message type)

Check whether `MqttMessage` (the type written to `ChannelWriter`) exposes V5 properties. If not, it needs:
- `UserProperties`
- `ContentType`
- `ResponseTopic`
- `CorrelationData`
- `SubscriptionIdentifiers`
- `PayloadFormatIndicator`
- `MessageExpiryInterval`

### 13.3 Connection Events

The client should expose V5-specific connection events:
- Server-initiated disconnect (with Reason Code)
- Authentication challenge/response progress
- Session state (broker's session present + capabilities)

**Verifiable tests:**
- [ ] API test: `MqttClientConnectOptions` accepts all V5 properties without throwing
- [ ] API test: `MqttMessage` exposes User Properties, Content Type, Response Topic, Correlation Data
- [ ] E2E: User code receives `MqttMessage` with V5 properties from EMQX

---

## 14. Acceptance Criteria Summary

### Codec Completeness
- [ ] All 15 MQTT 5.0 packet types encode and decode correctly
- [ ] All 28 MQTT 5.0 property identifiers handled
- [ ] FsCheck roundtrip properties pass for all packet types at 100+ iterations
- [ ] Error path tests for malformed V5 properties (invalid identifiers, truncated data)

### Broker Compatibility
- [ ] Full E2E connect/subscribe/publish/disconnect against EMQX at MQTT 5.0
- [ ] QoS 0, 1, and 2 verified against EMQX with V5 protocol
- [ ] Authentication against EMQX with username/password over V5
- [ ] User Properties sent and received correctly through EMQX

### Performance
- [ ] MQTT 5.0 codec benchmarks exist (connect, publish, full pipeline)
- [ ] Throughput documented and compared against MQTT 3.1.1 baseline
- [ ] No throughput regression on MQTT 3.1.1 pipeline

### No Regression
- [ ] All existing MQTT 3.1.1 unit tests pass
- [ ] All existing MQTT 3.1.1 E2E tests pass
- [ ] All existing MQTT 3.1.1 benchmarks produce comparable results

---

## 15. Implementation Order

```
1. Property infrastructure (Mqtt5PropertyWriter, Mqtt5PropertyReader, identifiers)
2. Mqtt5Encoder (all 15 packet types)
3. Mqtt5Decoder (all 15 packet types)
4. FsCheck generators + roundtrip property tests
5. ConnAckPacket field additions
6. Stream integration (flows, client streams, factory)
7. Broker limit enforcement (Receive Maximum, Max Packet Size, etc.)
8. Auth flow (IMqtt5AuthHandler, state machine, actor integration)
9. Server-initiated Disconnect handling
10. Public API updates (MqttClientConnectOptions, MqttMessage)
11. E2E container tests
12. Benchmarks
```

This order ensures each layer is testable before the next builds on it.
