# RALPH Iteration 07 — Flight Recorder

**Run ID:** 20260220-202420
**Date:** 2026-02-20
**Branch:** ralph/claude-20260220-202420

---

## Task Selected

**Task 3.0: Build MQTT 5.0 property encoding/decoding infrastructure**

PRD: docs/prd/mqtt5/README.md §1
Surface area: **domain**
Implementation Plan status: first incomplete task in Phase 3

---

## Surface Area Classification

Domain only — new static helper classes in `src/TurboMqtt/Protocol/`. No actors, no streams, no network I/O.

---

## Verification Level

**L1** — Unit tests only (no Docker/external infrastructure).
Rationale: pure codec infrastructure, no I/O coordination.

---

## Skills Consulted

- No `.claude/skills/` directory exists in this repo; constitution (CLAUDE.md) consulted directly.
- MQTT 5.0 PRD at `docs/prd/mqtt5/README.md §1` for property type table and method signatures.
- Existing patterns: `Mqtt311Encoder.cs` (ref Span pattern), `Mqtt311Decoder.cs` (ReadOnlySpan advancement), `MqttDecoderException.cs` (error type).

---

## Implementation Summary

Created three new source files and one test file:

### `src/TurboMqtt/Protocol/Mqtt5PropertyIdentifiers.cs`
- 27 constants covering all MQTT 5.0 property identifiers from OASIS spec Table 2-4
- Organized by type: Byte (8), Two Byte Integer (4, includes 0x13 ServerKeepAlive), Four Byte Integer (4), Variable Byte Integer (1), UTF-8 String (7), Binary Data (2), UTF-8 String Pair (1)
- Note: PRD says "28 identifiers" but OASIS spec Table 2-4 has 27 distinct identifiers; the PRD table lists 0x09 in both UTF-8 String and Binary Data rows (a PRD error); implemented the correct 27.

### `src/TurboMqtt/Protocol/Mqtt5PropertyWriter.cs`
- `WriteByte(ref Span<byte>, byte id, byte value)` → 2 bytes
- `WriteTwoByteInt(ref Span<byte>, byte id, ushort value)` → 3 bytes
- `WriteFourByteInt(ref Span<byte>, byte id, uint value)` → 5 bytes
- `WriteVariableByteInt(ref Span<byte>, byte id, uint value)` → 2–5 bytes
- `WriteUtf8String(ref Span<byte>, byte id, string value)` → 3 + byteLen bytes
- `WriteStringPair(ref Span<byte>, string key, string value)` → 5 + keyLen + valLen bytes (always writes 0x26 id)
- `WriteBinaryData(ref Span<byte>, byte id, ReadOnlySpan<byte> data)` → 3 + dataLen bytes
- `GetVariableByteIntSize(uint value)` helper (same boundary rules as existing MQTT length encoder)
- `EncodeVariableByteInt(ref Span<byte>, uint value)` internal helper

### `src/TurboMqtt/Protocol/Mqtt5PropertyReader.cs`
- `ReadByte(ref ReadOnlySpan<byte>)` → byte (throws `MqttDecoderException` on short buffer)
- `ReadTwoByteInt(ref ReadOnlySpan<byte>)` → ushort
- `ReadFourByteInt(ref ReadOnlySpan<byte>)` → uint
- `TryReadVariableByteInt(ref ReadOnlySpan<byte>, out uint)` → bool (safe Try pattern for VBI)
- `ReadUtf8String(ref ReadOnlySpan<byte>)` → string
- `ReadStringPair(ref ReadOnlySpan<byte>)` → `(string Key, string Value)`
- `ReadBinaryData(ref ReadOnlySpan<byte>)` → `ReadOnlyMemory<byte>`
- `ThrowUnknownPropertyIdentifier(byte id)` → always throws `MqttDecoderException` per MQTT 5.0 §2.2.2.2

### `tests/TurboMqtt.Tests/Protocol/Mqtt5PropertyRoundtripTests.cs`
- 44 tests (27 unit + 7 FsCheck properties × ~100 iterations)
- Covers: Byte, TwoByteInt, FourByteInt, VBI (8 boundary values), UTF-8 String (5 edge cases), StringPair (2 cases), BinaryData (3 cases)
- `GetVariableByteIntSize` boundary checks (8 cases)
- Unknown property identifier → `MqttDecoderException`
- Buffer-too-short guards for all typed readers (4 tests via try/catch — ref struct cannot be captured in lambdas)
- `TryReadVariableByteInt` with empty buffer and 5-byte encoding
- FsCheck: RandomByte, RandomTwoByteInt, RandomFourByteInt, RandomVariableByteInt, RandomUtf8String, RandomBinaryData, RandomStringPair

---

## Commands Run + Outcomes

```
dotnet build -c Release tests/TurboMqtt.Tests/
→ Build succeeded. 0 Warnings. 0 Errors. (first attempt had AsReadOnly + ref-in-lambda errors; fixed)

dotnet test tests/TurboMqtt.Tests/ -c Release --filter "FullyQualifiedName~Mqtt5Property"
→ Passed: 44, Failed: 0

dotnet test tests/TurboMqtt.Tests/ -c Release
→ Passed: 312, Failed: 0, Skipped: 0 (zero regressions)
```

---

## Build Issues Encountered + Fixes

1. **`Span<T>.AsReadOnly()` not available**: Replaced all `.AsReadOnly()` calls with `new ReadOnlySpan<byte>(array, offset, length)` constructor pattern. (`AsReadOnly` was added much later and isn't consistently available.)

2. **`ref local` in lambda (CS8175)**: `ReadOnlySpan<byte>` is a ref struct and cannot be captured in FluentAssertions `() => {}` lambdas. Fixed by:
   - Moving FsCheck property tests to use private helper methods (ByteRoundtrip, TwoByteIntRoundtrip, etc.)
   - Converting buffer-guard tests from `act.Should().Throw<>()` to explicit try/catch blocks

---

## Deviations / Skips

- **27 vs 28 identifiers**: PRD claims "28 property identifiers" but OASIS MQTT 5.0 Table 2-4 has 27 distinct ones. Implemented all 27 correct ones. (The PRD table inadvertently lists 0x09 CorrelationData in both UTF-8 String and Binary Data rows.) Not a deviation — the implementation is correct per spec.

---

## Follow-ups Noticed (Deferred)

- The `MqttPacketSizeEstimator` has a pre-existing bug in `EstimateAuthPacketSizeMqtt5`: `AuthenticationMethod` and `AuthenticationData` size calculations are missing the 1-byte property identifier overhead. These will surface when Task 3.2 (Mqtt5Encoder) is implemented. Deferring per scope of Task 3.0.
- Task 3.1 (Add missing MQTT 5.0 fields to ConnAckPacket) is the next immediate task in Phase 3 and can be started next iteration.
