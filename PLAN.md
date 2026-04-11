# NutClient Development Plan

## Context

Custom .NET 8 NUT client for graceful server shutdown on UPS power loss. Designed as a cross-platform replacement for the broken NUT-for-Windows build and the retired WinNUT-Client. Runs as a Windows service or Linux systemd service.

## Architecture

```
  NUT Server                         NutClient (each server)
 ┌──────────────┐                   ┌──────────────────────────┐
 │ upsd :3493   │◄── TCP poll ──────│ NutClient                │
 │              │    every 5s       │                          │
 └──────────────┘                   │ On battery → 60s timer   │
                                    │ Timer expires → script   │
                                    │ Low battery → immediate  │
                                    │ FSD → immediate          │
                                    │ Dead time → shutdown     │
                                    │ Power restored → cancel  │
                                    └──────────────────────────┘
```

---

## Phase 1: Development — DONE

- [x] Cross-platform .NET 8 app (Windows + Linux)
- [x] Polls ups.status, battery.charge, battery.runtime, input.voltage, ups.load
- [x] Shutdown triggers: OB timer, LB, FSD, battery charge/runtime thresholds
- [x] Disable timer with `ShutdownDelaySeconds: 0` for threshold-only shutdown
- [x] Dead time safety — shuts down if server unreachable while on battery
- [x] Pre-shutdown hook — optional command before main shutdown
- [x] Shutdown reason + UPS data passed as script arguments
- [x] Error handling — typed errors, exponential backoff
- [x] Startup messaging — "Cannot reach NUT server yet" and "Successfully connected for the first time"
- [x] Status file (nutclient-status.json) with 25-entry rolling history
- [x] Log rotation with configurable size
- [x] LogLevel config: "events" (default, quiet) vs "all" (every poll)
- [x] Example shutdown scripts (PowerShell + bash) with param blocks
- [x] State machine extracted for testability
- [x] **60 unit/integration tests** with mock NUT server
- [x] Full README with setup, config, developer docs
- [x] Install scripts for Linux and Windows (preserve existing config on upgrade)
- [x] Linux install: clear distro compatibility notes (Debian-tested, systemd check)
- [x] Windows service: retries forever (10s, 30s, 5min, then every 5min)
- [x] Linux systemd: `Restart=always` with 10s delay
- [x] GitHub Actions CI — auto-build + release on tag push (win-x64, linux-x64, linux-arm64)
- [x] CI workflow runs tests on every push and PR
- [x] Sanitized config templates and docs (no personal info)
- [x] MIT License
- [x] **v1.2.0 release published** (latest, sanitized)

---

## Phase 2: Testing with Dedicated Test UPS — DONE

Test setup: Raspberry Pi running NUT with APC Back-UPS ES 850G2 plus a `dummy-ups` driver for simulating arbitrary status values.

- [x] **2.1** Set up test NUT server (Raspberry Pi + APC Back-UPS)
- [x] **2.2** Verify polling in console mode
- [x] **2.3** Install as systemd service on Linux, verify auto-start on reboot
- [x] **2.4** Install as Windows service on jldev, verify auto-start on reboot
- [x] **2.5** Simulate power loss — verified 60s countdown and shutdown with correct args
- [x] **2.6** Test power flicker — verified shutdown cancels on restore
- [x] **2.6.1** Set up `dummy-ups` driver on test NUT server for safe simulation of LB/FSD/dead time
- [x] **2.7** Test LB/FSD — verified immediate shutdown for both `OB LB` and `FSD` (no 60s wait)
- [x] **2.8** Test dead time — verified shutdown 30s after server unreachable while on battery. Confirmed regular timer pauses when polls fail (timer check only runs in HandleStatus on successful poll), making dead time the essential safety net for network outages during power failures.

---

## Phase 3: Production Rollout — DONE (initial)

- [x] **3.0** v1.3.0 released with Phase 2 fixes (startup messaging, Windows retry-forever, install instructions, CI workflow)
- [x] **3.0.1** Upgrade test verified — upgrading from v1.2.0 → v1.3.0 preserves nutclient.json and customized shutdown scripts, restarts the service cleanly
- [x] **3.1** Initial production rollout — deployed to one Windows and one Linux server. More servers can be added incrementally as needed.
- [ ] **3.2** End-to-end test with a real UPS unplug on a production server (deferred — can be done opportunistically during the next planned outage)

---

## Phase 4: Hardening — TODO

- [ ] **4.1** Test edge cases (rapid flicker, server unreachable during outage)
- [ ] **4.2** Configure Hyper-V VM auto-start on each host (if applicable)

---

## Phase 5: Security Hardening — TODO

Findings from a focused security audit. Threat model: NutClient runs as root/SYSTEM, connects outbound to a NUT server on a trusted LAN over plain TCP. **No critical RCE found.** One HIGH and a handful of MEDIUM/LOW items.

### HIGH

- [ ] **5.1 (F1)** Argument injection from NUT `ups.status` into shutdown command line. `ExecuteShutdown` builds the args string by concatenation; quoting is not escaping. Not exploitable in default config (the example script ignores extra args), but becomes RCE-as-root if anyone uses a `bash -c` or wrapper-style shutdown command. **Fix:** use `ProcessStartInfo.ArgumentList.Add(...)` (.NET 8 built-in) instead of string concatenation. Optionally whitelist `ups.status` to known NUT flag tokens.

### MEDIUM

- [ ] **5.2 (F2)** `nutclient.json` file permissions not enforced after install. Contains the NUT password in plaintext. **Fix:** `install.sh` should `chown root:root && chmod 600` after copying; `install.ps1` should set a restrictive ACL (`icacls /inheritance:r /grant SYSTEM:F /grant Administrators:F`).
- [ ] **5.3 (F3)** Unbounded `ReadLineAsync` in `NutConnection.ReadResponseAsync`. A malicious or MITM'd NUT server could stream gigabytes to OOM the client or stall polls. **Fix:** read into a bounded buffer (8 KB max per line) and throw `NutException` if exceeded.
- [ ] **5.4 (F4)** systemd unit has no hardening directives — runs as root with full ambient capabilities. **Fix:** add `NoNewPrivileges=yes`, `ProtectSystem=strict`, `ProtectHome=yes`, `ReadWritePaths=/var/log /opt/nutclient`, `CapabilityBoundingSet=CAP_SYS_BOOT`, `AmbientCapabilities=CAP_SYS_BOOT`, `RestrictAddressFamilies=AF_INET AF_INET6 AF_UNIX`. Keep running as root for the `poweroff` capability.

### LOW

- [ ] **5.5 (F5/F6)** Status file and log file get default umask permissions under `/var/log` (world-readable). Neither contains secrets, but UPS topology and host info leak. **Fix:** set 0640 explicitly on creation.
- [ ] **5.6 (F8)** `Log` and `RotateLogIfNeeded` swallow all exceptions silently. Could hide failures from an attacker manipulating the log path. **Fix:** at minimum log to stderr/_logger on failure; open log files with `FileShare.Read` and explicit non-symlink-follow semantics.
- [ ] **5.7 (F12)** `Thread.Sleep` during pre-shutdown delay blocks the BackgroundService thread and ignores cancellation. **Fix:** replace with token-aware `Task.Delay`.

### INFO (won't fix unless needed)

- **F7** install.sh doesn't verify what it's copying — informational, mitigated by users running it next to the release artifact
- **F9** NUT response text echoed verbatim into exception messages — does not leak the password, just attacker-chosen text into client log
- **F10** No TLS to NUT — explicitly assumed in threat model (trusted LAN), document in SECURITY.md and README
- **F11** Example shutdown scripts don't validate args — defense in depth, low value since they don't re-feed args to other commands

### What's already done well (per audit)

- `UpsStateMachine` is pure and well-structured, easy to test
- `BatteryCharge`/`BatteryRuntime` parsed through `int.TryParse` before reaching `ExecuteShutdown` — neutralizes numeric-field injection
- Status file write is atomic (.tmp + rename)
- `UseShellExecute = false` — no shell interpretation
- Config never reloaded hot — local config attackers don't get live re-execution
- `AccessDenied` is terminal — stops cleanly instead of hammering server
- 5-second per-read timeout on socket
- `NutConnection` IDisposable cleanup is thorough

---

## Safety Points

- 60s battery countdown prevents shutdown on brief flickers
- Dead time (30s default) shuts down if NUT server unreachable while on battery
- Threshold-based shutdown only triggers when already on battery
- Pre-shutdown hook runs before main shutdown for alerts/cleanup
- LB and FSD from the UPS always trigger immediate shutdown as safety nets
- Startup grace: client never shuts down on a "can't reach server" error if it has never successfully connected
