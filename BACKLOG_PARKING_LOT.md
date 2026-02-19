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
