# Plan: NutClient — Cross-Platform NUT UPS Monitor

## Context

Custom .NET 8 NUT client for graceful server shutdown on UPS power loss. Deployed to Windows and Linux servers in the Cygnetron server room. Monitors UPS via the Pi's NUT server (necprojpi3) over TCP port 3493.

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
- [x] Example shutdown scripts (PowerShell + bash) with param blocks
- [x] Service install scripts (Windows + Linux systemd)
- [x] State machine extracted for testability (UpsStateMachine)
- [x] 52 unit/integration tests with mock NUT server — all passing
- [x] Full README with setup, config, developer docs
- [x] Split into own repo (`~/Documents/nutclient/`)

---

## Phase 2: Test with Dedicated Test UPS — TODO

Set up a separate NUT server on a test UPS for safe testing.

- [ ] **2.1** Set up test NUT server on test UPS
- [ ] **2.2** Point NutClient at test NUT server, verify polling in console mode
- [ ] **2.3** Install as systemd service on Linux, verify auto-start
- [ ] **2.4** Install as Windows service on jldev, verify auto-start
- [ ] **2.5** Simulate power loss (unplug test UPS) — verify:
  - 60s countdown appears in log
  - Shutdown script runs with correct reason + args
  - Status file shows "On Battery" → "Shutting Down"
- [ ] **2.6** Test power flicker (unplug briefly, plug back in) — verify shutdown cancels
- [ ] **2.7** Test LB/FSD — verify immediate shutdown (no 60s wait)
- [ ] **2.8** Test dead time — kill NUT server while on battery, verify shutdown after 30s

---

## Phase 3: Roll Out to Production Servers — TODO

- [ ] **3.1** Get all server names, IPs, and MACs
- [ ] **3.2** Deploy NutClient + config + shutdown script to each server
- [ ] **3.3** Update `server_inventory.json` with all servers
- [ ] **3.4** End-to-end test with real UPS unplug

---

## Phase 4: Hardening — TODO

- [ ] **4.1** Test edge cases (flicker, server unreachable during outage)
- [ ] **4.2** Configure Hyper-V VM auto-start on each host
- [ ] **4.3** Synology NAS — deploy NutClient or investigate alternatives
- [ ] **4.4** Push to Bitbucket

---

## Key Info

### Network
- **Pi (necprojpi3):** 172.24.142.16
- **jldev (test VM):** 172.24.142.15, MAC 00:15:5d:8e:4c:2e

### Credentials
- **NUT secondary user:** `upsmon_secondary` / `j0@tm0n`
- **NUT protocol port:** 3493

### Safety Points
- 60s battery countdown prevents shutdown on brief flickers
- Dead time (30s default) shuts down if NUT server unreachable while on battery
- Threshold-based shutdown only triggers when already on battery
- Pre-shutdown hook runs before main shutdown for alerts/cleanup
- VMs wake via Hyper-V auto-start, not WOL (WOL is for physical hosts only)
