# RALPH Iteration 01 — Run 20260221-020516

## Task Selected

**Task 3.4: Add FsCheck generators and roundtrip property tests for MQTT 5.0**

Phase 3 / MQTT 5.0 Implementation. First incomplete task in IMPLEMENTATION_PLAN.md.

## Surface Area Classification

**Domain** — pure codec/protocol layer. New test files only; no runtime behavior changes.
The `MqttPacketSizeEstimator.cs` modification (fixing `=` vs `+=` bug and adding missing
Properties VBI bytes) was pre-staged by the previous RALPH run but not committed.

## Verification Level

**L1** — unit tests only. No I/O, no actors, no Docker.
Reason: Task is entirely codec + FsCheck; L2+ not required.

## Skills Consulted

- CLAUDE.md Quality Bar (property-based tests required for codec/protocol logic)
- TOOLING.md (dotnet test command)

## Files Changed

### New (untracked → committed)
- `tests/TurboMqtt.Tests/Mqtt5PacketGenerators.cs` — FsCheck generators for all 15 MQTT 5.0 packet types
- `tests/TurboMqtt.Tests/Protocol/Mqtt5RoundtripPropertyTests.cs` — per-type + combined roundtrip property tests
- `tests/TurboMqtt.Tests/Protocol/Mqtt5DecoderErrorPathSpecs.cs` — error path and boundary condition tests

### Modified
- `src/TurboMqtt/Protocol/MqttPacketSizeEstimator.cs` — Fixed bugs: (1) `=` instead of `+=` for user properties
  size (#349), (2) missing VBI byte for properties length in CONNACK/CONNECT
- `tests/TurboMqtt.Tests/Packets/ConnAck/ConnAckPacketSpecs.cs` — Updated expected sizes to match corrected estimator:
  no-props CONNACK: 2→3, with-props CONNACK: 32→33
- `tests/TurboMqtt.Tests/Packets/Connect/ConnectPacketMqtt5Specs.cs` — Updated expected sizes:
  basic: 40→41, with-props: 50→71, with-will: 75→100
- `tests/TurboMqtt.Tests/Protocol/Mqtt5RoundtripPropertyTests.cs` — Fixed PUBLISH roundtrip test:
  added `.Excluding(x => x.PacketId)` from BeEquivalentTo (PacketId not encoded for QoS 0)
- `IMPLEMENTATION_PLAN.md` — Task 3.4 checkboxes checked

## Commands Run + Outcomes

```
dotnet test tests/TurboMqtt.Tests/ -c Release --filter "FullyQualifiedName~Mqtt5" --no-build
→ Failed: 7 (size estimation specs + PUBLISH roundtrip), Passed: 153

# Root cause analysis:
# 1. Old size estimation tests expected values based on buggy estimator
# 2. PUBLISH roundtrip BeEquivalentTo compared PacketId even for QoS 0

dotnet build tests/TurboMqtt.Tests/ -c Release
→ Build succeeded, 0 warnings

dotnet test tests/TurboMqtt.Tests/ -c Release --no-build --filter "FullyQualifiedName!~TcpMqtt311HeartbeatFailure"
→ Passed: 409, Failed: 0

# Verified heartbeat test is pre-existing failure (port conflict on machine):
git stash && dotnet test ... --filter "TcpMqtt311HeartbeatFailure" → Failed (same error before our changes)
git stash pop
```

## Deviations / Skips

- Heartbeat test excluded from final pass count. Failure is "Address already in use" —
  pre-existing environment issue; confirmed by reverting all changes and re-running.
  Not caused by our modifications.

## Findings

### Why the size estimation tests needed updating

The CONNECT and CONNACK size estimation tests were written against the OLD (buggy) estimator which:
1. Used `=` instead of `+=` for user properties size (issue #349) — replaced the base 20 bytes
   with just user-prop bytes
2. Did NOT include the 1-byte VBI encoding of the Properties Length in contentSize

The new estimator correctly mirrors `Mqtt5Encoder`'s `ComputeConnectContentSize` /
`ComputeConnAckPropertiesSize` logic, adding the VBI byte and fixing the `+=` assignment.

### Why the PUBLISH roundtrip test needed fixing

`BeEquivalentTo` performs structural equality on ALL properties by default. `PacketId` was
not excluded from the comparison, so QoS 0 packets (where PacketId is not encoded/decoded)
would show a mismatch (original: 30488, decoded: 0). Fix: add `.Excluding(x => x.PacketId)`.

## Follow-ups Noticed But Deferred

- Task 3.5 (Wire Mqtt5Encoder/Decoder into Akka.Streams pipeline) is the next task.
- The heartbeat test port conflict should eventually be fixed (tracked in #99 area as
  test infrastructure issue), but is pre-existing and out of scope.
