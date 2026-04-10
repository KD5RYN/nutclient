using System.Text.Json.Serialization;

namespace NutClient;

public class UpsData
{
    public string Status { get; set; } = "";
    public int? BatteryCharge { get; set; }
    public int? BatteryRuntime { get; set; }
    public double? InputVoltage { get; set; }
    public int? Load { get; set; }
}

// Status file models — serialized to nutclient-status.json
public class StatusSnapshot
{
    [JsonPropertyName("server")]
    public string Server { get; set; } = "";

    [JsonPropertyName("upsName")]
    public string UpsName { get; set; } = "";

    [JsonPropertyName("state")]
    public string State { get; set; } = "";

    [JsonPropertyName("currentStatus")]
    public string CurrentStatus { get; set; } = "";

    [JsonPropertyName("batteryCharge")]
    public int? BatteryCharge { get; set; }

    [JsonPropertyName("batteryRuntime")]
    public int? BatteryRuntime { get; set; }

    [JsonPropertyName("inputVoltage")]
    public double? InputVoltage { get; set; }

    [JsonPropertyName("upsLoad")]
    public int? UpsLoad { get; set; }

    [JsonPropertyName("lastPoll")]
    public string? LastPoll { get; set; }

    [JsonPropertyName("consecutiveFailures")]
    public int ConsecutiveFailures { get; set; }

    [JsonPropertyName("onBatterySince")]
    public string? OnBatterySince { get; set; }

    [JsonPropertyName("shutdownInSeconds")]
    public int? ShutdownInSeconds { get; set; }

    [JsonPropertyName("history")]
    public List<StatusHistoryEntry> History { get; set; } = new();
}

public class StatusHistoryEntry
{
    [JsonPropertyName("time")]
    public string Time { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("event")]
    public string Event { get; set; } = "";
}
