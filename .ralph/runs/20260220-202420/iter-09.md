# RALPH Iteration 09

**Date:** 2026-02-20
**Run ID:** 20260220-202420
**Branch:** ralph/claude-20260220-202420

## Task Completed

**Task 3.2: Implement Mqtt5Encoder**

## What Was Done

### New Files
- `src/TurboMqtt/Protocol/Mqtt5Encoder.cs` — Static encoder for all 15 MQTT 5.0 packet types
- `tests/TurboMqtt.Tests/Protocol/Mqtt5EncoderSpecs.cs` — 38 unit tests with hand-computed expected bytes

### IMPLEMENTATION_PLAN.md
All 9 checkboxes for Task 3.2 marked complete.

## Key Design Decisions

**Properties VBI not in estimator:** `MqttPacketSizeEstimator` doesn't include the Properties Length VBI field in its content size. Each encoder method computes `contentSize` inline and uses `estimatedSize.TotalSize` only as a buffer-size lower-bound guard.

**Compact ACK form (OASIS §3.4.2.1):** PubAck/PubRec/PubRel/PubComp use the compact 4-byte form (`Fixed Header + 0x02 + PacketId`) when Reason Code is Success and no properties. Full form otherwise.

**DISCONNECT compact form:** `0xE0 0x00` (2 bytes) when `NormalDisconnection` and no properties.

**6 mandatory CONNECT properties:** Always writes Session Expiry Interval, Receive Maximum, Maximum Packet Size, Topic Alias Maximum, Request Response Information, Request Problem Information (20 bytes total) to match `MqttPacketSizeEstimator` behaviour.

**NonZeroUInt16 struct-field initialization:** When `NonZeroUInt16` is a field of a class (like `MqttLastWill.DelayInterval`), it is default-initialized to Value=0 (zeroed struct), NOT Value=1 (the parameterless ctor result). The parameterless constructor is only invoked by explicit `new NonZeroUInt16()`. This means `WillDelayInterval` is not written unless explicitly set.

## Bugs Fixed in Tests
- `PingReqPacket`/`PingRespPacket` use singleton `Instance` pattern — fixed `new PingReqPacket()` → `PingReqPacket.Instance`
- PUBLISH tests: byte offset off-by-one — 1-character topic sits at bytes[4], so props VBI was at bytes[5] not bytes[4]
- CONNECT+will test: expected `willPropsSize=5` but struct-field default gives `DelayInterval.Value=0` → `willPropsSize=0` → corrected assertion to `0x00`

## Test Results
- `dotnet build src/TurboMqtt/ -c Release` → 0 warnings
- `dotnet test tests/TurboMqtt.Tests/ -c Release` → 350/350 pass (38 new Mqtt5Encoder tests)
