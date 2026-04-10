namespace NutClient;

/// <summary>
/// Pure state machine for UPS monitoring decisions. No I/O, no side effects.
/// Returns decisions (log messages, shutdown actions) that the caller executes.
/// </summary>
public class UpsStateMachine
{
    private readonly MonitoringConfig _config;
    private readonly TimeProvider _clock;

    // UPS state
    private bool _onBattery;
    private DateTimeOffset? _batteryStartTime;
    private bool _shutdownInitiated;

    // Connection health
    private int _consecutiveFailures;
    private DateTimeOffset? _lastSuccessfulPoll;
    private bool _accessDenied;

    // Last known data
    private UpsData _lastUpsData = new();
    private string _currentUpsStatus = "";
    private string _currentState = "Starting";

    // History
    private readonly List<StatusHistoryEntry> _history = new();
    private const int MaxHistoryEntries = 25;
    private const int MaxBackoffSeconds = 60;

    public string CurrentState => _currentState;
    public string CurrentUpsStatus => _currentUpsStatus;
    public UpsData LastUpsData => _lastUpsData;
    public bool IsOnBattery => _onBattery;
    public bool IsShutdownInitiated => _shutdownInitiated;
    public bool IsAccessDenied => _accessDenied;
    public int ConsecutiveFailures => _consecutiveFailures;
    public DateTimeOffset? LastSuccessfulPoll => _lastSuccessfulPoll;
    public DateTimeOffset? BatteryStartTime => _batteryStartTime;
    public IReadOnlyList<StatusHistoryEntry> History => _history;

    public UpsStateMachine(MonitoringConfig config, TimeProvider? clock = null)
    {
        _config = config;
        _clock = clock ?? TimeProvider.System;
    }

    public StatusDecision HandleStatus(UpsData data)
    {
        var events = new List<string>();  // always logged
        var polls = new List<string>();   // only logged when LogLevel=all
        ShutdownAction? shutdown = null;

        var status = data.Status;
        _currentUpsStatus = status;
        _lastUpsData = data;

        var chargeStr = data.BatteryCharge.HasValue ? $", charge: {data.BatteryCharge}%" : "";
        var runtimeStr = data.BatteryRuntime.HasValue ? $", runtime: {data.BatteryRuntime}s" : "";
        polls.Add($"UPS status: {status}{chargeStr}{runtimeStr}");

        var isOnBattery = status.Contains("OB");
        var isLowBattery = status.Contains("LB");
        var isForcedShutdown = status.Contains("FSD");

        // Transition: AC power → on battery
        if (isOnBattery && !_onBattery)
        {
            _onBattery = true;
            _batteryStartTime = _clock.GetUtcNow();
            _shutdownInitiated = false;
            _currentState = "On Battery";
            events.Add($"POWER LOSS DETECTED — UPS on battery (status: {status})");
            events.Add($"Shutdown will begin in {_config.ShutdownDelaySeconds} seconds if power is not restored");
            AddHistory(status, "power loss detected");
        }
        // Transition: on battery → AC power restored
        else if (!isOnBattery && _onBattery)
        {
            _onBattery = false;
            _batteryStartTime = null;
            _currentState = "Online";
            events.Add($"POWER RESTORED — UPS back online (status: {status})");
            AddHistory(status, "power restored");

            if (!_shutdownInitiated)
                events.Add("Shutdown cancelled — power returned in time");
        }
        else
        {
            _currentState = isOnBattery ? "On Battery" : "Online";
            AddHistory(status, "poll");
        }

        // FSD or LB — immediate shutdown
        if ((isLowBattery || isForcedShutdown) && !_shutdownInitiated)
        {
            var reason = isForcedShutdown ? "forced_shutdown" : "low_battery";
            _currentState = "Shutting Down";
            events.Add($"{reason.ToUpper()} — immediate shutdown (status: {status})");
            AddHistory(status, reason);
            shutdown = new ShutdownAction(reason, data);
            _shutdownInitiated = true;
            return new StatusDecision(shutdown, events, polls, _currentState);
        }

        // Threshold checks — only when on battery
        if (_onBattery && !_shutdownInitiated)
        {
            if (_config.BatteryChargePercent.HasValue
                && data.BatteryCharge.HasValue
                && data.BatteryCharge.Value <= _config.BatteryChargePercent.Value)
            {
                _currentState = "Shutting Down";
                events.Add($"BATTERY CHARGE {data.BatteryCharge}% at or below threshold ({_config.BatteryChargePercent}%) — initiating shutdown");
                AddHistory(status, $"battery_charge <= {_config.BatteryChargePercent}%");
                shutdown = new ShutdownAction("battery_charge", data);
                _shutdownInitiated = true;
                return new StatusDecision(shutdown, events, polls, _currentState);
            }

            if (_config.BatteryRuntimeSeconds.HasValue
                && data.BatteryRuntime.HasValue
                && data.BatteryRuntime.Value <= _config.BatteryRuntimeSeconds.Value)
            {
                _currentState = "Shutting Down";
                events.Add($"BATTERY RUNTIME {data.BatteryRuntime}s at or below threshold ({_config.BatteryRuntimeSeconds}s) — initiating shutdown");
                AddHistory(status, $"battery_runtime <= {_config.BatteryRuntimeSeconds}s");
                shutdown = new ShutdownAction("battery_runtime", data);
                _shutdownInitiated = true;
                return new StatusDecision(shutdown, events, polls, _currentState);
            }
        }

        // Timer expiry
        if (_onBattery && !_shutdownInitiated && _batteryStartTime.HasValue)
        {
            var elapsed = (_clock.GetUtcNow() - _batteryStartTime.Value).TotalSeconds;
            if (elapsed >= _config.ShutdownDelaySeconds)
            {
                _currentState = "Shutting Down";
                events.Add($"Shutdown delay of {_config.ShutdownDelaySeconds}s elapsed — initiating shutdown");
                AddHistory(status, "timer_expired");
                shutdown = new ShutdownAction("timer_expired", data);
                _shutdownInitiated = true;
            }
            else
            {
                var remaining = _config.ShutdownDelaySeconds - (int)elapsed;
                // Countdown is important — log as event so it always shows during battery
                events.Add($"On battery for {(int)elapsed}s — shutdown in {remaining}s");
            }
        }

        // Warning-only checks
        if (_config.InputVoltageMinWarn.HasValue
            && data.InputVoltage.HasValue
            && data.InputVoltage.Value < _config.InputVoltageMinWarn.Value)
        {
            events.Add($"WARNING: Input voltage {data.InputVoltage:F1}V below threshold ({_config.InputVoltageMinWarn}V)");
        }

        if (_config.LoadPercentWarn.HasValue
            && data.Load.HasValue
            && data.Load.Value > _config.LoadPercentWarn.Value)
        {
            events.Add($"WARNING: UPS load {data.Load}% exceeds threshold ({_config.LoadPercentWarn}%)");
        }

        return new StatusDecision(shutdown, events, polls, _currentState);
    }

    public PollDecision OnPollSuccess()
    {
        var events = new List<string>();
        if (_consecutiveFailures > 0)
            events.Add($"Connection restored after {_consecutiveFailures} failed poll(s)");

        _consecutiveFailures = 0;
        _lastSuccessfulPoll = _clock.GetUtcNow();
        return new PollDecision(null, events, new List<string>());
    }

    public PollDecision OnPollFailure(string message)
    {
        var events = new List<string>();
        ShutdownAction? shutdown = null;

        _consecutiveFailures++;
        _currentState = "Error";
        AddHistory("error", message);

        if (_consecutiveFailures <= 3 || _consecutiveFailures % 10 == 0)
            events.Add($"Poll error ({_consecutiveFailures} consecutive): {message}");

        if (_lastSuccessfulPoll.HasValue)
        {
            var downtime = (_clock.GetUtcNow() - _lastSuccessfulPoll.Value).TotalSeconds;
            if (_consecutiveFailures == 6)
                events.Add($"WARNING: UPS server unreachable for {(int)downtime}s");
        }
        else if (_consecutiveFailures == 6)
        {
            events.Add("WARNING: UPS server has never been reachable since startup");
        }

        // Dead time check
        if (_onBattery && !_shutdownInitiated && _lastSuccessfulPoll.HasValue)
        {
            var downtime = (_clock.GetUtcNow() - _lastSuccessfulPoll.Value).TotalSeconds;
            if (downtime >= _config.DeadTimeSeconds)
            {
                _currentState = "Shutting Down";
                events.Add($"DEAD TIME — server unreachable for {(int)downtime}s while on battery, assuming power still out — initiating shutdown");
                AddHistory("error", "dead_time");
                shutdown = new ShutdownAction("dead_time", _lastUpsData);
                _shutdownInitiated = true;
            }
        }

        return new PollDecision(shutdown, events, new List<string>());
    }

    public void SetAccessDenied()
    {
        _accessDenied = true;
    }

    public TimeSpan GetPollDelay()
    {
        if (_consecutiveFailures == 0)
            return TimeSpan.FromSeconds(_config.PollIntervalSeconds);

        var backoffSeconds = Math.Min(
            _config.PollIntervalSeconds * (1 << Math.Min(_consecutiveFailures, 6)),
            MaxBackoffSeconds);
        return TimeSpan.FromSeconds(backoffSeconds);
    }

    public StatusSnapshot GetStatusSnapshot(string serverHost, int serverPort, string upsName)
    {
        int? shutdownIn = null;
        if (_onBattery && !_shutdownInitiated && _batteryStartTime.HasValue)
        {
            var elapsed = (_clock.GetUtcNow() - _batteryStartTime.Value).TotalSeconds;
            shutdownIn = Math.Max(0, _config.ShutdownDelaySeconds - (int)elapsed);
        }

        return new StatusSnapshot
        {
            Server = $"{serverHost}:{serverPort}",
            UpsName = upsName,
            State = _accessDenied ? "Access Denied" : _currentState,
            CurrentStatus = _currentUpsStatus,
            BatteryCharge = _lastUpsData.BatteryCharge,
            BatteryRuntime = _lastUpsData.BatteryRuntime,
            InputVoltage = _lastUpsData.InputVoltage,
            UpsLoad = _lastUpsData.Load,
            LastPoll = _lastSuccessfulPoll?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            ConsecutiveFailures = _consecutiveFailures,
            OnBatterySince = _batteryStartTime?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            ShutdownInSeconds = shutdownIn,
            History = _history.ToList()
        };
    }

    private void AddHistory(string status, string eventDesc)
    {
        _history.Insert(0, new StatusHistoryEntry
        {
            Time = _clock.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss"),
            Status = status,
            Event = eventDesc
        });

        if (_history.Count > MaxHistoryEntries)
            _history.RemoveAt(_history.Count - 1);
    }
}

public record ShutdownAction(string Reason, UpsData Data);
public record StatusDecision(ShutdownAction? Shutdown, List<string> EventMessages, List<string> PollMessages, string NewState);
public record PollDecision(ShutdownAction? Shutdown, List<string> EventMessages, List<string> PollMessages);
