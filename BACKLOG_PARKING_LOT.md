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
