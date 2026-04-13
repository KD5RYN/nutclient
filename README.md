# NutClient — Cross-Platform NUT UPS Monitor

[![CI](https://github.com/KD5RYN/nutclient/actions/workflows/ci.yml/badge.svg)](https://github.com/KD5RYN/nutclient/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A .NET 8 service that monitors a NUT (Network UPS Tools) server and runs a shutdown script when UPS power is lost. Works on Windows and Linux.

## How It Works

```
  NUT Server (Pi)                    This Client (any server)
 ┌──────────────┐                   ┌──────────────────────────┐
 │ upsd :3493   │◄── TCP poll ──────│ NutClient                │
 │              │    every 5s       │                          │
 │ ups1, ups2   │                   │ On battery → 60s timer   │
 └──────────────┘                   │ Timer expires → script   │
                                    │ Low battery → immediate  │
                                    │ FSD → immediate          │
                                    │ Power restored → cancel  │
                                    └──────────────────────────┘
```

- Maintains a persistent TCP connection to a NUT server (port 3493) with `LOGIN` registration
- Polls `ups.status` every 5 seconds (configurable)
- On battery (`OB`): starts a 60-second countdown
- Low battery (`LB`) or Forced Shutdown (`FSD`): immediate shutdown
- Power restored during countdown: cancels shutdown
- Runs a configurable shutdown script (PowerShell on Windows, bash on Linux)
- Writes a status file (`nutclient-status.json`) with current state and last 25 poll results

---

## Prerequisites

- **NUT server** accessible on the network (port 3493)
- No .NET SDK required — pre-built binaries are available

---

## Installation

### Option A: Download pre-built release (recommended)

Download the latest release for your platform from [GitHub Releases](../../releases):

- `nutclient-win-x64.zip` — Windows
- `nutclient-linux-x64.tar.gz` — Linux x64
- `nutclient-linux-arm64.tar.gz` — Linux ARM64 (Raspberry Pi, Synology)

**Linux:**
```bash
tar xzf nutclient-linux-x64.tar.gz
cd linux-x64
sudo ./install.sh                            # installs to /opt/nutclient
sudo nano /opt/nutclient/nutclient.json      # set NUT server host, UPS name, credentials
sudo systemctl start nutclient               # start the service
sudo systemctl status nutclient              # verify it's running
```

> `install.sh` copies `nutclient.json.linux-example` to `/opt/nutclient/nutclient.json` (the Linux template, not the Windows-defaults `nutclient.json` in the archive). It registers the service but doesn't start it — edit the config first, then start manually. On reboot it'll start automatically.

> **Distro compatibility:** `install.sh` is written for **Debian-based distros** (Debian, Ubuntu, Raspberry Pi OS) and has been tested there. It should also work on any **systemd-based distro** (RHEL, CentOS, Fedora, Rocky, Alma, openSUSE, Arch) since it only uses `systemctl` and standard FHS paths. A few notes:
>
> - **RHEL / Fedora family:** SELinux may block the shutdown script or log writes. If the service fails, check `sudo ausearch -m avc` for denials. For testing you can temporarily run `sudo setenforce 0`; for production, create a proper SELinux policy or set `LogFile` in `nutclient.json` to a writable path like `/opt/nutclient/nutclient.log`.
> - **Synology DSM:** has pre-installed NUT and its own init system — `install.sh` won't work directly. Manual install: copy `NutClient` to `/usr/local/bin` and create a startup entry via Task Scheduler or synoservice.
> - **Alpine / OpenRC / runit / Void:** no systemd, so `install.sh` bails out early. You'll need to write a distro-native service unit (OpenRC init script, runit sv directory, etc.) and copy the binary + config manually.

**Windows** (run PowerShell as Administrator):
```powershell
Expand-Archive nutclient-win-x64.zip -DestinationPath C:\NutClient
cd C:\NutClient\win-x64
powershell -ExecutionPolicy Bypass -File install.ps1   # installs to C:\NutClient
notepad C:\NutClient\nutclient.json                    # set NUT server host, UPS name, credentials
Start-Service NutUpsMonitor                            # start the service
Get-Service NutUpsMonitor                              # verify it's running
```

> **Note:** `install.ps1` is not code-signed, so PowerShell's default execution policy will block it. Use `-ExecutionPolicy Bypass` as shown above, or run `Unblock-File install.ps1` first. The install script registers the Windows service but doesn't start it — edit the config first, then start manually the first time. On subsequent boots the service starts automatically.

The install scripts will:
- Copy the binary, config, and shutdown script to the right locations
- Install and enable the service (systemd or Windows service)
- Preserve existing config if upgrading

### Option B: Build from source

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
# Clone
git clone https://github.com/KD5RYN/nutclient.git
cd nutclient

# Build
dotnet publish nutclient -c Release -r linux-x64 -o publish
# or: -r win-x64, -r linux-arm64

# Install
cd publish
nano nutclient.json
sudo ./install.sh                # Linux
# or: powershell -File install.ps1   # Windows
```

### Testing in Console Mode

Before relying on the service, run interactively to verify it connects:

```bash
# Windows
C:\NutClient\NutClient.exe

# Linux
/opt/nutclient/NutClient
```

You should see output like:
```
2026-04-10 14:30:41 NUT UPS Monitor started
2026-04-10 14:30:41 Monitoring ups1@your-nut-server.example.com:3493
```

With `LogLevel: "events"` (default), it will be silent during normal polling and only log when something happens. Press Ctrl+C to stop.

### Managing the Service

**Windows:**
```powershell
Get-Service NutUpsMonitor                              # check status
Start-Service NutUpsMonitor                            # start
Stop-Service NutUpsMonitor                             # stop
Restart-Service NutUpsMonitor                          # restart
Get-Content C:\Scripts\nutclient.log -Tail 20          # view logs
Get-Content C:\Scripts\nutclient-status.json           # quick status
```

To uninstall: `powershell -File C:\NutClient\uninstall-service.ps1`

**Linux:**
```bash
sudo systemctl status nutclient                        # check status
sudo systemctl start nutclient                         # start
sudo systemctl stop nutclient                          # stop
sudo systemctl restart nutclient                       # restart
journalctl -u nutclient -f                             # view logs
cat /var/log/nutclient-status.json                     # quick status
```

To uninstall: `sudo systemctl disable --now nutclient && sudo rm -rf /opt/nutclient /etc/systemd/system/nutclient.service`

### Modifying Settings After Install

The config file is at:
- **Linux:** `/opt/nutclient/nutclient.json`
- **Windows:** `C:\NutClient\nutclient.json`

After editing, restart the service to pick up the changes:

**Linux:**
```bash
sudo nano /opt/nutclient/nutclient.json
sudo systemctl restart nutclient
sudo systemctl status nutclient    # verify it's still running
```

**Windows:**
```powershell
notepad C:\NutClient\nutclient.json
Restart-Service NutUpsMonitor
Get-Service NutUpsMonitor          # verify it's still running
```

`install.sh` and `install.ps1` both **preserve your existing config** if you re-run them later (e.g., for an upgrade). They only create a fresh template if no config exists at the destination.

If the service fails to start after a config change, check the log for parse errors:
- Linux: `journalctl -u nutclient -n 50`
- Windows: `Get-Content C:\Scripts\nutclient.log -Tail 50`

---

## Configuration

All settings are in `nutclient.json`, which must be in the same directory as the executable.

```json
{
  "NutServer": {
    "Host": "your-nut-server.example.com",
    "Port": 3493,
    "UpsName": "ups1",
    "Username": "monuser",
    "Password": "CHANGE_ME"
  },
  "Monitoring": {
    "PollIntervalSeconds": 5,
    "ShutdownDelaySeconds": 60,
    "ShutdownCommand": "powershell.exe",
    "ShutdownArguments": "-ExecutionPolicy Bypass -File C:\\Scripts\\graceful-shutdown.ps1",
    "LogFile": "C:\\Scripts\\nutclient.log",
    "StatusFile": "C:\\Scripts\\nutclient-status.json",
    "LogLevel": "events",
    "LogMaxBytes": 1048576,
    "DeadTimeSeconds": 30,
    "BatteryChargePercent": null,
    "BatteryRuntimeSeconds": null,
    "InputVoltageMinWarn": null,
    "LoadPercentWarn": null,
    "PreShutdownCommand": null,
    "PreShutdownArguments": null,
    "PreShutdownDelaySeconds": 5
  }
}
```

### NutServer Settings

| Setting | Description |
|---------|-------------|
| `Host` | Hostname or IP of the NUT server |
| `Port` | NUT server port (default 3493) |
| `UpsName` | Name of the UPS to monitor (e.g., `ups1`, `ups2`) |
| `Username` | NUT username for authentication |
| `Password` | NUT password for authentication |

### Monitoring Settings

| Setting | Description | Default |
|---------|-------------|---------|
| `PollIntervalSeconds` | How often to check UPS status | `5` |
| `ShutdownDelaySeconds` | Seconds to wait on battery before shutting down. Set to `0` to disable the timer and rely on thresholds / LB / FSD only. | `60` |
| `ShutdownCommand` | Program to run for shutdown | Windows: `powershell.exe`, Linux: `/bin/bash` |
| `ShutdownArguments` | Arguments passed to shutdown command | Path to shutdown script |
| `LogFile` | Path to the log file | Windows: `C:\Scripts\nutclient.log`, Linux: `/var/log/nutclient.log` |
| `StatusFile` | Path to the status JSON file (leave empty for default next to exe) | Windows: `C:\Scripts\nutclient-status.json`, Linux: `/var/log/nutclient-status.json` |
| `BatteryChargePercent` | Shut down when battery charge drops to or below this % (on battery only) | `null` (disabled) |
| `BatteryRuntimeSeconds` | Shut down when estimated runtime drops to or below this many seconds (on battery only) | `null` (disabled) |
| `InputVoltageMinWarn` | Log a warning when input AC voltage drops below this value | `null` (disabled) |
| `LoadPercentWarn` | Log a warning when UPS load exceeds this % | `null` (disabled) |
| `DeadTimeSeconds` | If server is unreachable while last known on battery, shut down after this many seconds | `30` |
| `PreShutdownCommand` | Program to run before the main shutdown command (e.g., send alert, sync) | `null` (disabled) |
| `PreShutdownArguments` | Arguments for the pre-shutdown command | `null` |
| `PreShutdownDelaySeconds` | Seconds to wait between pre-shutdown and shutdown commands | `5` |
| `LogMaxBytes` | Rotate log file when it exceeds this size in bytes | `1048576` (1 MB) |
| `LogLevel` | `"events"` = only state changes, warnings, errors, shutdown; `"all"` = every poll | `"events"` |

**Log level:** In `events` mode (default), the log is silent during normal AC operation — it only writes when something happens (power loss, restore, countdown, warnings, errors, shutdown). Use `"all"` for debugging to see every poll with UPS status. The status file is always updated every poll regardless of log level.

**Threshold notes:**
- `BatteryChargePercent` and `BatteryRuntimeSeconds` only trigger shutdown when the UPS is already on battery (`OB`). They won't trigger during normal charging.
- `InputVoltageMinWarn` and `LoadPercentWarn` are warning-only — they log but don't trigger shutdown.
- Set to `null` to disable (default). The basic OB/LB/FSD status-based shutdown works without any thresholds configured.

**Shutdown strategies — pick whichever fits your needs:**

*Default (timer-based):* wait 60 seconds on battery, then shut down regardless of charge level.
```json
"ShutdownDelaySeconds": 60,
"BatteryChargePercent": null,
"BatteryRuntimeSeconds": null
```

*Threshold-only (charge %):* disable the timer, shut down when battery drops below a percentage.
```json
"ShutdownDelaySeconds": 0,
"BatteryChargePercent": 30,
"BatteryRuntimeSeconds": null
```

*Threshold-only (runtime):* disable the timer, shut down when estimated runtime drops below a threshold.
```json
"ShutdownDelaySeconds": 0,
"BatteryChargePercent": null,
"BatteryRuntimeSeconds": 180
```

*Belt-and-suspenders:* use all three — whichever triggers first wins.
```json
"ShutdownDelaySeconds": 300,
"BatteryChargePercent": 20,
"BatteryRuntimeSeconds": 120
```

In all cases, `LB` (low battery) and `FSD` (forced shutdown) from the UPS still trigger immediate shutdown as a safety net.

**Dead time:** If the NUT server becomes unreachable while the UPS was last known to be on battery, the client assumes the worst (power is still out, UPS is draining) and triggers shutdown after `DeadTimeSeconds`. This prevents a network failure during a power outage from leaving the server running until the UPS dies.

**Pre-shutdown hook:** Runs before the main shutdown script with the same arguments (reason, charge, runtime, status). Use it to send a final email alert, flush caches, notify users, or stop databases before the main shutdown script runs. The `PreShutdownDelaySeconds` gap gives the pre-shutdown command time to complete.

**Log rotation:** When the log file exceeds `LogMaxBytes`, it's copied to `<logfile>.1` and the original is cleared. Only one rotated backup is kept.

---

## Shutdown Scripts

The client runs a script when shutdown is triggered. Example scripts are in the `scripts/` directory.

NutClient appends the following arguments to your configured `ShutdownArguments`:

| Argument | Position | Description |
|----------|----------|-------------|
| Reason | 1 | Why shutdown was triggered (see table below) |
| Battery Charge | 2 | Battery % at time of shutdown (-1 if unknown) |
| Battery Runtime | 3 | Estimated runtime in seconds (-1 if unknown) |
| UPS Status | 4 | Raw UPS status string (e.g., "OB LB") |

**Reason values:**

| Reason | Meaning |
|--------|---------|
| `timer_expired` | On battery for ShutdownDelaySeconds with no power restore |
| `low_battery` | UPS flagged LB (low battery) |
| `forced_shutdown` | UPS flagged FSD (forced shutdown) |
| `battery_charge` | Battery charge dropped to or below BatteryChargePercent threshold |
| `battery_runtime` | Estimated runtime dropped to or below BatteryRuntimeSeconds threshold |
| `dead_time` | NUT server unreachable while last known on battery for DeadTimeSeconds |

### Windows — `graceful-shutdown.ps1`

Default location: `C:\Scripts\graceful-shutdown.ps1`

The script receives arguments as named PowerShell parameters:
```powershell
param(
    [string]$Reason = "unknown",
    [int]$BatteryCharge = -1,
    [int]$BatteryRuntime = -1,
    [string]$UpsStatus = ""
)
```

What the example does:
- Logs the shutdown event with reason and battery info
- Stops all running Hyper-V VMs in parallel (if Hyper-V is installed), with a 3-minute timeout
- Shuts down Windows

Customize it to stop your own services, save application state, or take different actions based on the reason.

### Linux — `graceful-shutdown.sh`

Default location: `/opt/nutclient/scripts/graceful-shutdown.sh`

The script receives arguments as positional parameters:
```bash
REASON="${1:-unknown}"
BATTERY_CHARGE="${2:--1}"
BATTERY_RUNTIME="${3:--1}"
UPS_STATUS="${4:-}"
```

What the example does:
- Logs the shutdown event with reason and battery info
- Includes commented-out examples for stopping Docker containers and systemd services
- Runs `poweroff`

> `install.sh` automatically sets the executable bit when it copies the script. If you write your own shutdown script from scratch, run `chmod +x graceful-shutdown.sh` yourself.

---

## Status File

The client writes a `nutclient-status.json` file every poll cycle. This gives you a quick way to check current state without reading logs.

```bash
# Windows
type C:\Scripts\nutclient-status.json

# Linux
cat /var/log/nutclient-status.json
```

Example output:
```json
{
  "server": "your-nut-server.example.com:3493",
  "upsName": "ups1",
  "state": "Online",
  "currentStatus": "OL",
  "batteryCharge": 100,
  "batteryRuntime": 608,
  "inputVoltage": 115.6,
  "upsLoad": 49,
  "lastPoll": "2026-04-10 14:30:41",
  "consecutiveFailures": 0,
  "history": [
    { "time": "2026-04-10 14:30:41", "status": "OL", "event": "poll" },
    { "time": "2026-04-10 14:30:36", "status": "OL", "event": "poll" }
  ]
}
```

| Field | Description |
|-------|-------------|
| `state` | Human-readable: `Online`, `On Battery`, `Shutting Down`, `Error`, `Access Denied` |
| `currentStatus` | Raw UPS status string (e.g., `OL`, `OB`, `OB LB`) |
| `batteryCharge` | Battery charge % (null if not available) |
| `batteryRuntime` | Estimated runtime in seconds (null if not available) |
| `inputVoltage` | AC input voltage (null if `InputVoltageMinWarn` not configured) |
| `upsLoad` | UPS load % (null if `LoadPercentWarn` not configured) |
| `consecutiveFailures` | Number of failed polls in a row (0 = healthy) |
| `onBatterySince` | Timestamp when battery mode started (null if on AC) |
| `shutdownInSeconds` | Seconds until shutdown triggers (null if not counting down) |
| `history` | Last 25 status entries, newest first |

---

## Error Handling

- **Connection failures**: Retries with exponential backoff (5s, 10s, 20s, 40s, up to 60s max)
- **Bad credentials**: Stops immediately — fix `nutclient.json` and restart the service
- **Server unreachable**: Logs a warning after 6 consecutive failures, continues retrying
- **Connection restored**: Logs recovery and resets to normal polling interval

---

## Hardening (Linux)

The default `nutclient.service` includes a conservative set of systemd hardening directives that block exotic attacks but **don't restrict what your shutdown script can do**:

- `NoNewPrivileges`, `ProtectKernelTunables`, `ProtectKernelModules`
- `ProtectControlGroups`, `LockPersonality`
- `RestrictRealtime`, `RestrictSUIDSGID`

If your shutdown script is **simple** (e.g., just runs `poweroff` and writes a log file), you can opt into stronger restrictions by editing `/etc/systemd/system/nutclient.service` and uncommenting some or all of the directives in the "Optional aggressive hardening" section. The most useful ones:

| Directive | What it blocks | What it might break |
|-----------|---------------|---------------------|
| `ProtectSystem=strict` | Writes to `/usr`, `/boot`, `/etc` | Scripts that update `/etc` config or `/var/lib` state |
| `ProtectHome=yes` | Access to `/home`, `/root` | Scripts that write to a user home dir |
| `PrivateTmp=yes` | Shared `/tmp` access | Scripts that read other processes' temp files |
| `RestrictAddressFamilies=AF_INET AF_INET6 AF_UNIX` | Raw sockets, Bluetooth, etc. | Scripts using exotic networking |
| `MemoryDenyWriteExecute=yes` | Writable+executable memory | Some interpreters with JIT |
| `CapabilityBoundingSet=CAP_SYS_BOOT CAP_KILL` | All capabilities except boot/kill | Mount, raw network, ptrace, ZFS commands, etc. |
| `ReadWritePaths=/var/log /opt/nutclient` | Required if using `ProtectSystem=strict` | — |

**After editing the unit file:**
```bash
sudo systemctl daemon-reload
sudo systemctl restart nutclient
sudo systemctl status nutclient    # verify it actually started
```

**Test the shutdown script after enabling hardening** — restrictions are silent, and you only find out the script is broken when you actually need it. Run a real or simulated power loss and confirm `nutclient-shutdown.log` (or whatever log your script writes) shows the script ran successfully.

---

## Development

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Project Structure

```
nutclient/                    <- Main application
├── NutClient.csproj          <- .NET 8 project file
├── Program.cs                <- Entry point — loads config, sets up DI, detects OS
├── NutMonitorService.cs      <- BackgroundService — poll loop, I/O, shutdown execution
├── UpsStateMachine.cs        <- Pure state machine — all decision logic, no I/O
├── NutConnection.cs          <- TCP client for NUT protocol (connect, auth, query)
├── Config.cs                 <- Configuration model classes
├── Models.cs                 <- Shared data models (UpsData, StatusSnapshot, etc.)
├── nutclient.json            <- Config file (Windows defaults)
├── nutclient.json.linux-example
├── scripts/
│   ├── graceful-shutdown.ps1 <- Example Windows shutdown script
│   └── graceful-shutdown.sh  <- Example Linux shutdown script
├── install-service.ps1       <- Installs as Windows service
├── uninstall-service.ps1     <- Removes Windows service
└── nutclient.service         <- systemd unit file for Linux

nutclient.tests/              <- Test suite
├── NutClient.Tests.csproj
├── MockNutServer.cs          <- TCP server that speaks NUT protocol for tests
├── NutConnectionTests.cs     <- Connection, auth, response parsing, error handling
├── UpsStateMachineTests.cs   <- State transitions, thresholds, timers, dead time
└── BackoffTests.cs           <- Exponential backoff calculation
```

### Architecture

The core logic is split into two layers:

- **`UpsStateMachine`** — Pure decision logic with no I/O. Takes UPS data in, returns decisions (log messages, shutdown actions) out. Uses .NET 8's `TimeProvider` for testable time. This is where all the state transitions, threshold checks, timer logic, and dead time detection live.

- **`NutMonitorService`** — Thin orchestration shell. Runs the poll loop, calls `NutConnection` to fetch data, feeds it to the state machine, and executes the resulting decisions (logging, writing files, running shutdown scripts).

This separation means the state machine can be tested with a fake clock and no network, while `NutConnection` is tested against a `MockNutServer`.

### Building

```bash
# Build (debug)
dotnet build nutclient

# Build for deployment
dotnet publish nutclient -c Release -r linux-x64 -o publish
dotnet publish nutclient -c Release -r win-x64 -o publish
dotnet publish nutclient -c Release -r linux-arm64 -o publish
```

### Running Tests

```bash
# Run all tests
dotnet test nutclient.tests

# Run with detailed output
dotnet test nutclient.tests --verbosity normal

# Run a specific test class
dotnet test nutclient.tests --filter "FullyQualifiedName~UpsStateMachineTests"

# Run a specific test
dotnet test nutclient.tests --filter "FullyQualifiedName~TimerExpiry_TriggersShutdown"
```

### Test Coverage

**89 tests** across 4 files:

| File | Tests | What's covered |
|------|-------|----------------|
| `NutConnectionTests` | 19 | TCP connection, auth success/failure, variable queries, error classification (Transient vs AccessDenied vs Protocol), server disconnect, persistent connection, LOGIN registration |
| `UpsStateMachineTests` | 41 | OL/OB/LB/FSD state transitions, power restore cancels shutdown, timer expiry, disabled timer (ShutdownDelaySeconds=0), battery charge/runtime thresholds, thresholds ignored on AC, dead time (comms loss while on battery), input voltage/load warnings, combined status flags (OL CHRG, OB LB DISCHRG), history capping, shutdown-only-once, first-connect/startup-notice messages |
| `SanitizeUpsStatusTests` | 20 | UPS status string parsing and sanitization (parameterized) |
| `BackoffTests` | 9 | Exponential backoff formula (parameterized), max cap at 60s, reset after success |

Tests run automatically on every push and pull request via GitHub Actions ([ci.yml](.github/workflows/ci.yml)). Contributors can see test results directly on their PRs.

Tests use:
- **`MockNutServer`** — A real TCP listener on localhost (random port) that speaks the NUT text protocol. Tests configure what responses it returns.
- **`FakeTimeProvider`** — .NET 8's built-in fake clock (`Microsoft.Extensions.Time.Testing`). Lets tests advance time without real waits, making timer and dead time tests instant.

### Adding New Tests

To test a new state machine behavior:

```csharp
[Fact]
public void MyNewBehavior_DoesTheThing()
{
    // _sm and _clock are set up in the constructor
    _sm.HandleStatus(Status("OB"));         // put it on battery
    _clock.Advance(TimeSpan.FromSeconds(10)); // advance time
    var decision = _sm.HandleStatus(Status("OB", charge: 50));

    Assert.Null(decision.Shutdown);          // or Assert.NotNull
    Assert.Contains(decision.LogMessages, m => m.Contains("expected text"));
}
```

To test NutConnection against the mock server:

```csharp
[Fact]
public async Task MyNewConnectionTest()
{
    _server.Variables["ups.status"] = "OB";  // configure mock response
    using var conn = CreateConnection();
    await conn.ConnectAsync();

    var status = await conn.GetVariableAsync("ups1", "ups.status");
    Assert.Equal("OB", status);
}
```

---

## License

NutClient is released under the [MIT License](LICENSE). Free to use, modify, and distribute for any purpose — commercial or personal. Only requirement is to keep the copyright notice in derivative works.
