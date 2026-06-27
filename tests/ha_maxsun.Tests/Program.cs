using System.Text.Json;
using HaMaxsun.Core;
using HaMaxsun.Service;

namespace HaMaxsun.Tests;

internal static class Program
{
    private static int _failures;

    private static async Task<int> Main()
    {
        Test("brightness scales RGB channels", () =>
        {
            var result = ColorMath.ApplyBrightness(new RgbColor(100, 200, 255), 128);
            AssertEqual(new RgbColor(50, 100, 128), result);
        });

        Test("off state maps to black", () =>
        {
            var state = new LightState(false, new RgbColor(255, 12, 99), 255);
            AssertEqual(RgbColor.Black, state.EffectiveColor);
        });

        Test("RGB helper state parses", () =>
        {
            AssertTrue(ColorParser.TryParseRgb("88, 52, 46", out var color));
            AssertEqual(new RgbColor(88, 52, 46), color);
        });

        Test("invalid RGB helper state is rejected", () =>
        {
            AssertFalse(ColorParser.TryParseRgb("300,0,0", out _));
            AssertFalse(ColorParser.TryParseRgb("not-a-color", out _));
        });

        Test("HA helper state reducer updates light state", () =>
        {
            var ids = new EntityIds();
            var reducer = new LightStateAccumulator(ids);
            AssertTrue(reducer.Update(new EntityState(ids.Power, "on")));
            AssertTrue(reducer.Update(new EntityState(ids.Brightness, "64")));
            AssertTrue(reducer.Update(new EntityState(ids.Color, "10,20,30")));
            AssertEqual(new LightState(true, new RgbColor(10, 20, 30), 64), reducer.Current);
        });

        Test("HAL protocol serializes apply request", () =>
        {
            var request = HalRequest.Apply(new LightState(true, new RgbColor(1, 2, 3), 42));
            var json = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            AssertTrue(json.Contains("\"command\":\"apply\"", StringComparison.Ordinal));
            AssertTrue(json.Contains("\"brightness\":42", StringComparison.Ordinal));
        });

        await TestAsync("fake HAL helper receives state-derived apply command", async () =>
        {
            var temp = Path.Combine(Path.GetTempPath(), "ha_maxsun.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);

            var capturePath = Path.Combine(temp, "captured.json");
            var fakeScript = Path.Combine(temp, "fake-hal-helper.ps1");
            var fakeCmd = Path.Combine(temp, "fake-hal-helper.cmd");
            File.WriteAllText(fakeScript, """
$line = [Console]::In.ReadLine()
if ($env:MAXSUN_FAKE_HAL_CAPTURE) {
    Set-Content -LiteralPath $env:MAXSUN_FAKE_HAL_CAPTURE -Value $line -Encoding UTF8
}
$request = $line | ConvertFrom-Json
if ($request.command -eq 'probe') {
    '{"ok":true,"deviceName":"MAXSUN MOTHERBOARD LED ENE","guid":"9d590787-6015-445d-9076-30b360cdf24b","ledCount":264}'
    exit 0
}
if ($request.command -eq 'apply') {
    $r = [int][math]::Floor(($request.rgb[0] * $request.brightness) / 255)
    $g = [int][math]::Floor(($request.rgb[1] * $request.brightness) / 255)
    $b = [int][math]::Floor(($request.rgb[2] * $request.brightness) / 255)
    "{`"ok`":true,`"appliedRgb`":[$r,$g,$b]}"
    exit 0
}
'{"ok":false,"error":"unexpected command"}'
exit 2
""");
            File.WriteAllText(fakeCmd, """
@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0fake-hal-helper.ps1" %*
""");

            var previousCapture = Environment.GetEnvironmentVariable("MAXSUN_FAKE_HAL_CAPTURE");
            Environment.SetEnvironmentVariable("MAXSUN_FAKE_HAL_CAPTURE", capturePath);
            try
            {
                var options = new BridgeOptions
                {
                    Hal =
                    {
                        HelperPath = fakeCmd,
                        HelperTimeoutSeconds = 10
                    },
                    Bridge =
                    {
                        LogDirectory = Path.Combine(temp, "logs")
                    }
                };

                var ids = new EntityIds();
                var reducer = new LightStateAccumulator(ids);
                AssertTrue(reducer.Update(new EntityState(ids.Power, "on")));
                AssertTrue(reducer.Update(new EntityState(ids.Color, "10,20,30")));
                AssertTrue(reducer.Update(new EntityState(ids.Brightness, "64")));

                using var logger = new BridgeLogger(options.Bridge.GetLogDirectory());
                var helper = new HalHelperClient(options, logger);
                var response = await helper.ApplyAsync(reducer.Current, CancellationToken.None);

                AssertTrue(response.Ok);
                AssertEqual(3, response.AppliedRgb?.Length ?? 0);
                AssertEqual(2, response.AppliedRgb![0]);
                AssertEqual(5, response.AppliedRgb[1]);
                AssertEqual(7, response.AppliedRgb[2]);

                using var captured = JsonDocument.Parse(File.ReadAllText(capturePath).TrimStart('\uFEFF'));
                var root = captured.RootElement;
                AssertEqual("apply", root.GetProperty("command").GetString());
                AssertTrue(root.GetProperty("on").GetBoolean());
                AssertEqual(10, root.GetProperty("rgb")[0].GetInt32());
                AssertEqual(20, root.GetProperty("rgb")[1].GetInt32());
                AssertEqual(30, root.GetProperty("rgb")[2].GetInt32());
                AssertEqual(64, root.GetProperty("brightness").GetInt32());
            }
            finally
            {
                Environment.SetEnvironmentVariable("MAXSUN_FAKE_HAL_CAPTURE", previousCapture);
                TryDeleteDirectory(temp);
            }
        });

        await TestAsync("bridge reconnects after Home Assistant connection failure", async () =>
        {
            var temp = Path.Combine(Path.GetTempPath(), "ha_maxsun.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);

            try
            {
                var options = new BridgeOptions
                {
                    Bridge =
                    {
                        ResumeDelayMilliseconds = 0,
                        ReconnectDelaySeconds = 1,
                        ApplyOnStartup = false,
                        LogDirectory = Path.Combine(temp, "logs")
                    }
                };

                using var logger = new BridgeLogger(options.Bridge.GetLogDirectory());
                var attempts = 0;
                var secondConnect = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                IHomeAssistantClient CreateClient()
                {
                    attempts++;
                    return attempts == 1
                        ? new FailingHomeAssistantClient()
                        : new HoldingHomeAssistantClient(secondConnect);
                }

                var runner = new BridgeRunner(
                    options,
                    logger,
                    CreateClient,
                    new FakeHalClient(),
                    () => Array.Empty<string>(),
                    (_, _) => Task.CompletedTask);

                using var cts = new CancellationTokenSource();
                var runTask = runner.RunAsync(cts.Token);
                await secondConnect.Task.WaitAsync(TimeSpan.FromSeconds(5));
                cts.Cancel();
                await runTask.WaitAsync(TimeSpan.FromSeconds(5));

                AssertEqual(2, attempts);
            }
            finally
            {
                TryDeleteDirectory(temp);
            }
        });

        Console.WriteLine(_failures == 0 ? "All tests passed." : $"{_failures} tests failed.");
        return _failures == 0 ? 0 : 1;
    }

    private static void Test(string name, Action action)
    {
        try
        {
            action();
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            _failures++;
            Console.WriteLine($"FAIL {name}: {ex.Message}");
        }
    }

    private static async Task TestAsync(string name, Func<Task> action)
    {
        try
        {
            await action();
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            _failures++;
            Console.WriteLine($"FAIL {name}: {ex.Message}");
        }
    }

    private static void AssertEqual<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"expected {expected}, got {actual}");
        }
    }

    private static void AssertTrue(bool value)
    {
        if (!value)
        {
            throw new InvalidOperationException("expected true");
        }
    }

    private static void AssertFalse(bool value)
    {
        if (value)
        {
            throw new InvalidOperationException("expected false");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for temporary fake helper files.
        }
    }

    private sealed class FailingHomeAssistantClient : IHomeAssistantClient
    {
        public Task Completion => Task.CompletedTask;

        public Task ConnectAsync(CancellationToken cancellationToken)
            => throw new InvalidOperationException("synthetic connection failure");

        public Task<IReadOnlyList<EntityState>> GetStatesAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task SubscribeStateChangedAsync(Func<EntityState, Task> stateChangedHandler, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task SetInputBooleanAsync(string entityId, bool on, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class HoldingHomeAssistantClient : IHomeAssistantClient
    {
        private readonly TaskCompletionSource<bool> _connected;
        private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public HoldingHomeAssistantClient(TaskCompletionSource<bool> connected)
        {
            _connected = connected;
        }

        public Task Completion => _completion.Task;

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            _connected.TrySetResult(true);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<EntityState>> GetStatesAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<EntityState>>(Array.Empty<EntityState>());

        public Task SubscribeStateChangedAsync(Func<EntityState, Task> stateChangedHandler, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task SetInputBooleanAsync(string entityId, bool on, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeHalClient : IHalClient
    {
        public Task<HalResponse> ProbeAsync(CancellationToken cancellationToken)
            => Task.FromResult(HalResponse.SuccessProbe(
                "MAXSUN MOTHERBOARD LED ENE",
                "9d590787-6015-445d-9076-30b360cdf24b",
                264));

        public Task<HalResponse> ApplyAsync(LightState state, CancellationToken cancellationToken)
            => Task.FromResult(HalResponse.SuccessApply(state.EffectiveColor));
    }
}

