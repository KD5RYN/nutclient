namespace NutClient;

public class Config
{
    public NutServerConfig NutServer { get; set; } = new();
    public MonitoringConfig Monitoring { get; set; } = new();
}

public class NutServerConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 3493;
    public string UpsName { get; set; } = "ups1";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class MonitoringConfig
{
    public int PollIntervalSeconds { get; set; } = 5;
    public int ShutdownDelaySeconds { get; set; } = 60;
    public string ShutdownCommand { get; set; } = "shutdown.exe";
    public string ShutdownArguments { get; set; } = "/s /t 0 /f";
    public string LogFile { get; set; } = "C:\\Scripts\\nutclient.log";
    public string StatusFile { get; set; } = "";

    // Optional thresholds — null means disabled
    public int? BatteryChargePercent { get; set; }     // Shutdown when charge drops below this (e.g., 20)
    public int? BatteryRuntimeSeconds { get; set; }    // Shutdown when runtime drops below this (e.g., 120)
    public int? InputVoltageMinWarn { get; set; }      // Log warning when input voltage drops below this (e.g., 100)
    public int? LoadPercentWarn { get; set; }           // Log warning when load exceeds this (e.g., 80)

    // Dead time — shutdown if server unreachable while last known on battery
    public int DeadTimeSeconds { get; set; } = 30;

    // Pre-shutdown hook — runs before the main shutdown command
    public string? PreShutdownCommand { get; set; }
    public string? PreShutdownArguments { get; set; }
    public int PreShutdownDelaySeconds { get; set; } = 5;  // Wait between pre-shutdown and shutdown

    // Log rotation
    public long LogMaxBytes { get; set; } = 1_048_576;  // 1 MB default
}
