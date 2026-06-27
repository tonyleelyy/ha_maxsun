using System.Text.Json;
using HaMaxsun.Core;

namespace HaMaxsun.Service;

internal sealed class BridgeOptions
{
    public HomeAssistantOptions HomeAssistant { get; set; } = new();
    public EntityIds Entities { get; set; } = new();
    public HalOptions Hal { get; set; } = new();
    public BridgeRuntimeOptions Bridge { get; set; } = new();

    public static BridgeOptions Load(string? path, bool requireHomeAssistant = true)
    {
        var configPath = ResolveConfigPath(path);
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException(
                $"Configuration file not found. Copy appsettings.example.json to appsettings.json and edit it first. Looked for: {configPath}",
                configPath);
        }

        var json = File.ReadAllText(configPath);
        var options = JsonSerializer.Deserialize<BridgeOptions>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Configuration file is empty.");
        options.Validate(configPath, requireHomeAssistant);
        return options;
    }

    private static string ResolveConfigPath(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            return Path.GetFullPath(path);
        }

        var basePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        return File.Exists(basePath) ? basePath : Path.GetFullPath("appsettings.json");
    }

    private void Validate(string configPath, bool requireHomeAssistant)
    {
        if (!requireHomeAssistant)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(HomeAssistant.WebSocketUrl))
        {
            throw new InvalidOperationException($"{configPath}: homeAssistant.webSocketUrl is required.");
        }

        if (string.IsNullOrWhiteSpace(HomeAssistant.LongLivedAccessToken) ||
            HomeAssistant.LongLivedAccessToken.Contains("REPLACE", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{configPath}: homeAssistant.longLivedAccessToken must be set.");
        }
    }
}

internal sealed class HomeAssistantOptions
{
    public string WebSocketUrl { get; set; } = "ws://homeassistant.local:8123/api/websocket";
    public string LongLivedAccessToken { get; set; } = "REPLACE_ME";
    public int RequestTimeoutSeconds { get; set; } = 15;
}

internal sealed class HalOptions
{
    public string HelperPath { get; set; } = "ha_maxsun.HalHelper.dll";
    public string TargetHalGuid { get; set; } = "9d590787-6015-445d-9076-30b360cdf24b";
    public string ExpectedDeviceName { get; set; } = "MAXSUN MOTHERBOARD LED ENE";
    public int ExpectedLedCount { get; set; } = 264;
    public string AuraSdkDirectory { get; set; } = @"C:\Program Files\ASUS\AuraSDK";
    public string MaxsunHalDirectory { get; set; } = @"C:\Program Files\MaxSun\LightControlModule\Aac_MaxSunEneLight";
    public string EneHalDirectory { get; set; } = @"C:\Program Files\ENE\Aac_ENE RGB HAL\x64";
    public int HelperTimeoutSeconds { get; set; } = 20;
}

internal sealed class BridgeRuntimeOptions
{
    public int ReconnectDelaySeconds { get; set; } = 10;
    public int ResumeDelayMilliseconds { get; set; } = 15000;
    public string LogDirectory { get; set; } = "logs";
    public bool ApplyOnStartup { get; set; } = true;

    public string GetLogDirectory()
    {
        if (Path.IsPathRooted(LogDirectory))
        {
            return LogDirectory;
        }

        return Path.Combine(AppContext.BaseDirectory, LogDirectory);
    }
}

