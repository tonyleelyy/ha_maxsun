using HaMaxsun.Core;

namespace HaMaxsun.Service;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var cli = CliOptions.Parse(args);
        var config = BridgeOptions.Load(cli.ConfigPath, requireHomeAssistant: !cli.OnceProbe && cli.OnceApply is null);
        using var logger = new BridgeLogger(config.Bridge.GetLogDirectory());

        if (cli.OnceProbe)
        {
            if (ReportConflictsForOnceCommand())
            {
                return 3;
            }

            var helper = new HalHelperClient(config, logger);
            var result = await helper.ProbeAsync(CancellationToken.None);
            Console.WriteLine(result.Ok
                ? $"OK: {result.DeviceName} {result.Guid} LEDs={result.LedCount}"
                : $"ERROR: {result.Error}");
            return result.Ok ? 0 : 2;
        }

        if (cli.OnceApply is not null)
        {
            if (ReportConflictsForOnceCommand())
            {
                return 3;
            }

            var helper = new HalHelperClient(config, logger);
            var result = await helper.ApplyAsync(cli.OnceApply, CancellationToken.None);
            Console.WriteLine(result.Ok
                ? $"OK: applied {string.Join(',', result.AppliedRgb ?? [])}"
                : $"ERROR: {result.Error}");
            return result.Ok ? 0 : 2;
        }

        var runAsService = cli.Service || !Environment.UserInteractive;
        if (runAsService)
        {
            return new BridgeWindowsService(config, logger).Run();
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        await new BridgeRunner(config, logger).RunAsync(cts.Token);
        return 0;
    }

    private static bool ReportConflictsForOnceCommand()
    {
        var conflicts = ProcessConflictDetector.GetConflicts();
        if (conflicts.Count == 0)
        {
            return false;
        }

        Console.WriteLine($"ERROR: MaxsunSync conflict detected: {string.Join(", ", conflicts)}. Stop MaxsunSync2/MaxsunSyncService before hardware testing.");
        return true;
    }
}

internal sealed class CliOptions
{
    public string? ConfigPath { get; private init; }
    public bool Service { get; private init; }
    public bool OnceProbe { get; private init; }
    public LightState? OnceApply { get; private init; }

    public static CliOptions Parse(string[] args)
    {
        string? configPath = null;
        var service = false;
        var onceProbe = false;
        LightState? onceApply = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--config", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                configPath = args[++i];
            }
            else if (arg.Equals("--service", StringComparison.OrdinalIgnoreCase))
            {
                service = true;
            }
            else if (arg.Equals("--once-probe", StringComparison.OrdinalIgnoreCase))
            {
                onceProbe = true;
            }
            else if (arg.Equals("--once-apply", StringComparison.OrdinalIgnoreCase))
            {
                var on = true;
                var color = new RgbColor(255, 255, 255);
                var brightness = 255;
                while (i + 1 < args.Length)
                {
                    var next = args[i + 1];
                    if (next.StartsWith("--", StringComparison.Ordinal))
                    {
                        break;
                    }

                    i++;
                }

                for (var j = 0; j < args.Length; j++)
                {
                    if (args[j].Equals("--off", StringComparison.OrdinalIgnoreCase))
                    {
                        on = false;
                    }
                    else if (args[j].Equals("--rgb", StringComparison.OrdinalIgnoreCase) && j + 1 < args.Length)
                    {
                        ColorParser.TryParseRgb(args[j + 1], out color);
                    }
                    else if (args[j].Equals("--brightness", StringComparison.OrdinalIgnoreCase) && j + 1 < args.Length)
                    {
                        int.TryParse(args[j + 1], out brightness);
                    }
                }

                onceApply = new LightState(on, color, brightness);
            }
        }

        return new CliOptions
        {
            ConfigPath = configPath,
            Service = service,
            OnceProbe = onceProbe,
            OnceApply = onceApply
        };
    }
}

