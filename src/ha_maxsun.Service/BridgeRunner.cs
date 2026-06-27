using HaMaxsun.Core;

namespace HaMaxsun.Service;

internal sealed class BridgeRunner
{
    private readonly BridgeOptions _options;
    private readonly BridgeLogger _logger;
    private readonly Func<IHomeAssistantClient> _homeAssistantFactory;
    private readonly IHalClient _helper;
    private readonly Func<IReadOnlyList<string>> _getConflicts;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly LightStateAccumulator _state;
    private readonly SemaphoreSlim _applyLock = new(1, 1);

    public BridgeRunner(BridgeOptions options, BridgeLogger logger)
        : this(
            options,
            logger,
            () => new HomeAssistantClient(options, logger),
            new HalHelperClient(options, logger),
            ProcessConflictDetector.GetConflicts,
            Task.Delay)
    {
    }

    internal BridgeRunner(
        BridgeOptions options,
        BridgeLogger logger,
        Func<IHomeAssistantClient> homeAssistantFactory,
        IHalClient helper,
        Func<IReadOnlyList<string>> getConflicts,
        Func<TimeSpan, CancellationToken, Task> delayAsync)
    {
        _options = options;
        _logger = logger;
        _homeAssistantFactory = homeAssistantFactory;
        _helper = helper;
        _getConflicts = getConflicts;
        _delayAsync = delayAsync;
        _state = new LightStateAccumulator(options.Entities);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_options.Bridge.ResumeDelayMilliseconds > 0)
        {
            _logger.Info($"Waiting {_options.Bridge.ResumeDelayMilliseconds}ms before first hardware probe.");
            await _delayAsync(TimeSpan.FromMilliseconds(_options.Bridge.ResumeDelayMilliseconds), cancellationToken);
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var ha = _homeAssistantFactory();
                await ha.ConnectAsync(cancellationToken);
                await InitializeAsync(ha, cancellationToken);
                await ha.SubscribeStateChangedAsync(
                    state => OnStateChangedAsync(ha, state, cancellationToken),
                    cancellationToken);
                _logger.Info("Subscribed to Home Assistant state_changed events.");
                await ha.Completion.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Bridge loop failed");
                await DelayBeforeReconnect(cancellationToken);
            }
        }
    }

    private async Task InitializeAsync(IHomeAssistantClient ha, CancellationToken cancellationToken)
    {
        var states = await ha.GetStatesAsync(cancellationToken);
        foreach (var state in states)
        {
            _state.Update(state);
        }

        var conflicts = _getConflicts();
        if (conflicts.Count > 0)
        {
            _logger.Warn($"MaxsunSync conflict detected: {string.Join(", ", conflicts)}");
            await ha.SetInputBooleanAsync(_options.Entities.Available, false, cancellationToken);
            return;
        }

        var probe = await _helper.ProbeAsync(cancellationToken);
        if (!probe.Ok)
        {
            _logger.Warn($"HAL probe failed: {probe.Error}");
            await ha.SetInputBooleanAsync(_options.Entities.Available, false, cancellationToken);
            return;
        }

        _logger.Info($"HAL probe OK: {probe.DeviceName}, GUID={probe.Guid}, LEDs={probe.LedCount}");
        await ha.SetInputBooleanAsync(_options.Entities.Available, true, cancellationToken);

        if (_options.Bridge.ApplyOnStartup)
        {
            await ApplyCurrentStateAsync(ha, cancellationToken);
        }
    }

    private async Task OnStateChangedAsync(IHomeAssistantClient ha, EntityState entityState, CancellationToken cancellationToken)
    {
        if (!_options.Entities.IsControlEntity(entityState.EntityId))
        {
            return;
        }

        _logger.Info($"Home Assistant state changed: {entityState.EntityId}={entityState.State}");
        if (!_state.Update(entityState))
        {
            _logger.Warn($"Ignored invalid Home Assistant state: {entityState.EntityId}={entityState.State}");
            return;
        }

        await ApplyCurrentStateAsync(ha, cancellationToken);
    }

    private async Task ApplyCurrentStateAsync(IHomeAssistantClient ha, CancellationToken cancellationToken)
    {
        await _applyLock.WaitAsync(cancellationToken);
        try
        {
            var conflicts = _getConflicts();
            if (conflicts.Count > 0)
            {
                _logger.Warn($"Skipping apply while MaxsunSync is running: {string.Join(", ", conflicts)}");
                await ha.SetInputBooleanAsync(_options.Entities.Available, false, cancellationToken);
                return;
            }

            var current = _state.Current;
            var response = await _helper.ApplyAsync(current, cancellationToken);
            if (!response.Ok)
            {
                _logger.Warn($"HAL apply failed: {response.Error}");
                await ha.SetInputBooleanAsync(_options.Entities.Available, false, cancellationToken);
                return;
            }

            await ha.SetInputBooleanAsync(_options.Entities.Available, true, cancellationToken);
            _logger.Info($"Applied light state: on={current.On}, rgb={current.Color}, brightness={current.Brightness}, effective={string.Join(',', response.AppliedRgb ?? [])}");
        }
        finally
        {
            _applyLock.Release();
        }
    }

    private async Task DelayBeforeReconnect(CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(Math.Max(1, _options.Bridge.ReconnectDelaySeconds));
        _logger.Info($"Reconnecting in {delay.TotalSeconds:0}s.");
        await _delayAsync(delay, cancellationToken);
    }
}

