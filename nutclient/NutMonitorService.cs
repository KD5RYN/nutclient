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
                ProcessPollDecision(decision);
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
                ProcessPollDecision(decision);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                var decision = _stateMachine.OnPollFailure($"{ex.GetType().Name}: {ex.Message}");
                ProcessPollDecision(decision);
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

        Log("NUT UPS Monitor stopped");
    }

    private void ProcessPollDecision(PollDecision decision)
    {
        foreach (var msg in decision.LogMessages)
            Log(msg);
        if (decision.Shutdown is { } s)
            ExecuteShutdown(s.Reason, s.Data);
        WriteStatusFile();
    }

    private void ProcessStatusDecision(StatusDecision decision)
    {
        foreach (var msg in decision.LogMessages)
            Log(msg);
        if (decision.Shutdown is { } s)
            ExecuteShutdown(s.Reason, s.Data);
    }

    private async Task PollUpsStatusAsync(CancellationToken ct)
    {
        using var conn = new NutConnection(
            _config.NutServer.Host,
            _config.NutServer.Port,
            _config.NutServer.Username,
            _config.NutServer.Password);

        await conn.ConnectAsync(ct);

        try
        {
            var ups = _config.NutServer.UpsName;
            var data = new UpsData
            {
                Status = await conn.GetVariableAsync(ups, "ups.status", ct)
            };

            data.BatteryCharge = await GetIntVariableAsync(conn, ups, "battery.charge", ct);
            data.BatteryRuntime = await GetIntVariableAsync(conn, ups, "battery.runtime", ct);

            if (_config.Monitoring.InputVoltageMinWarn.HasValue)
                data.InputVoltage = await GetDoubleVariableAsync(conn, ups, "input.voltage", ct);
            if (_config.Monitoring.LoadPercentWarn.HasValue)
                data.Load = await GetIntVariableAsync(conn, ups, "ups.load", ct);

            await conn.LogoutAsync();

            var decision = _stateMachine.HandleStatus(data);
            ProcessStatusDecision(decision);
        }
        catch
        {
            await conn.LogoutAsync();
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

    private void ExecuteShutdown(string reason, UpsData data)
    {
        var charge = data.BatteryCharge?.ToString() ?? "-1";
        var runtime = data.BatteryRuntime?.ToString() ?? "-1";
        var statusQuoted = $"\"{data.Status}\"";

        // Run pre-shutdown hook if configured
        if (!string.IsNullOrEmpty(_config.Monitoring.PreShutdownCommand))
        {
            var preArgs = $"{_config.Monitoring.PreShutdownArguments ?? ""} {reason} {charge} {runtime} {statusQuoted}";
            Log($"RUNNING PRE-SHUTDOWN: {_config.Monitoring.PreShutdownCommand} {preArgs}");
            RunProcess(_config.Monitoring.PreShutdownCommand, preArgs, "Pre-shutdown");

            if (_config.Monitoring.PreShutdownDelaySeconds > 0)
            {
                Log($"Waiting {_config.Monitoring.PreShutdownDelaySeconds}s before shutdown...");
                Thread.Sleep(_config.Monitoring.PreShutdownDelaySeconds * 1000);
            }
        }

        var fullArgs = $"{_config.Monitoring.ShutdownArguments} {reason} {charge} {runtime} {statusQuoted}";
        Log($"EXECUTING SHUTDOWN: {_config.Monitoring.ShutdownCommand} {fullArgs}");
        RunProcess(_config.Monitoring.ShutdownCommand, fullArgs, "Shutdown");
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
            File.Move(tmpFile, _statusFile, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to write status file: {Message}", ex.Message);
        }
    }

    private void Log(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
        _logger.LogInformation(message);

        try
        {
            RotateLogIfNeeded();
            File.AppendAllText(_logFile, line + Environment.NewLine);
        }
        catch { }

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
                File.WriteAllText(_logFile, "");
            }
        }
        catch { }
    }
}
