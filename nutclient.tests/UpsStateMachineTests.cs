using Microsoft.Extensions.Time.Testing;

namespace NutClient.Tests;

public class UpsStateMachineTests
{
    private readonly FakeTimeProvider _clock = new();
    private readonly MonitoringConfig _config;
    private readonly UpsStateMachine _sm;

    public UpsStateMachineTests()
    {
        _config = new MonitoringConfig
        {
            PollIntervalSeconds = 5,
            ShutdownDelaySeconds = 60,
            DeadTimeSeconds = 30,
        };
        _sm = new UpsStateMachine(_config, _clock);
    }

    private static UpsData Status(string status, int? charge = null, int? runtime = null) =>
        new() { Status = status, BatteryCharge = charge, BatteryRuntime = runtime };

    // --- State transitions ---

    [Fact]
    public void InitialState_IsStarting()
    {
        Assert.Equal("Starting", _sm.CurrentState);
    }

    [Fact]
    public void OnlineStatus_SetsOnlineState()
    {
        var decision = _sm.HandleStatus(Status("OL"));
        Assert.Equal("Online", decision.NewState);
        Assert.Null(decision.Shutdown);
    }

    [Fact]
    public void OB_TransitionsToOnBattery()
    {
        var decision = _sm.HandleStatus(Status("OB"));
        Assert.Equal("On Battery", decision.NewState);
        Assert.True(_sm.IsOnBattery);
        Assert.Null(decision.Shutdown);
    }

    [Fact]
    public void OB_Then_OL_RestoresPower()
    {
        _sm.HandleStatus(Status("OB"));
        var decision = _sm.HandleStatus(Status("OL"));

        Assert.Equal("Online", decision.NewState);
        Assert.False(_sm.IsOnBattery);
        Assert.Null(decision.Shutdown);
        Assert.Contains(decision.EventMessages, m => m.Contains("POWER RESTORED"));
    }

    [Fact]
    public void OB_Then_OL_CancelsShutdown()
    {
        _sm.HandleStatus(Status("OB"));
        var decision = _sm.HandleStatus(Status("OL"));

        Assert.Contains(decision.EventMessages, m => m.Contains("Shutdown cancelled"));
    }

    // --- Immediate shutdown triggers ---

    [Fact]
    public void LB_TriggersImmediateShutdown()
    {
        var decision = _sm.HandleStatus(Status("OB LB"));
        Assert.NotNull(decision.Shutdown);
        Assert.Equal("low_battery", decision.Shutdown.Reason);
        Assert.Equal("Shutting Down", decision.NewState);
    }

    [Fact]
    public void FSD_TriggersImmediateShutdown()
    {
        var decision = _sm.HandleStatus(Status("FSD"));
        Assert.NotNull(decision.Shutdown);
        Assert.Equal("forced_shutdown", decision.Shutdown.Reason);
    }

    [Fact]
    public void LB_WithoutPriorOB_StillTriggersShutdown()
    {
        // Edge case: LB flag without OB (shouldn't happen, but be safe)
        var decision = _sm.HandleStatus(Status("OL LB"));
        Assert.NotNull(decision.Shutdown);
        Assert.Equal("low_battery", decision.Shutdown.Reason);
    }

    // --- Timer expiry ---

    [Fact]
    public void TimerExpiry_TriggersShutdown()
    {
        _sm.HandleStatus(Status("OB"));

        _clock.Advance(TimeSpan.FromSeconds(61));

        var decision = _sm.HandleStatus(Status("OB"));
        Assert.NotNull(decision.Shutdown);
        Assert.Equal("timer_expired", decision.Shutdown.Reason);
    }

    [Fact]
    public void TimerNotExpired_NoShutdown()
    {
        _sm.HandleStatus(Status("OB"));

        _clock.Advance(TimeSpan.FromSeconds(30));

        var decision = _sm.HandleStatus(Status("OB"));
        Assert.Null(decision.Shutdown);
        Assert.Contains(decision.EventMessages, m => m.Contains("shutdown in"));
    }

    [Fact]
    public void TimerExpiry_ExactBoundary()
    {
        _sm.HandleStatus(Status("OB"));
        _clock.Advance(TimeSpan.FromSeconds(60));

        var decision = _sm.HandleStatus(Status("OB"));
        Assert.NotNull(decision.Shutdown);
        Assert.Equal("timer_expired", decision.Shutdown.Reason);
    }

    // --- Thresholds ---

    [Fact]
    public void BatteryChargeThreshold_TriggersShutdown()
    {
        _config.BatteryChargePercent = 20;
        _sm.HandleStatus(Status("OB", charge: 15));

        // HandleStatus already returned the decision, but let's verify via state
        Assert.True(_sm.IsShutdownInitiated);
    }

    [Fact]
    public void BatteryChargeThreshold_AtThreshold_TriggersShutdown()
    {
        _config.BatteryChargePercent = 20;
        var decision = _sm.HandleStatus(Status("OB", charge: 20));
        Assert.NotNull(decision.Shutdown);
        Assert.Equal("battery_charge", decision.Shutdown.Reason);
    }

    [Fact]
    public void BatteryChargeThreshold_AboveThreshold_NoShutdown()
    {
        _config.BatteryChargePercent = 20;
        var decision = _sm.HandleStatus(Status("OB", charge: 25));
        Assert.Null(decision.Shutdown);
    }

    [Fact]
    public void BatteryChargeThreshold_OnAC_NoShutdown()
    {
        _config.BatteryChargePercent = 20;
        var decision = _sm.HandleStatus(Status("OL", charge: 15));
        Assert.Null(decision.Shutdown);
    }

    [Fact]
    public void BatteryRuntimeThreshold_TriggersShutdown()
    {
        _config.BatteryRuntimeSeconds = 120;
        var decision = _sm.HandleStatus(Status("OB", runtime: 60));
        Assert.NotNull(decision.Shutdown);
        Assert.Equal("battery_runtime", decision.Shutdown.Reason);
    }

    [Fact]
    public void BatteryRuntimeThreshold_AboveThreshold_NoShutdown()
    {
        _config.BatteryRuntimeSeconds = 120;
        var decision = _sm.HandleStatus(Status("OB", runtime: 300));
        Assert.Null(decision.Shutdown);
    }

    // --- Shutdown only fires once ---

    [Fact]
    public void ShutdownOnlyOnce()
    {
        var d1 = _sm.HandleStatus(Status("OB LB"));
        Assert.NotNull(d1.Shutdown);

        var d2 = _sm.HandleStatus(Status("OB LB"));
        Assert.Null(d2.Shutdown);
    }

    // --- Warning checks ---

    [Fact]
    public void LowInputVoltage_GeneratesWarning()
    {
        _config.InputVoltageMinWarn = 100;
        var decision = _sm.HandleStatus(new UpsData
        {
            Status = "OL", InputVoltage = 95.5
        });
        Assert.Null(decision.Shutdown);
        Assert.Contains(decision.EventMessages, m => m.Contains("WARNING: Input voltage"));
    }

    [Fact]
    public void HighLoad_GeneratesWarning()
    {
        _config.LoadPercentWarn = 80;
        var decision = _sm.HandleStatus(new UpsData
        {
            Status = "OL", Load = 90
        });
        Assert.Null(decision.Shutdown);
        Assert.Contains(decision.EventMessages, m => m.Contains("WARNING: UPS load"));
    }

    [Fact]
    public void NormalVoltage_NoWarning()
    {
        _config.InputVoltageMinWarn = 100;
        var decision = _sm.HandleStatus(new UpsData
        {
            Status = "OL", InputVoltage = 120.0
        });
        Assert.DoesNotContain(decision.EventMessages, m => m.Contains("WARNING"));
    }

    // --- Poll success/failure ---

    [Fact]
    public void OnPollSuccess_ResetsConsecutiveFailures()
    {
        _sm.OnPollFailure("error 1");
        _sm.OnPollFailure("error 2");
        Assert.Equal(2, _sm.ConsecutiveFailures);

        _sm.OnPollSuccess();
        Assert.Equal(0, _sm.ConsecutiveFailures);
    }

    [Fact]
    public void OnPollSuccess_LogsRestoredAfterFailure()
    {
        _sm.OnPollFailure("error");
        var decision = _sm.OnPollSuccess();
        Assert.Contains(decision.EventMessages, m => m.Contains("Connection restored"));
    }

    [Fact]
    public void OnPollSuccess_NoMessageWhenNoFailures()
    {
        var decision = _sm.OnPollSuccess();
        Assert.Empty(decision.EventMessages);
    }

    [Fact]
    public void OnPollFailure_IncrementsCounter()
    {
        _sm.OnPollFailure("err");
        Assert.Equal(1, _sm.ConsecutiveFailures);
        _sm.OnPollFailure("err");
        Assert.Equal(2, _sm.ConsecutiveFailures);
    }

    // --- Dead time ---

    [Fact]
    public void DeadTime_OnBatteryAndUnreachable_TriggersShutdown()
    {
        // Go on battery
        _sm.HandleStatus(Status("OB", charge: 80, runtime: 300));
        _sm.OnPollSuccess(); // record last successful poll

        // Server becomes unreachable, time passes
        _clock.Advance(TimeSpan.FromSeconds(31));
        var decision = _sm.OnPollFailure("Connection timed out");

        Assert.NotNull(decision.Shutdown);
        Assert.Equal("dead_time", decision.Shutdown.Reason);
    }

    [Fact]
    public void DeadTime_OnlineAndUnreachable_NoShutdown()
    {
        _sm.HandleStatus(Status("OL"));
        _sm.OnPollSuccess();

        _clock.Advance(TimeSpan.FromSeconds(60));
        var decision = _sm.OnPollFailure("Connection timed out");

        Assert.Null(decision.Shutdown);
    }

    [Fact]
    public void DeadTime_NotEnoughElapsed_NoShutdown()
    {
        _sm.HandleStatus(Status("OB"));
        _sm.OnPollSuccess();

        _clock.Advance(TimeSpan.FromSeconds(15)); // less than DeadTimeSeconds=30
        var decision = _sm.OnPollFailure("Connection timed out");

        Assert.Null(decision.Shutdown);
    }

    // --- Combined status flags ---

    [Fact]
    public void OL_CHRG_IsOnline()
    {
        var decision = _sm.HandleStatus(Status("OL CHRG"));
        Assert.Equal("Online", decision.NewState);
        Assert.Null(decision.Shutdown);
    }

    [Fact]
    public void OB_DISCHRG_IsOnBattery()
    {
        var decision = _sm.HandleStatus(Status("OB DISCHRG"));
        Assert.Equal("On Battery", decision.NewState);
        Assert.True(_sm.IsOnBattery);
    }

    [Fact]
    public void OB_LB_DISCHRG_TriggersShutdown()
    {
        var decision = _sm.HandleStatus(Status("OB LB DISCHRG"));
        Assert.NotNull(decision.Shutdown);
        Assert.Equal("low_battery", decision.Shutdown.Reason);
    }

    // --- History ---

    [Fact]
    public void History_CapsAt25()
    {
        for (int i = 0; i < 30; i++)
            _sm.HandleStatus(Status("OL"));

        Assert.Equal(25, _sm.History.Count);
    }

    [Fact]
    public void History_NewestFirst()
    {
        _sm.HandleStatus(Status("OL"));
        _clock.Advance(TimeSpan.FromSeconds(5));
        _sm.HandleStatus(Status("OB"));

        Assert.Equal("OB", _sm.History[0].Status);
        Assert.Equal("OL", _sm.History[1].Status);
    }
}
