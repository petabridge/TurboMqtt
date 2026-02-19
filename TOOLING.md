# Tooling

## Repository

- **Upstream**: [petabridge/TurboMqtt](https://github.com/petabridge/TurboMqtt) - PRs always target this repo's `dev` branch
- **Default branch**: `dev`

## Build

| Tool | Version | Access | Purpose |
|------|---------|--------|---------|
| .NET SDK | 8.0.400 (pinned in `global.json`, rollForward: latestMinor) | `dotnet` | Build, test, pack |
| `build.ps1` | - | `pwsh build.ps1` | Extracts version + release notes from RELEASE_NOTES.md, updates Directory.Build.props |
| `SignClient` | 1.2.109 | Local tool (`.config/dotnet-tools.json`) | NuGet package code signing (**deprecated** - migrating to `dotnet sign`, see #326) |

## Package Management

- **Central Package Management** via `Directory.Packages.props`
- **Shared build properties** via `Directory.Build.props`
- **NuGet source**: nuget.org only (`nuget.config` with package source mapping)
- Use `dotnet add/remove/list package` commands; do not edit XML directly

## Testing

| Tool | Purpose | Notes |
|------|---------|-------|
| xUnit 2.9.2 | Test framework | All test projects |
| Akka.Hosting.TestKit | Actor testing | For actor-based specs |
| FsCheck 2.16.6 | Property-based testing | Codec fuzzing |
| FluentAssertions 6.12.1 | Assertions | |
| TestContainers 4.1.0 | Container-based E2E tests | EMQX, NanoMQ brokers |
| Coverlet 6.0.3 | Code coverage collection | XPlat format in CI |

### Running Tests

```bash
# Unit + integration tests (no Docker required)
dotnet test tests/TurboMqtt.Tests/

# Container-based E2E tests (requires Docker)
dotnet test tests/TurboMqtt.Container.Tests/
```

## Benchmarking

| Tool | Purpose |
|------|---------|
| BenchmarkDotNet 0.14.0 | Performance measurement |
| `start-emqx.ps1` | Start EMQX Docker container for E2E benchmarks |
| `stop-eqmx.ps1` | Stop EMQX container |

```bash
# Run benchmarks (EMQX must be running for E2E benchmarks)
dotnet run -c Release --project benchmarks/TurboMqtt.Benchmarks/
```

## CI/CD

| System | Trigger | Purpose |
|--------|---------|---------|
| GitHub Actions (`pr_validation.yaml`) | Push / PR to dev, main, master | Build + test (Ubuntu + Windows), code coverage |
| Azure DevOps (`build_release.yaml`) | Git tag push | **Being replaced** by GitHub Actions (see #326) |
| Dependabot | Daily at 11:00 UTC | NuGet + GitHub Actions dependency updates |

## Source Control

| Tool | Version | Purpose |
|------|---------|---------|
| GitHub CLI | 2.76.1 | Issues, PRs, releases, API |
| Docker | 28.3.3 | EMQX/NanoMQ broker containers |
