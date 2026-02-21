# RALPH Iteration 7 — Flight Recorder

## Task Selected
**Task 3.10: Add MQTT 5.0 E2E container tests with authentication**

## Surface Area Classification
Cross-cutting — new container test file, no source code changes.

## Verification Level
**L2** — Container tests against real EMQX broker with TestContainers.
Rationale: integration with a real broker (EMQX in Docker) is required to verify authentication enforcement. No mocking is appropriate here.

## Skills Consulted
- `EmqxMqtt311AuthEnd2EndSpecs.cs` — MQTT 3.1.1 auth test patterns (mirrored for V5)
- `EmqxMqtt5End2EndSpecs.cs` — MQTT 5.0 anonymous test patterns
- `EmqxAuthFixture.cs` — existing fixture with `EmqxAuthCollection`, `ValidUserName`, `ValidPassword`

## Approach
Created `tests/TurboMqtt.Container.Tests/End2End/EmqxMqtt5AuthEnd2EndSpecs.cs`:
- Reuses existing `EmqxAuthFixture` (EMQX with `allow_anonymous=false` + built-in database authenticator)
- Uses `MqttProtocolVersion.V5_0` with `ValidUserName`/`ValidPassword` credentials
- 4 tests covering all Done-when criteria:
  1. `ShouldConnectWithValidCredentials` — V5 connect with valid creds succeeds
  2. `ShouldRejectConnectionWithInvalidPassword` — V5 connect with wrong password is rejected
  3. `ShouldPublishAndSubscribeWithAuth_QoS0` — pub/sub QoS 0 over authenticated V5 connection
  4. `ShouldPublishAndSubscribeWithAuth_QoS1` — pub/sub QoS 1 over authenticated V5 connection

## Commands Run + Outcomes

```
dotnet build tests/TurboMqtt.Container.Tests/ -c Release
  => Build succeeded. 0 Warning(s). 0 Error(s).

dotnet test tests/TurboMqtt.Container.Tests/ -c Release --no-build -v normal
  => Test Run Successful. Total tests: 30. Passed: 30.
  New tests:
    Passed EmqxMqtt5AuthEnd2EndSpecs.ShouldConnectWithValidCredentials [9 ms]
    Passed EmqxMqtt5AuthEnd2EndSpecs.ShouldRejectConnectionWithInvalidPassword [23 ms]
    Passed EmqxMqtt5AuthEnd2EndSpecs.ShouldPublishAndSubscribeWithAuth_QoS0 [105 ms]
    Passed EmqxMqtt5AuthEnd2EndSpecs.ShouldPublishAndSubscribeWithAuth_QoS1 [16 ms]
  Previous count: 26. New count: 30 (delta: +4, all new tests pass).
```

## Deviations / Skips
None. All Done-when criteria satisfied exactly as specified.

## Follow-ups Noticed (Deferred)
- Task 3.11 (MQTT 5.0 TCP benchmarks) is next; requires separate work session.
- Task 3.12 (MQTT 5.0 TLS benchmarks) depends on 3.11.
