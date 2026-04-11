# Changelog

All notable changes to NutClient are documented here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- `CONTRIBUTING.md` with build, test, and PR conventions
- `SECURITY.md` for vulnerability reporting via GitHub Security Advisories
- Issue templates (bug report, feature request) and PR template
- Dependabot config to auto-update NuGet packages and GitHub Actions weekly
- This CHANGELOG file
- GitHub repo topics for discoverability

### Fixed
- README clone URL no longer references a placeholder org

## [v1.3.0] - 2026-04-11

### Added
- Startup messages: "Cannot reach NUT server yet — will keep trying" on first failure with no prior connection, and "Successfully connected to NUT server for the first time" on first success. Gives admins clear feedback on startup.
- CI workflow (`.github/workflows/ci.yml`) runs all 60 tests on every push and PR, not just on releases
- 5 new tests covering startup messaging behavior

### Changed
- Windows service now retries forever after the initial backoff: 10s → 30s → 5min → 5min → 5min... (was: gave up after 3 retries)
- README install instructions now show editing config **after** running install script, matching what install.sh actually does
- README clarifies that `install.sh` already handles `chmod +x` for the example shutdown script

## [v1.2.0] - 2026-04-11

### Changed
- Sanitized config templates and docs — removed personal hostnames, usernames, passwords, and test environment details. Templates now use placeholders (`your-nut-server.example.com`, `monuser`, `CHANGE_ME`).

### Added
- MIT License

## [v1.1.0] - 2026-04-11

### Added
- `ShutdownDelaySeconds: 0` disables the timer entirely. Use this with `BatteryChargePercent` or `BatteryRuntimeSeconds` for threshold-only shutdown strategies.
- `install.sh` Linux distro compatibility notes and early `systemctl` check
- 3 new tests covering disabled-timer scenarios

## [v1.0.0] - 2026-04-10

Initial public release.

### Features
- Cross-platform .NET 8 service (Windows + Linux)
- Polls NUT server every 5 seconds (configurable)
- Shutdown triggers:
  - Timer expiry on battery (60s default)
  - Low Battery (`LB`) — immediate
  - Forced Shutdown (`FSD`) — immediate
  - Battery charge threshold (`BatteryChargePercent`)
  - Battery runtime threshold (`BatteryRuntimeSeconds`)
  - Dead time (server unreachable while on battery)
- Pre-shutdown hook with configurable delay
- Shutdown reason and UPS data passed to script as arguments
- Status file (`nutclient-status.json`) with rolling 25-entry history
- Log rotation at configurable size
- LogLevel: `events` (quiet) or `all` (every poll)
- Exponential backoff on connection failures
- Typed errors (Transient/AccessDenied/Protocol)
- Install scripts for Linux (`install.sh`) and Windows (`install.ps1`)
- Pre-built releases for `win-x64`, `linux-x64`, `linux-arm64`
- 52 unit and integration tests with mock NUT server

[Unreleased]: https://github.com/KD5RYN/nutclient/compare/v1.3.0...HEAD
[v1.3.0]: https://github.com/KD5RYN/nutclient/releases/tag/v1.3.0
[v1.2.0]: https://github.com/KD5RYN/nutclient/releases/tag/v1.2.0
[v1.1.0]: https://github.com/KD5RYN/nutclient/releases/tag/v1.1.0
[v1.0.0]: https://github.com/KD5RYN/nutclient/releases/tag/v1.0.0
