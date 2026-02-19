# TurboMqtt Agent Constitution

TurboMqtt is a high-performance, streaming-first MQTT client library for .NET built on Akka.NET and Akka.Streams. The goal is to be the fastest MQTT client in the .NET ecosystem.

See [PROJECT_CONTEXT.md](PROJECT_CONTEXT.md) for architecture, roadmap, and current state.
See [TOOLING.md](TOOLING.md) for build, test, benchmark, and CI/CD instructions.

## Task Routing

Before acting, identify the work mode:

| Mode | Signals | Outputs |
|------|---------|---------|
| **Engineering** | Code in `src/`, `tests/`, `benchmarks/`, `samples/`; bugs; features; performance | Code + tests; benchmarks for perf work |
| **Release** | Version bumps, RELEASE_NOTES.md, tags, publishing | Updated version, release notes, tag |

## Build and Test

```bash
# Build
dotnet build -c Release

# Test (unit + integration, no Docker)
dotnet test tests/TurboMqtt.Tests/ -c Release

# Test (E2E with real broker, requires Docker)
dotnet test tests/TurboMqtt.Container.Tests/ -c Release

# Pack
dotnet pack -c Release
```

All code must compile with zero warnings (`TreatWarningsAsErrors` is on).

## Quality Bar

### All Code Changes

- Tests pass on both Linux and Windows
- No new compiler warnings
- Property-based tests (FsCheck) for any codec or protocol logic
- Akka.Hosting.TestKit for actor behavior tests

### Performance-Sensitive Changes

- Run relevant BenchmarkDotNet benchmarks before and after
- No throughput regressions on the MQTT 3.1.1 pipeline
- Prefer zero-allocation patterns: `ReadOnlyMemory<byte>`, `Span<T>`, `MemoryPool<byte>`, `IMemoryOwner<byte>`
- Use `System.Threading.Channels` over locks for concurrency
- Batch operations where possible (see `BatchWeighted` in encoding flows)

### Protocol Changes

- Validate against real brokers via container tests (EMQX)
- Cover all three QoS levels (0, 1, 2)
- Test partial frame handling and packet boundary edge cases

## Coding Conventions

- File-scoped namespaces, implicit usings, nullable reference types enabled
- C# latest language version
- Central Package Management: use `dotnet add package` (never edit csproj/props XML directly)
- Akka.NET actors use `ReceiveActor` or `UntypedActor` patterns; hosting via `Akka.Hosting`
- Internal types exposed to test projects via `InternalsVisibleTo` in `Properties/Friends.cs`

## Key Architectural Rules

- **Akka.NET is the concurrency model.** Do not introduce raw `Task.Run`, `Thread`, or manual synchronization unless there is no actor-based alternative.
- **Akka.Streams is the data pipeline.** Encode, decode, and receive flows are stream stages. Do not bypass streams for packet processing.
- **Channels bridge actors to user code.** `System.Threading.Channels` is the public-facing async interface; actors and streams are internal.
- **Memory is pooled.** Use `MemoryPool<byte>` for buffers. Dispose `IMemoryOwner<byte>` when done. Never copy payload bytes unnecessarily.

## Versioning and Releases

- Version is in `Directory.Build.props` (`VersionPrefix`)
- Release notes go in `RELEASE_NOTES.md` using `#### VERSION DATE ####` format
- `build.ps1` extracts version + notes and patches Directory.Build.props
- Releases are triggered by pushing a git tag; GitHub Actions handles signing and publishing (migration from Azure DevOps tracked in #326)

## Skills Index

Use these skills by name when the task matches. Invoke with `/skill-name`.
Skills prefixed with `dotnet-skills:` come from the [petabridge/dotnet-skills](https://github.com/petabridge/dotnet-skills) marketplace. If a skill is not installed, add it via Claude Code's `/install-skill` or the dotnet-skills README.

### Akka.NET & Architecture
`akka-best-practices` actors, supervision, error handling, work distribution
`akka-testing-patterns` Akka.Hosting.TestKit, TestProbes, persistence testing
`analyze-racy-test` flaky tests, race conditions, timing-dependent failures

### C# & API Design
`csharp-coding-standards` records, pattern matching, Span/Memory, async/await
`csharp-concurrency-patterns` Channels, async, choosing concurrency abstractions
`csharp-api-design` public API compat, versioning, extend-only design
`csharp-type-design-performance` sealed classes, readonly structs, collection choices

### Performance & Benchmarking
`create-benchmark` design new BenchmarkDotNet benchmarks
`run-benchmark` execute benchmarks and capture results
`analyze-performance` analyze benchmark results, profiler data, regressions
`benchmark-workflow` coordinated run + analyze workflow

### Testing & Quality
`testcontainers` E2E tests with real brokers in Docker
`crap-analysis` code coverage + CRAP score risk hotspots
`slopwatch` detect reward hacking in code changes

### Project & Packages
`project-structure` Directory.Build.props, CPM, SourceLink, versioning
`package-management` NuGet CPM, dotnet add/remove/list

### PR & Release
`check-api-breaking` API breaking change detection
`review-pr` comprehensive pull request review
`create-release` release notes, git tags, publish workflow

## Continuous Improvement

- If a workflow is repeated 3+ times, extract it into a repo skill or script
- Volatile project knowledge belongs in `PROJECT_CONTEXT.md` or `TOOLING.md`, not here
- This file should remain compact and stable; update only for governance changes
