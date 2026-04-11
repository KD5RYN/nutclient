using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NutClient;

// Load config — fail fast with a clean message if missing or malformed.
// Stack traces are useless to admins; tell them what to fix.
var exeDir = AppContext.BaseDirectory;
var configPath = Path.Combine(exeDir, "nutclient.json");

if (!File.Exists(configPath))
{
    Console.Error.WriteLine($"ERROR: Config file not found: {configPath}");
    Console.Error.WriteLine("Copy nutclient.json (Windows) or nutclient.json.linux-example (Linux)");
    Console.Error.WriteLine("from the release archive into the same directory as the binary.");
    Environment.Exit(1);
}

Config config;
try
{
    var json = File.ReadAllText(configPath);
    config = JsonSerializer.Deserialize<Config>(json)
        ?? throw new Exception("config deserialized to null");
}
catch (JsonException ex)
{
    Console.Error.WriteLine($"ERROR: {configPath} is not valid JSON.");
    Console.Error.WriteLine($"  {ex.Message}");
    Console.Error.WriteLine("Fix the file (validate with `python3 -m json.tool nutclient.json`) and try again.");
    Environment.Exit(1);
    return; // unreachable but the compiler doesn't know that
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: Failed to load {configPath}: {ex.Message}");
    Environment.Exit(1);
    return;
}

var builder = Host.CreateApplicationBuilder(args);

if (OperatingSystem.IsWindows())
{
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "NUT UPS Monitor";
    });
}
else
{
    builder.Services.AddSystemd();
}

builder.Services.AddSingleton(config);
builder.Services.AddHostedService<NutMonitorService>();

var host = builder.Build();
host.Run();
