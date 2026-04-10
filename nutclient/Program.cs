using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NutClient;

// Load config
var exeDir = AppContext.BaseDirectory;
var configPath = Path.Combine(exeDir, "nutclient.json");
if (!File.Exists(configPath))
    throw new FileNotFoundException($"Config file not found: {configPath}");

var json = File.ReadAllText(configPath);
var config = JsonSerializer.Deserialize<Config>(json)
    ?? throw new Exception("Failed to parse nutclient.json");

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
