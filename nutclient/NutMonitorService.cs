using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NutClient;

public class NutMonitorService : BackgroundService
{
    private readonly ILogger<NutMonitorService> _logger;
    private readonly Config _config;
    private readonly string _logFile;
    private readonly string _statusFile;
    private readonly UpsStateMachine _stateMachine;

    // Persistent connection — opened once at startup, held open across polls so
    // we register as a real monitoring client (visible in `LIST CLIENT`/`NUMLOGINS`).
    // Recreated on transient failures by the per-poll logic in PollUpsStatusAsync.
    private NutConnection? _persistentConn;

    private static readonly JsonSerializerOptions StatusJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public NutMonitorService(ILogger<NutMonitorService> logger, Config config)
    {
        _logger = logger;
        _config = config;
        _stateMachine = new UpsStateMachine(config.Monitoring);

        _logFile = config.Monitoring.LogFile;
        _statusFile = string.IsNullOrEmpty(config.Monitoring.StatusFile)
            ? Path.Combine(AppContext.BaseDirectory, "nutclient-status.json")
            : config.Monitoring.StatusFile;

        // Ensure log directory exists
        var logDir = Path.GetDirectoryName(_logFile);
        if (!string.IsNullOrEmpty(logDir))
            Directory.CreateDirectory(logDir);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Log("NUT UPS Monitor started");
        Log($"Monitoring {_config.NutServer.UpsName}@{_config.NutServer.Host}:{_config.NutServer.Port}");
        Log($"Shutdown delay: {_config.Monitoring.ShutdownDelaySeconds}s");
        Log($"Shutdown command: {_config.Monitoring.ShutdownCommand} {_config.Monitoring.ShutdownArguments}");

        // SECURITY: warn if the config file (which contains the NUT password) is
        // readable by group or other on Linux. install.sh should set 0600, but a
        // user who installed manually or with an older script may have looser perms.
        WarnIfConfigFileIsTooLoose();

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_stateMachine.IsAccessDenied)
            {
                Log("Access denied — credentials are wrong. Stopping monitor. Fix nutclient.json and restart.");
                break;
            }

            try
            {
                await PollUpsStatusAsync(stoppingToken);
                var decision = _stateMachine.OnPollSuccess();
                await ProcessPollDecisionAsync(decision, stoppingToken);
            }
            catch (NutException ex) when (ex.Kind == NutErrorKind.AccessDenied)
            {
                _stateMachine.SetAccessDenied();
                Log($"ACCESS DENIED: {ex.Message}");
                continue;
            }
            catch (NutException ex)
            {
                var decision = _stateMachine.OnPollFailure(ex.Message);
                await ProcessPollDecisionAsync(decision, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                var decision = _stateMachine.OnPollFailure($"{ex.GetType().Name}: {ex.Message}");
                await ProcessPollDecisionAsync(decision, stoppingToken);
            }

            var delay = _stateMachine.GetPollDelay();
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        // Clean shutdown — send LOGOUT and close the persistent connection
        // so the server immediately removes us from `LIST CLIENT`. Best-effort,
        // any failures here are swallowed since we're stopping anyway.
        if (_persistentConn != null)
        {
            try
            {
                await _persistentConn.LogoutAsync();
            }
            catch { }
            _persistentConn.Dispose();
            _persistentConn = null;
        }

        Log("NUT UPS Monitor stopped");
    }

    private bool LogAll => _config.Monitoring.LogLevel.Equals("all", StringComparison.OrdinalIgnoreCase);

    private async Task ProcessPollDecisionAsync(PollDecision decision, CancellationToken ct)
    {
        foreach (var msg in decision.EventMessages)
            Log(msg);
        if (LogAll)
            foreach (var msg in decision.PollMessages)
                Log(msg);
        if (decision.Shutdown is { } s)
            await ExecuteShutdownAsync(s.Reason, s.Data, ct);
        WriteStatusFile();
    }

    private async Task ProcessStatusDecisionAsync(StatusDecision decision, CancellationToken ct)
    {
        foreach (var msg in decision.EventMessages)
            Log(msg);
        if (LogAll)
            foreach (var msg in decision.PollMessages)
                Log(msg);
        if (decision.Shutdown is { } s)
            await ExecuteShutdownAsync(s.Reason, s.Data, ct);
    }

    private async Task PollUpsStatusAsync(CancellationToken ct)
    {
        var ups = _config.NutServer.UpsName;

        // Ensure we have a live persistent connection. If it's null (first run
        // or torn down by a previous failure), create + connect + login. Any
        // failure here propagates as a NutException, which the main loop catches
        // and feeds to the state machine's OnPollFailure (which handles backoff
        // and dead-time).
        if (_persistentConn == null || !_persistentConn.IsConnected)
        {
            _persistentConn?.Dispose();
            _persistentConn = null;

            var conn = new NutConnection(
                _config.NutServer.Host,
                _config.NutServer.Port,
                _config.NutServer.Username,
                _config.NutServer.Password);

            try
            {
                await conn.ConnectAsync(ct);
                await conn.LoginAsync(ups, ct);
            }
            catch
            {
                conn.Dispose();
                throw;
            }

            _persistentConn = conn;
        }

        try
        {
            var data = new UpsData
            {
                Status = await _persistentConn.GetVariableAsync(ups, "ups.status", ct)
            };

            data.BatteryCharge = await GetIntVariableAsync(_persistentConn, ups, "battery.charge", ct);
            data.BatteryRuntime = await GetIntVariableAsync(_persistentConn, ups, "battery.runtime", ct);

            if (_config.Monitoring.InputVoltageMinWarn.HasValue)
                data.InputVoltage = await GetDoubleVariableAsync(_persistentConn, ups, "input.voltage", ct);
            if (_config.Monitoring.LoadPercentWarn.HasValue)
                data.Load = await GetIntVariableAsync(_persistentConn, ups, "ups.load", ct);

            var decision = _stateMachine.HandleStatus(data);
            await ProcessStatusDecisionAsync(decision, ct);
        }
        catch (NutException ex) when (ex.Kind == NutErrorKind.Transient)
        {
            // Transient errors usually mean the TCP connection is broken or the
            // server timed out. Tear down so the next poll will reconnect+relogin.
            _persistentConn.Dispose();
            _persistentConn = null;
            throw;
        }
        catch
        {
            // Any other unexpected exception — also tear down the connection
            // defensively to avoid getting stuck on a bad socket.
            _persistentConn?.Dispose();
            _persistentConn = null;
            throw;
        }
    }

    private async Task<int?> GetIntVariableAsync(NutConnection conn, string ups, string variable, CancellationToken ct)
    {
        try
        {
            var val = await conn.GetVariableAsync(ups, variable, ct);
            return int.TryParse(val, out var result) ? result : null;
        }
        catch (NutException) { return null; }
    }

    private async Task<double?> GetDoubleVariableAsync(NutConnection conn, string ups, string variable, CancellationToken ct)
    {
        try
        {
            var val = await conn.GetVariableAsync(ups, variable, ct);
            return double.TryParse(val, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : null;
        }
        catch (NutException) { return null; }
    }

    private async Task ExecuteShutdownAsync(string reason, UpsData data, CancellationToken ct)
    {
        // SECURITY: data.Status comes from the NUT server, which is network-controlled.
        // It gets passed to the shutdown command line, so a malicious or MITM'd server
        // could try to break out of the quoting and inject extra arguments. Sanitize it
        // to a whitelist of known NUT status flags before letting it anywhere near argv.
        // BatteryCharge / BatteryRuntime are already int? so they're safe.
        var safeStatus = SanitizeUpsStatus(data.Status);
        var charge = data.BatteryCharge?.ToString() ?? "-1";
        var runtime = data.BatteryRuntime?.ToString() ?? "-1";
        var statusQuoted = $"\"{safeStatus}\"";

        // Run pre-shutdown hook if configured
        if (!string.IsNullOrEmpty(_config.Monitoring.PreShutdownCommand))
        {
            var preArgs = $"{_config.Monitoring.PreShutdownArguments ?? ""} {reason} {charge} {runtime} {statusQuoted}";
            Log($"RUNNING PRE-SHUTDOWN: {_config.Monitoring.PreShutdownCommand} {preArgs}");
            RunProcess(_config.Monitoring.PreShutdownCommand, preArgs, "Pre-shutdown");

            if (_config.Monitoring.PreShutdownDelaySeconds > 0)
            {
                Log($"Waiting {_config.Monitoring.PreShutdownDelaySeconds}s before shutdown...");
                // SECURITY (F12): use Task.Delay with the cancellation token instead of
                // Thread.Sleep so service shutdown isn't blocked by this delay. If the
                // delay is cancelled (e.g., systemctl stop), we still proceed to the
                // main shutdown command on the next line — that's intentional, the
                // pre-shutdown hook already ran.
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_config.Monitoring.PreShutdownDelaySeconds), ct);
                }
                catch (TaskCanceledException)
                {
                    Log("Pre-shutdown delay cancelled — proceeding directly to shutdown");
                }
            }
        }

        var fullArgs = $"{_config.Monitoring.ShutdownArguments} {reason} {charge} {runtime} {statusQuoted}";
        Log($"EXECUTING SHUTDOWN: {_config.Monitoring.ShutdownCommand} {fullArgs}");
        RunProcess(_config.Monitoring.ShutdownCommand, fullArgs, "Shutdown");
    }

    /// <summary>
    /// Whitelist a NUT ups.status string to known status flag tokens only.
    /// Anything that isn't a recognized NUT flag is dropped. This prevents a
    /// malicious or MITM'd NUT server from injecting shell metacharacters or
    /// extra command-line arguments into the shutdown command via the status
    /// field.
    /// See: https://networkupstools.org/docs/developer-guide.chunked/apas02.html
    /// </summary>
    internal static string SanitizeUpsStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return "UNKNOWN";

        // Canonical NUT status flags. Anything not in this set is silently dropped.
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "OL",       // online (on AC)
            "OB",       // on battery
            "LB",       // low battery
            "HB",       // high battery
            "RB",       // replace battery
            "CHRG",     // charging
            "DISCHRG",  // discharging
            "BYPASS",   // on bypass
            "CAL",      // calibrating
            "OFF",      // off
            "OVER",     // overload
            "TRIM",     // trimming voltage
            "BOOST",    // boosting voltage
            "FSD",      // forced shutdown
            "ALARM",    // alarm
            "TEST",     // test in progress
            "ECO",      // economy mode
            "COMM",     // comms ok
            "NOCOMM",   // comms lost
        };

        var clean = status
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => allowed.Contains(t))
            .ToArray();

        return clean.Length > 0 ? string.Join(" ", clean) : "UNKNOWN";
    }

    private void RunProcess(string command, string arguments, string label)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var process = Process.Start(psi);
            if (process != null)
            {
                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit(120_000);

                Log($"{label} process exited with code {process.ExitCode}");
                if (!string.IsNullOrWhiteSpace(stdout))
                    Log($"stdout: {stdout.Trim()}");
                if (!string.IsNullOrWhiteSpace(stderr))
                    Log($"stderr: {stderr.Trim()}");
            }
        }
        catch (Exception ex)
        {
            Log($"{label.ToUpper()} FAILED: {ex.Message}");
        }
    }

    private void WriteStatusFile()
    {
        try
        {
            var snapshot = _stateMachine.GetStatusSnapshot(
                _config.NutServer.Host, _config.NutServer.Port, _config.NutServer.UpsName);
            var json = JsonSerializer.Serialize(snapshot, StatusJsonOptions);

            var tmpFile = _statusFile + ".tmp";
            File.WriteAllText(tmpFile, json);
            // SECURITY (F5): restrict to owner+group read/write before moving into place.
            // Status file contains UPS topology and host info — not secrets, but no need
            // to expose to all local users.
            SetSecurePermissions(tmpFile);
            File.Move(tmpFile, _statusFile, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to write status file: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Set 0640 (owner rw, group r, other none) on a file. No-op on Windows.
    /// Used for log and status files which contain non-secret but topology info.
    /// </summary>
    private static void SetSecurePermissions(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        }
        catch
        {
            // Permission setting is best-effort. If the file system doesn't support it
            // (e.g., FAT mounted somewhere weird), just continue with default perms.
        }
    }

    private void WarnIfConfigFileIsTooLoose()
    {
        // Only check on Unix — Windows uses ACLs which are checked elsewhere.
        if (OperatingSystem.IsWindows()) return;

        try
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "nutclient.json");
            if (!File.Exists(configPath)) return;

            var mode = File.GetUnixFileMode(configPath);

            // Check if group or other has any access (read, write, or execute).
            const UnixFileMode groupOrOther =
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

            if ((mode & groupOrOther) != 0)
            {
                var octal = Convert.ToString((int)mode, 8);
                Log($"WARNING: {configPath} is readable by group/other (mode: {octal})");
                Log("WARNING: This file contains the NUT password. Run: sudo chmod 600 " + configPath);
            }
        }
        catch (Exception ex)
        {
            // Don't fail startup over a permission check.
            _logger.LogWarning("Could not check config file permissions: {Message}", ex.Message);
        }
    }

    // Track whether we've already complained about a log write failure so we
    // don't spam the system journal/Event Log every 5 seconds.
    private bool _logWriteFailureReported;

    private void Log(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
        _logger.LogInformation(message);

        try
        {
            RotateLogIfNeeded();
            var existed = File.Exists(_logFile);
            File.AppendAllText(_logFile, line + Environment.NewLine);
            // SECURITY (F6): set 0640 on first creation. No-op on existing files
            // and on Windows.
            if (!existed)
                SetSecurePermissions(_logFile);

            // Reset the failure flag on the first successful write after a failure
            if (_logWriteFailureReported)
                _logWriteFailureReported = false;
        }
        catch (Exception ex)
        {
            // SECURITY (F8): don't silently swallow log write failures. Surface them
            // to the system logger (journalctl/Event Log) so admins notice if log
            // file writes are broken (full disk, bad perms, missing dir, etc.).
            // Throttled to once per failure streak so we don't spam every poll.
            if (!_logWriteFailureReported)
            {
                _logger.LogWarning("Log file write failed ({Path}): {Message}", _logFile, ex.Message);
                _logWriteFailureReported = true;
            }
        }

        Console.WriteLine(line);
    }

    private void RotateLogIfNeeded()
    {
        try
        {
            var info = new FileInfo(_logFile);
            if (info.Exists && info.Length >= _config.Monitoring.LogMaxBytes)
            {
                var rotated = _logFile + ".1";
                File.Copy(_logFile, rotated, overwrite: true);
                // SECURITY (F6): rotated backup gets the same perms as the live log.
                SetSecurePermissions(rotated);
                File.WriteAllText(_logFile, "");
                SetSecurePermissions(_logFile);
            }
        }
        catch (Exception ex)
        {
            // SECURITY (F8): surface rotation failures to the system logger instead
            // of silently dropping them.
            _logger.LogWarning("Log rotation failed ({Path}): {Message}", _logFile, ex.Message);
        }
    }
}
