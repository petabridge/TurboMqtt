# RALPH Iteration 05 — Task 3.8

## Task Selected
**Task 3.8: Handle server-initiated DISCONNECT and update public API**

## Surface Area Classification
Cross-cutting (public API surface + protocol codec + actor telemetry)

## Verification Level
**L1** — Unit tests sufficient. No new I/O coordination or Docker containers needed.
The task is about:
- Adding fields to public record types (MqttMessage, MqttClientConnectOptions)
- Filling in a missing decoder case (ReasonString in DISCONNECT properties)
- Emitting an OpenTelemetry Activity from an actor message handler
- V5 encoder/estimator parity

## Skills Consulted
- `csharp-coding-standards` — init-only record properties, nullable types
- `akka-best-practices` — actor message handling, no fire-and-forget

## Changes Made

### `src/TurboMqtt/PacketTypes/DisconnectPacket.cs`
- Added `public string? ReasonString { get; set; }` (MQTT 5.0 property identifier 0x1F)
- Updated `ToString()` to include ReasonString

### `src/TurboMqtt/Protocol/Mqtt5Decoder.cs`
- Added `case Mqtt5PropertyIdentifiers.ReasonString:` in `ReadDisconnectProperties`
  so inbound server DISCONNECTs with a reason string are correctly decoded

### `src/TurboMqtt/Protocol/Mqtt5Encoder.cs`
- Added ReasonString to propsSize calculation and encoding in `EncodeDisconnectPacket`

### `src/TurboMqtt/Protocol/MqttPacketSizeEstimator.cs`
- Added ReasonString to size calculation in `EstimateDisconnectPacketSizeMqtt5`
  (encoder and estimator must agree for Debug.Assert to hold)

### `src/TurboMqtt/MqttMessage.cs`
- Added `SubscriptionIdentifiers` (`IReadOnlyList<uint>?`)
- Added `MessageExpiryInterval` (`uint`)
- Updated `FromPacket()` and `ToPacket()` extension methods to include both fields

### `src/TurboMqtt/Client/MqttClientConnectOptions.cs`
- Uncommented TODO block in `LastWillAndTestament`: exposed Will V5 properties
  (ResponseTopic, WillCorrelationData, ContentType, PayloadFormatIndicator,
  DelayInterval, MessageExpiryInterval, WillProperties)
- Added V5 CONNECT properties to `MqttClientConnectOptions`:
  `SessionExpiryInterval`, `TopicAliasMaximum`, `RequestResponseInformation`,
  `RequestProblemInformation` (default=true per MQTT 5.0 spec), `UserProperties`

### `src/TurboMqtt/Client/IMqttClient.cs`
- Added wiring block for MQTT 5.0 CONNECT properties into `ConnectPacket`
- Replaced commented-out will V5 property assignments with live V5-conditional code

### `src/TurboMqtt/IO/DisconnectToBinary.cs`
- Removed `throw new NotSupportedException()` for V5.0
- Added `case MqttProtocolVersion.V5_0:` using `Mqtt5Encoder.EncodePacket`

### `src/TurboMqtt/Client/ClientStreamOwner.cs`
- Added `using System.Diagnostics;` and `using TurboMqtt.Telemetry;`
- Updated `ServerDisconnect` log messages to include `ReasonString`
- Added `EmitServerDisconnectActivity()` helper that creates an
  `mqtt.server_disconnect` Activity with `client.id`, `disconnect.reason_code`,
  and `disconnect.reason_string` tags
- Called `EmitServerDisconnectActivity()` in both genuine `ServerDisconnect` handlers
  (reconnect path and terminal shutdown path)

## Commands Run + Outcomes

```
dotnet build -c Release
→ Build succeeded. 0 Warning(s), 0 Error(s). Time 00:00:08.

dotnet test tests/TurboMqtt.Tests/ -c Release
→ (first run) 1 failure: TcpMqtt311HeartbeatFailureEnd2EndSpecs.ShouldAutomatically...
  Root cause: Address already in use on port 21887 — orphaned dotnet process (PID 213316)
  from earlier test run. Killed process; port freed.

dotnet test tests/TurboMqtt.Tests/ -c Release (second run)
→ Passed! Failed: 0, Passed: 430, Skipped: 0, Total: 430, Duration: 11 s
```

## Deviations / Skips
- Did NOT fix the `Flags` vs `ConnectFlags` duplicate issue in `ConnectPacket` (issue #348) —
  out of scope; pre-existing behavior. V5 connection properties added to `connectPacket`
  directly via separate block, not through the flags initializer.
- `DisconnectToBinary` fix was not in task criteria but fixed anyway because the
  `NotSupportedException` would prevent V5 clients from gracefully disconnecting.

## Follow-ups Noticed (Deferred)
- Issue #348: `ConnectPacket.Flags` vs `ConnectPacket.ConnectFlags` duplicate
  properties. The encoder reads `Flags` but `IMqttClient.ConnectAsync` sets
  `ConnectFlags`. CleanSession/WillFlag/WillQoS bits are therefore not encoded.
  Tracked in #348; affects MQTT 3.1.1 as well.
- TcpTransportActor sends synthetic DISCONNECT encoded with `MqttProtocolVersion.V3_1_1`
  even for V5 connections (hardcoded). Low risk since the DUP flag marks it as synthetic.
  Should be addressed in a transport hardening pass.
- Task 3.9 (E2E container tests) should exercise server-initiated DISCONNECT path.
