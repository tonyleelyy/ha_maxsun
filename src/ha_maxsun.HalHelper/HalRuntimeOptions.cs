namespace HaMaxsun.HalHelper;

internal sealed class HalRuntimeOptions
{
    public Guid TargetHalGuid { get; init; } = Guid.Parse("9d590787-6015-445d-9076-30b360cdf24b");
    public string ExpectedDeviceName { get; init; } = "MAXSUN MOTHERBOARD LED ENE";
    public int ExpectedLedCount { get; init; } = 264;
    public string AuraSdkDirectory { get; init; } = @"C:\Program Files\ASUS\AuraSDK";
    public string MaxsunHalDirectory { get; init; } = @"C:\Program Files\MaxSun\LightControlModule\Aac_MaxSunEneLight";
    public string EneHalDirectory { get; init; } = @"C:\Program Files\ENE\Aac_ENE RGB HAL\x64";

    public static HalRuntimeOptions FromEnvironment()
    {
        var options = new HalRuntimeOptions();
        return new HalRuntimeOptions
        {
            TargetHalGuid = ReadGuid("MAXSUN_TARGET_HAL_GUID", options.TargetHalGuid),
            ExpectedDeviceName = ReadString("MAXSUN_EXPECTED_DEVICE_NAME", options.ExpectedDeviceName),
            ExpectedLedCount = ReadInt("MAXSUN_EXPECTED_LED_COUNT", options.ExpectedLedCount),
            AuraSdkDirectory = ReadString("MAXSUN_AURA_SDK_DIR", options.AuraSdkDirectory),
            MaxsunHalDirectory = ReadString("MAXSUN_HAL_DIR", options.MaxsunHalDirectory),
            EneHalDirectory = ReadString("MAXSUN_ENE_HAL_DIR", options.EneHalDirectory)
        };
    }

    private static string ReadString(string name, string fallback)
        => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))
            ? fallback
            : Environment.GetEnvironmentVariable(name)!;

    private static int ReadInt(string name, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;

    private static Guid ReadGuid(string name, Guid fallback)
        => Guid.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;
}

