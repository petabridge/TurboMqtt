# Iteration 05 — Task 2.8: MQTT 3.1.1 E2E Tests with Authentication

## Summary

Implemented Task 2.8: Add MQTT 3.1.1 E2E tests with authentication enabled.

**Files created:**
- `tests/TurboMqtt.Container.Tests/EmqxAuthFixture.cs`
- `tests/TurboMqtt.Container.Tests/End2End/EmqxMqtt311AuthEnd2EndSpecs.cs`

**Result:** 4/4 new tests pass; 19/19 total container tests pass.

---

## Technical Notes

### EMQX 5.5.1 API Key Bootstrap

Several issues were encountered diagnosing the correct bootstrap mechanism:

**Wrong env var:** `EMQX_MANAGEMENT__API_KEY__BOOTSTRAP_FILE` does NOT work.
The correct env var is `EMQX_API_KEY__BOOTSTRAP_FILE` (root-level config path `[api_key, bootstrap_file]`).
Verified via: `emqx eval "io:format(\"~p~n\", [emqx:get_config([api_key], undefined)])."` → `#{bootstrap_file => <<>>}`.

**Wrong file format:** `AppID:ApiKey:ApiSecret` (3-field with app ID first) does NOT work.
Correct format is `ApiKey:ApiSecret:Role` per line (e.g., `emqx-test-api-key:emqx-test-api-secret-1234:administrator`).
Verified via Erlang beam analysis and live container testing.

**`WithResourceMapping(string, string)` pitfall:** In TestContainers 4.10.0 this overload treats the first argument as a HOST FILE PATH, not file content. Use `Path.GetTempFileName()` + `File.WriteAllText()` + `WithBindMount(hostPath, containerPath)` instead.

**HTTP Basic auth:** `Authorization: Basic base64(ApiKey:ApiSecret)` — not dashboard credentials.

### Reconnect Safety for Failed Auth Test

The `ShouldRejectConnectionWithInvalidPassword` test relies on the client NOT reconnecting after a CONNACK rejection. This is safe because:
- `ClientStreamOwner`: when `!_successfullyConnected`, `StreamTerminated` does not trigger reconnect.
- `ClientAcksActor`: `ConnAckReasonCode > Success` → `ConnectFailure` (reason code 4 = `BadUserNameOrPassword`).
- EMQX built-in database authenticator: returns definitive `deny` when user exists but password is wrong.

The test observes `connectResult.IsSuccess == false` without the client retrying.

---

## Test Results

```
Total tests: 4 (auth suite)
     Passed: 4
 Total time: 9.7233 Seconds

Total tests: 19 (full container suite)
     Passed: 19
 Total time: 10.0997 Seconds
```
