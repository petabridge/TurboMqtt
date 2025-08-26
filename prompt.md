TurboMqtt aims to be a high-performance MQTT library, but it has a number of known stability problems with the MQTT 3.1.1 implementation:

1. Inconsistent handling of connection retries
2. Decoding problems
3. Performance issues, possibly
4. Numerous failing PRs on the main repository: https://github.com/petabridge/TurboMqtt

You are working locally off of my fork of this repository. 

Your goals:

- [x] Upgrade to the latest version of Akka.NET (1.5.37 → 1.5.48)
- [x] Find any racy test failures (see if there are any on the open pull requests) 
  - No racy failures detected after multiple test runs
  - One pre-existing test failure (ShouldHandleMultipleMessagesWithPartialFrame)
- [x] Find issues with the MQTT decoding pipeline and simplify
  - Fixed critical off-by-one buffer copy bug in MqttDecodingFlows
  - Implemented ImmutableList.Builder reducing allocations from O(n²) to O(n)
  - Added MQTT max packet size validation (268MB limit)
- [x] See if we can simplify the MQTT transport pipeline and improve network connection handling
  - Fixed race conditions in TcpTransportActor task coordination
  - Implemented atomic state transitions using Interlocked operations
  - Added proper graceful shutdown with timeout handling
- [ ] Maximize performance on the MQTT 3.1.1 transmission pipeline

## Critical Issues Fixed:
1. **Buffer Copy Bug (MqttDecodingFlows:98)**: Off-by-one error causing data corruption - FIXED
2. **Race Conditions (TcpTransportActor)**: Fire-and-forget tasks without coordination - FIXED
3. **Memory Allocations**: Decoder O(n²) allocations with ImmutableList.Add() - FIXED
4. **Missing Packet Size Validation**: No protection against oversized packets - FIXED

## Known Issues Still Present:
1. **Container Tests Failing**: Need Docker environment to test
2. **Test Bug**: ShouldHandleMultipleMessagesWithPartialFrame has frame overlap issue (test problem, not code)
3. **Buffer Pooling Not Implemented**: Decoder still allocates for fragmented packets

* Add any new issues or bugs you encounter to this file.
* Mark off any completed issues to this file. 
* Never commit this file (`prompt.md`) to the repository. 
* Each time you successfully complete a change, commit it without gpg signing but never push anything to any remotes.

You may have to run the test suites many times in order to reproduce racy test failures locally - aggressively look for and fix those. 

Look at expanding our use of FsCheck / other property-based testing libraries in .NET to drum out error sources from our encoding / decoding pipelines.

If you suspect there are Akka.NET bugs or performance issues caused by it - save those as memories to memorizer.