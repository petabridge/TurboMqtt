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

---

*No active tasks. Plan exhausted.*
