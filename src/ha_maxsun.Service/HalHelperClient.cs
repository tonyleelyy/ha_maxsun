using System.Diagnostics;
using System.Text.Json;
using HaMaxsun.Core;

namespace HaMaxsun.Service;

internal interface IHalClient
{
    Task<HalResponse> ProbeAsync(CancellationToken cancellationToken);
    Task<HalResponse> ApplyAsync(LightState state, CancellationToken cancellationToken);
}

internal sealed class HalHelperClient : IHalClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly BridgeOptions _options;
    private readonly BridgeLogger _logger;

    public HalHelperClient(BridgeOptions options, BridgeLogger logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task<HalResponse> ProbeAsync(CancellationToken cancellationToken)
        => SendAsync(HalRequest.Probe(), cancellationToken);

    public Task<HalResponse> ApplyAsync(LightState state, CancellationToken cancellationToken)
        => SendAsync(HalRequest.Apply(state), cancellationToken);

    private async Task<HalResponse> SendAsync(HalRequest request, CancellationToken cancellationToken)
    {
        var helperPath = ResolveHelperPath();
        var startInfo = CreateStartInfo(helperPath);

        startInfo.Environment["MAXSUN_TARGET_HAL_GUID"] = _options.Hal.TargetHalGuid;
        startInfo.Environment["MAXSUN_EXPECTED_DEVICE_NAME"] = _options.Hal.ExpectedDeviceName;
        startInfo.Environment["MAXSUN_EXPECTED_LED_COUNT"] = _options.Hal.ExpectedLedCount.ToString();
        startInfo.Environment["MAXSUN_AURA_SDK_DIR"] = _options.Hal.AuraSdkDirectory;
        startInfo.Environment["MAXSUN_HAL_DIR"] = _options.Hal.MaxsunHalDirectory;
        startInfo.Environment["MAXSUN_ENE_HAL_DIR"] = _options.Hal.EneHalDirectory;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start HAL helper: {helperPath}");

        var timeout = TimeSpan.FromSeconds(_options.Hal.HelperTimeoutSeconds);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions));
            process.StandardInput.Close();

            var lineTask = process.StandardOutput.ReadLineAsync(timeoutCts.Token).AsTask();
            var exitTask = process.WaitForExitAsync(timeoutCts.Token);
            var completed = await Task.WhenAny(lineTask, exitTask);
            if (completed == exitTask && !lineTask.IsCompleted)
            {
                var stderr = await process.StandardError.ReadToEndAsync(timeoutCts.Token);
                return HalResponse.Failure($"HAL helper exited before writing a response. Exit={process.ExitCode}. {stderr}");
            }

            var line = await lineTask;
            await exitTask;
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.Warn($"HAL helper stderr: {error.Trim()}");
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
                return HalResponse.Failure(
                    $"HAL helper returned an empty response. Exit={process.ExitCode}. {stderr}".Trim());
            }

            return JsonSerializer.Deserialize<HalResponse>(line, JsonOptions)
                ?? HalResponse.Failure("HAL helper response could not be parsed.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return HalResponse.Failure($"HAL helper timed out after {timeout.TotalSeconds:0}s.");
        }
    }

    private static ProcessStartInfo CreateStartInfo(string helperPath)
    {
        var extension = Path.GetExtension(helperPath);
        var fileName = helperPath;
        var arguments = "--stdio";
        if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            fileName = GetDotnetHostPath();
            arguments = $"{QuoteArgument(helperPath)} --stdio";
        }

        return new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(helperPath) ?? AppContext.BaseDirectory
        };
    }

    private string ResolveHelperPath()
    {
        var configured = _options.Hal.HelperPath;
        if (Path.IsPathRooted(configured) && File.Exists(configured))
        {
            return configured;
        }

        var baseCandidate = Path.Combine(AppContext.BaseDirectory, configured);
        if (File.Exists(baseCandidate))
        {
            return baseCandidate;
        }

        var baseDllCandidate = Path.ChangeExtension(baseCandidate, ".dll");
        if (File.Exists(baseDllCandidate))
        {
            return baseDllCandidate;
        }

        var siblingCandidate = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "ha_maxsun.HalHelper",
            configured));
        if (File.Exists(siblingCandidate))
        {
            return siblingCandidate;
        }

        var siblingDllCandidate = Path.ChangeExtension(siblingCandidate, ".dll");
        if (File.Exists(siblingDllCandidate))
        {
            return siblingDllCandidate;
        }

        return Path.GetFullPath(configured);
    }

    private static string GetDotnetHostPath()
    {
        var dotnet = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(dotnet) &&
            Path.GetFileName(dotnet).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(dotnet))
        {
            return dotnet;
        }

        return "dotnet";
    }

    private static string QuoteArgument(string value)
        => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // The helper may already be gone.
        }
    }
}

