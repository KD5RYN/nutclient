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

## Phase 2: Testing with Dedicated Test UPS — IN PROGRESS

Test setup: Raspberry Pi running NUT with APC Back-UPS ES 850G2.

- [x] **2.1** Set up test NUT server (Raspberry Pi + APC Back-UPS)
- [x] **2.2** Verify polling in console mode
- [x] **2.3** Install as systemd service on Linux, verify auto-start on reboot
- [x] **2.4** Install as Windows service on jldev, verify auto-start on reboot
- [x] **2.5** Simulate power loss — verified 60s countdown and shutdown with correct args
- [x] **2.6** Test power flicker — verified shutdown cancels on restore
- [ ] **2.7** Test LB/FSD — verify immediate shutdown (no 60s wait)
- [ ] **2.8** Test dead time — kill NUT server while on battery, verify shutdown after 30s

---

## Phase 3: Production Rollout — TODO

- [ ] **3.1** Deploy to all production servers via install script
- [ ] **3.2** End-to-end test with real UPS unplug
- [ ] **3.3** Bundle remaining Phase 2 fixes into v1.3.0 release if needed

---

## Phase 4: Hardening — TODO

- [ ] **4.1** Test edge cases (rapid flicker, server unreachable during outage)
- [ ] **4.2** Configure Hyper-V VM auto-start on each host (if applicable)

---

## Safety Points

- 60s battery countdown prevents shutdown on brief flickers
- Dead time (30s default) shuts down if NUT server unreachable while on battery
- Threshold-based shutdown only triggers when already on battery
- Pre-shutdown hook runs before main shutdown for alerts/cleanup
- LB and FSD from the UPS always trigger immediate shutdown as safety nets
- Startup grace: client never shuts down on a "can't reach server" error if it has never successfully connected
