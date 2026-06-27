using System.Text.Json;
using HaMaxsun.Core;

namespace HaMaxsun.HalHelper;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        var options = HalRuntimeOptions.FromEnvironment();
        using var controller = new AuraHalController(options);

        if (args.Contains("--stdio", StringComparer.OrdinalIgnoreCase))
        {
            await RunStdioAsync(controller);
            return 0;
        }

        var request = ParseCli(args);
        var response = Execute(controller, request);
        Console.WriteLine(JsonSerializer.Serialize(response, JsonOptions));
        return response.Ok ? 0 : 2;
    }

    private static async Task RunStdioAsync(AuraHalController controller)
    {
        while (await Console.In.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            HalResponse response;
            try
            {
                var request = JsonSerializer.Deserialize<HalRequest>(line, JsonOptions)
                    ?? throw new InvalidOperationException("Empty request.");
                response = Execute(controller, request);
            }
            catch (Exception ex)
            {
                response = HalResponse.Failure(ex.Message);
            }

            Console.WriteLine(JsonSerializer.Serialize(response, JsonOptions));
        }
    }

    private static HalResponse Execute(AuraHalController controller, HalRequest request)
    {
        try
        {
            return request.Command.ToLowerInvariant() switch
            {
                "probe" => controller.Probe(),
                "apply" => controller.Apply(new LightState(
                    request.On,
                    request.GetRgbOrDefault(new RgbColor(255, 255, 255)),
                    request.Brightness)),
                _ => HalResponse.Failure($"Unknown command '{request.Command}'.")
            };
        }
        catch (Exception ex)
        {
            return HalResponse.Failure(ex.ToString());
        }
    }

    private static HalRequest ParseCli(string[] args)
    {
        if (args.Length == 0 || args[0].Equals("probe", StringComparison.OrdinalIgnoreCase))
        {
            return HalRequest.Probe();
        }

        if (!args[0].Equals("apply", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Use 'probe' or 'apply'.");
        }

        var on = true;
        var color = new RgbColor(255, 255, 255);
        var brightness = 255;

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--off", StringComparison.OrdinalIgnoreCase))
            {
                on = false;
            }
            else if (arg.Equals("--on", StringComparison.OrdinalIgnoreCase))
            {
                on = true;
            }
            else if (arg.Equals("--rgb", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (!ColorParser.TryParseRgb(args[++i], out color))
                {
                    throw new ArgumentException("--rgb must be formatted as r,g,b.");
                }
            }
            else if (arg.Equals("--brightness", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                brightness = int.Parse(args[++i]);
            }
        }

        return HalRequest.Apply(new LightState(on, color, brightness));
    }
}

