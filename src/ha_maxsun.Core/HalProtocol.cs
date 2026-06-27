using System.Text.Json.Serialization;

namespace HaMaxsun.Core;

public sealed class HalRequest
{
    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("on")]
    public bool On { get; set; }

    [JsonPropertyName("rgb")]
    public int[] Rgb { get; set; } = [255, 255, 255];

    [JsonPropertyName("brightness")]
    public int Brightness { get; set; } = 255;

    public static HalRequest Probe() => new() { Command = "probe" };

    public static HalRequest Apply(LightState state) => new()
    {
        Command = "apply",
        On = state.On,
        Rgb = state.Color.ToArray(),
        Brightness = state.Brightness
    };

    public RgbColor GetRgbOrDefault(RgbColor fallback)
    {
        if (Rgb.Length != 3)
        {
            return fallback;
        }

        return new RgbColor(
            ColorMath.ClampByte(Rgb[0]),
            ColorMath.ClampByte(Rgb[1]),
            ColorMath.ClampByte(Rgb[2]));
    }
}

public sealed class HalResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("deviceName")]
    public string? DeviceName { get; set; }

    [JsonPropertyName("guid")]
    public string? Guid { get; set; }

    [JsonPropertyName("ledCount")]
    public int? LedCount { get; set; }

    [JsonPropertyName("appliedRgb")]
    public int[]? AppliedRgb { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    public static HalResponse SuccessProbe(string deviceName, string guid, int ledCount) => new()
    {
        Ok = true,
        DeviceName = deviceName,
        Guid = guid,
        LedCount = ledCount
    };

    public static HalResponse SuccessApply(RgbColor applied) => new()
    {
        Ok = true,
        AppliedRgb = applied.ToArray()
    };

    public static HalResponse Failure(string error) => new()
    {
        Ok = false,
        Error = error
    };
}

