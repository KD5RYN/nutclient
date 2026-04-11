# Changelog

All notable changes to NutClient are documented here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Infrastructure
- Updated GitHub Actions to current versions (silences Node 20 deprecation warnings):
  - `actions/checkout` 4 → 6
  - `actions/setup-dotnet` 4 → 5
  - `actions/upload-artifact` 4 → 7
  - `actions/download-artifact` 4 → 8
- Updated test-only NuGet packages:
  - `coverlet.collector` 6.0.0 → 8.0.1
  - `Microsoft.NET.Test.Sdk` 17.8.0 → 18.4.0
  - `xunit` 2.5.3 → 2.9.3
- Added Dependabot ignore rules for `Microsoft.Extensions.Hosting.*` and `Microsoft.Extensions.TimeProvider.*` major bumps (target .NET 10, will revisit on .NET upgrade) and xunit major bumps (xunit 3 has breaking API changes, will upgrade deliberately).

No user-visible changes — the shipped binary is identical to v1.4.0.

## [v1.4.0] - 2026-04-11

Security hardening release. A focused security audit identified one HIGH and several MEDIUM/LOW findings, all addressed in this release. **No critical RCE was present in any prior version.**

### Security
- **F1 (HIGH):** Sanitize NUT `ups.status` before passing to shutdown command. The status string was being concatenated into the command line with simple double-quote wrapping, which is not escaping. A malicious or MITM'd NUT server could break out of the quotes by returning a status like `OB LB" extra-arg "`. Not exploitable in the default config, but became RCE-as-root with `bash -c` style shutdown commands. Now the status is whitelisted to known NUT flag tokens (OL, OB, LB, FSD, CHRG, etc.) before reaching the command line. The internal state machine still sees the raw status, so detection works normally.
- **F2 (MEDIUM):** Lock down `nutclient.json` file permissions. The config file contains the NUT password in plaintext and was being created with default umask (typically 644 on Linux, Users:Read on Windows). Three layers of defense:
  - `install.sh` now runs `chown root:root && chmod 600`
  - `install.ps1` now uses `icacls` to grant only SYSTEM and Administrators
  - NutMonitorService logs a `WARNING` at startup if the config file is group/other-readable on Linux (defense in depth for stale installs)
- **F3 (MEDIUM):** Bounded line reader on the NUT socket. `NutConnection.ReadResponseAsync` was using `StreamReader.ReadLineAsync` with no maximum length, allowing a malicious server to OOM the client or stall every poll. Now reads into an 8 KB bounded buffer and throws `NutException(Transient)` if a line exceeds the limit.
- **F4 (MEDIUM):** Conservative systemd unit hardening. Added 7 directives (`NoNewPrivileges`, `ProtectKernelTunables`, `ProtectKernelModules`, `ProtectControlGroups`, `LockPersonality`, `RestrictRealtime`, `RestrictSUIDSGID`) that block exotic attacks without restricting shutdown scripts. More aggressive directives are included as commented-out opt-in for users with simple shutdown scripts. New "Hardening (Linux)" section in the README explains the tradeoffs.
- **F5/F6 (LOW):** Log and status files now created with 0640 permissions (owner rw, group r, other none) instead of default 0644.
- **F8 (LOW):** Log write failures and rotation failures now surface in journalctl/Event Log instead of being silently swallowed. Throttled to once per failure streak.
- **F12 (LOW):** Pre-shutdown delay now uses `Task.Delay` with cancellation token instead of `Thread.Sleep`. Service stops cleanly even if interrupted during the pre-shutdown wait.

### Added
- `SanitizeUpsStatus()` whitelist function for NUT status flags
- `WarnIfConfigFileIsTooLoose()` runtime check on Linux startup
- `SetSecurePermissions()` helper for log/status files
- `RawGetVarResponse` field on `MockNutServer` for security tests
- "Hardening (Linux)" section in README with optional directives table
- 24 new security tests:
  - 15 tests for `SanitizeUpsStatusTests`
  - 4 tests for bounded line reader (oversized, exact-limit, CRLF, LF)
  - 5 tests already added for startup messaging in v1.3.0 (no change)
- `CONTRIBUTING.md`, `SECURITY.md`, issue templates, PR template, Dependabot config (added pre-1.4 but unreleased until now)
- GitHub repo topics for discoverability
- `CHANGELOG.md` itself

### Changed
- `ExecuteShutdown` is now `ExecuteShutdownAsync` and accepts a `CancellationToken`
- `ProcessPollDecision` and `ProcessStatusDecision` likewise renamed to `Async`
- Removed unused `_reader` (`StreamReader`) field from `NutConnection`

### Fixed
- README clone URL no longer references a placeholder org

### Stats
60 → 84 tests passing.

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

[Unreleased]: https://github.com/KD5RYN/nutclient/compare/v1.4.0...HEAD
[v1.4.0]: https://github.com/KD5RYN/nutclient/releases/tag/v1.4.0
[v1.3.0]: https://github.com/KD5RYN/nutclient/releases/tag/v1.3.0
[v1.2.0]: https://github.com/KD5RYN/nutclient/releases/tag/v1.2.0
[v1.1.0]: https://github.com/KD5RYN/nutclient/releases/tag/v1.1.0
[v1.0.0]: https://github.com/KD5RYN/nutclient/releases/tag/v1.0.0
