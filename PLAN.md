# Plan: NutClient — Cross-Platform NUT UPS Monitor

## Context

Custom .NET 8 NUT client for graceful server shutdown on UPS power loss. Deployed to Windows and Linux servers in the Cygnetron server room. Monitors UPS via NUT server over TCP port 3493.

## Architecture

```
  NUT Server (Pi)                    NutClient (each server)
 ┌──────────────┐                   ┌──────────────────────────┐
 │ upsd :3493   │◄── TCP poll ──────│ NutClient                │
 │              │    every 5s       │                          │
 │ ups1, ups2   │                   │ On battery → 60s timer   │
 └──────────────┘                   │ Timer expires → script   │
                                    │ Low battery → immediate  │
                                    │ FSD → immediate          │
                                    │ Dead time → shutdown     │
                                    │ Power restored → cancel  │
                                    └──────────────────────────┘
```

---

## Phase 1: Development — DONE

- [x] Custom NutClient built — C# .NET 8, cross-platform (Windows + Linux)
- [x] Polls ups.status, battery.charge, battery.runtime, input.voltage, ups.load
- [x] Shutdown triggers: OB timer (60s default), LB, FSD, battery charge/runtime thresholds
- [x] Dead time safety — shuts down if server unreachable while on battery
- [x] Pre-shutdown hook — optional command before main shutdown script
- [x] Shutdown reason + UPS data passed as script arguments
- [x] Error handling — typed errors (Transient/AccessDenied/Protocol), exponential backoff
- [x] Status file (nutclient-status.json) with 25-entry rolling history
- [x] Log rotation at configurable size (1MB default)
- [x] LogLevel config: "events" (default, quiet) vs "all" (every poll)
- [x] Example shutdown scripts (PowerShell + bash) with param blocks
- [x] State machine extracted for testability (UpsStateMachine)
- [x] 52 unit/integration tests with mock NUT server — all passing
- [x] Full README with setup, config, developer docs
- [x] Split into own repo (`~/Documents/nutclient/`)
- [x] Install scripts for Linux (install.sh) and Windows (install.ps1)
- [x] GitHub Actions CI — auto-build + release on tag push (win-x64, linux-x64, linux-arm64)

---

## Phase 2: Testing with Dedicated Test UPS — IN PROGRESS

Test NUT server set up on nutpi (Raspberry Pi) with APC Back-UPS ES 850G2.

- [x] **2.1** Set up test NUT server on nutpi (`nutpi.lan.cqspot.com`)
  - APC Back-UPS ES 850G2 detected via USB
  - NUT configured: usbhid-ups driver, netserver mode, listening on 0.0.0.0:3493
  - udev rule added for USB permissions
- [x] **2.2** Point NutClient at test NUT server, verify polling in console mode
  - Confirmed: polls OL, charge: 100%, runtime: 20600s
- [ ] **2.3** Install as systemd service on Linux, verify auto-start
- [ ] **2.4** Install as Windows service on jldev, verify auto-start
- [x] **2.5** Simulate power loss (unplug test UPS) — verified:
  - OB detected, 60s countdown logged every 5s
  - Shutdown script called with args: `timer_expired 92 18952 "OB DISCHRG"`
  - Tested with both `echo` and a real bash script — script received all args correctly
- [x] **2.6** Test power flicker — verified:
  - Unplugged for ~16s, plugged back in
  - "POWER RESTORED" logged, "Shutdown cancelled — power returned in time"
  - No shutdown triggered
- [ ] **2.7** Test LB/FSD — verify immediate shutdown (no 60s wait)
- [ ] **2.8** Test dead time — kill NUT server while on battery, verify shutdown after 30s

---

## Phase 3: Release & Deploy — TODO

- [ ] **3.1** Push repo to GitHub
- [ ] **3.2** Tag v1.0.0 — GitHub Actions builds and creates release
- [ ] **3.3** Test download + install on a clean Linux machine
- [ ] **3.4** Test download + install on Windows (jldev)

---

## Phase 4: Roll Out to Production Servers — TODO

- [ ] **4.1** Get all server names, IPs, and MACs
- [ ] **4.2** Deploy NutClient to each server via install script
- [ ] **4.3** Update `server_inventory.json` with all servers
- [ ] **4.4** End-to-end test with real UPS unplug

---

## Phase 5: Hardening — TODO

- [ ] **5.1** Test edge cases (server unreachable during outage, rapid flicker)
- [ ] **5.2** Configure Hyper-V VM auto-start on each host
- [ ] **5.3** Synology NAS — deploy NutClient or investigate alternatives

---

## Key Info

### Test Environment
- **Test Pi (nutpi):** `nutpi.lan.cqspot.com`, SSH: `jlacy` / `Football1!`
- **Test UPS:** APC Back-UPS ES 850G2 (vendor 051d:0002)
- **Production Pi (necprojpi3):** 172.24.142.16
- **Test VM (jldev):** 172.24.142.15, MAC 00:15:5d:8e:4c:2e

### Credentials
- **NUT secondary user:** `upsmon_secondary` / `j0@tm0n`
- **NUT protocol port:** 3493

### Safety Points
- 60s battery countdown prevents shutdown on brief flickers
- Dead time (30s default) shuts down if NUT server unreachable while on battery
- Threshold-based shutdown only triggers when already on battery
- Pre-shutdown hook runs before main shutdown for alerts/cleanup
- VMs wake via Hyper-V auto-start, not WOL (WOL is for physical hosts only)
