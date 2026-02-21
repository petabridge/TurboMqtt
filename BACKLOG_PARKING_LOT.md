# Backlog Parking Lot

> Items parked here need a human decision before they can be worked on.
> RALPH loops do NOT pick up items from this file -- only from `IMPLEMENTATION_PLAN.md`.
>
> Each item includes: what it is, where it came from, and what decision is needed.

---

## Items Awaiting Decision

### Hardcoded port 21883 in TcpMqtt311End2EndSpecs

**Source:** Adversarial review after iter-05, finding F-3 (run 20260221-053113)
**Description:** `TcpMqtt311End2EndSpecs` still hardcodes port 21883 in multiple places. Same class of bug as Task 4.4 (flaky HeartbeatFailure test with hardcoded port 21887, fixed in commit 391cace). Should switch to ephemeral port (port 0) + `server.BoundPort` for consistency.
**Decision needed:** File a GitHub issue and schedule as a low-priority fix, or batch with other test infrastructure improvements.

### CheckTimeout handler modifies dictionary during foreach iteration

**Source:** Adversarial review after iter-10, finding F-7 (run 20260221-053113); also noted in iter-08 flight recorder.
**Description:** Both `AtLeastOncePublishRetryActor.cs:186` and `ExactlyOncePublishRetryActor.cs:263` call `_pendingPackets.Remove(packetId, out _)` inside a `foreach` loop over `_pendingPackets`. In .NET, removing a dictionary entry during enumeration throws `InvalidOperationException` on the next `MoveNext()` if there are remaining items. In practice this is rare (usually only one packet times out per tick) but theoretically possible with multiple simultaneous timeouts.
**Fix:** Collect timed-out packet IDs in a `List<NonZeroUInt16>` first, then iterate the list to remove/notify after the foreach completes.
**Decision needed:** File a GitHub issue and schedule as a correctness fix. Low priority since the timeout tick is every 1 second and multiple simultaneous overdue packets are rare.

### Flaky SharedQuota_cross_Qos_drain test under parallel load

**Source:** Adversarial review after iter-15, finding F-11 (run 20260221-053113); first observed in iter-15 flight recorder.
**Description:** `SharedReceiveMaximumQuotaSpecs.SharedQuota_cross_Qos_drain_when_Qos2_frees_slot_and_Qos1_has_buffer` fails intermittently in full parallel suite runs (1 failure out of multiple runs) but passes consistently in isolation. The test uses `await Task.Delay(80, cts.Token)` + `TryRead().Should().BeFalse()` to assert a message was buffered — same class of timing-sensitive pattern as the DoDisconnect polling race fixed in Task 5.2.1. Pre-existing from Task 4.8.
**Fix:** Replace `Task.Delay(80)` + `TryRead` negative assertion with a more deterministic approach (e.g., a `WaitAsync` with short timeout that expects no result, or use Akka EventFilter on actor logs to confirm buffering).
**Decision needed:** File a GitHub issue and schedule as a low-priority test stabilization fix. Failure is rare and does not indicate a production bug.
