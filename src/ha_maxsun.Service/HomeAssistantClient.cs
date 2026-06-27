using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HaMaxsun.Core;

namespace HaMaxsun.Service;

internal interface IHomeAssistantClient : IAsyncDisposable
{
    Task Completion { get; }
    Task ConnectAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<EntityState>> GetStatesAsync(CancellationToken cancellationToken);
    Task SubscribeStateChangedAsync(Func<EntityState, Task> stateChangedHandler, CancellationToken cancellationToken);
    Task SetInputBooleanAsync(string entityId, bool on, CancellationToken cancellationToken);
}

internal sealed class HomeAssistantClient : IHomeAssistantClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly BridgeOptions _options;
    private readonly BridgeLogger _logger;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonObject>> _pending = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private ClientWebSocket? _socket;
    private int _nextId;
    private Func<EntityState, Task>? _stateChangedHandler;
    private Task? _receiveLoop;

    public HomeAssistantClient(BridgeOptions options, BridgeLogger logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task Completion => _receiveLoop ?? Task.CompletedTask;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        _socket = new ClientWebSocket();
        await _socket.ConnectAsync(new Uri(_options.HomeAssistant.WebSocketUrl), cancellationToken);

        var authRequired = await ReceiveTextAsync(cancellationToken);
        var authRequiredType = JsonNode.Parse(authRequired)?["type"]?.GetValue<string>();
        if (!string.Equals(authRequiredType, "auth_required", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Expected auth_required from Home Assistant, got: {authRequired}");
        }

        await SendJsonAsync(new JsonObject
        {
            ["type"] = "auth",
            ["access_token"] = _options.HomeAssistant.LongLivedAccessToken
        }, cancellationToken);

        var authResponse = await ReceiveTextAsync(cancellationToken);
        var authType = JsonNode.Parse(authResponse)?["type"]?.GetValue<string>();
        if (!string.Equals(authType, "auth_ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Home Assistant authentication failed: {authResponse}");
        }

        _receiveLoop = Task.Run(() => ReceiveLoopAsync(cancellationToken), CancellationToken.None);
        _logger.Info("Connected to Home Assistant WebSocket API.");
    }

    public async Task<IReadOnlyList<EntityState>> GetStatesAsync(CancellationToken cancellationToken)
    {
        var result = await SendRequestAsync(new JsonObject
        {
            ["type"] = "get_states"
        }, cancellationToken);

        var states = new List<EntityState>();
        if (result is not JsonArray array)
        {
            return states;
        }

        foreach (var item in array)
        {
            var entityId = item?["entity_id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(entityId))
            {
                continue;
            }

            states.Add(new EntityState(entityId, item?["state"]?.GetValue<string>()));
        }

        return states;
    }

    public async Task SubscribeStateChangedAsync(Func<EntityState, Task> stateChangedHandler, CancellationToken cancellationToken)
    {
        _stateChangedHandler = stateChangedHandler;
        await SendRequestAsync(new JsonObject
        {
            ["type"] = "subscribe_events",
            ["event_type"] = "state_changed"
        }, cancellationToken);
    }

    public Task SetInputBooleanAsync(string entityId, bool on, CancellationToken cancellationToken)
        => CallServiceAsync("input_boolean", on ? "turn_on" : "turn_off", entityId, null, cancellationToken);

    public Task SetInputNumberAsync(string entityId, int value, CancellationToken cancellationToken)
        => CallServiceAsync("input_number", "set_value", entityId, new JsonObject
        {
            ["value"] = value
        }, cancellationToken);

    public Task SetInputTextAsync(string entityId, string value, CancellationToken cancellationToken)
        => CallServiceAsync("input_text", "set_value", entityId, new JsonObject
        {
            ["value"] = value
        }, cancellationToken);

    private async Task CallServiceAsync(
        string domain,
        string service,
        string entityId,
        JsonObject? serviceData,
        CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["type"] = "call_service",
            ["domain"] = domain,
            ["service"] = service,
            ["target"] = new JsonObject
            {
                ["entity_id"] = entityId
            }
        };

        if (serviceData is not null)
        {
            payload["service_data"] = serviceData;
        }

        await SendRequestAsync(payload, cancellationToken);
    }

    private async Task<JsonNode?> SendRequestAsync(JsonObject payload, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        payload["id"] = id;

        var tcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        try
        {
            await SendJsonAsync(payload, cancellationToken);

            var timeout = TimeSpan.FromSeconds(_options.HomeAssistant.RequestTimeoutSeconds);
            var response = await tcs.Task.WaitAsync(timeout, cancellationToken);
            if (response["success"]?.GetValue<bool>() == false)
            {
                throw new InvalidOperationException(response["error"]?.ToJsonString(JsonOptions) ?? "Home Assistant request failed.");
            }

            return response["result"];
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task SendJsonAsync(JsonObject payload, CancellationToken cancellationToken)
    {
        if (_socket is null)
        {
            throw new InvalidOperationException("WebSocket is not connected.");
        }

        var json = payload.ToJsonString(JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await ReceiveTextAsync(cancellationToken);
                var node = JsonNode.Parse(message)?.AsObject();
                if (node is null)
                {
                    continue;
                }

                var type = node["type"]?.GetValue<string>();
                if (string.Equals(type, "event", StringComparison.OrdinalIgnoreCase))
                {
                    DispatchEvent(node);
                    continue;
                }

                if (node["id"] is JsonValue idValue &&
                    idValue.TryGetValue<int>(out var id) &&
                    _pending.TryRemove(id, out var pending))
                {
                    pending.TrySetResult(node);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Home Assistant receive loop stopped");
            foreach (var pending in _pending.Values)
            {
                pending.TrySetException(ex);
            }
        }
    }

    private void DispatchEvent(JsonObject node)
    {
        if (_stateChangedHandler is null)
        {
            return;
        }

        var data = node["event"]?["data"];
        var entityId = data?["entity_id"]?.GetValue<string>()
            ?? data?["new_state"]?["entity_id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return;
        }

        var newState = data?["new_state"]?["state"]?.GetValue<string>();
        var handler = _stateChangedHandler;
        _ = Task.Run(async () =>
        {
            try
            {
                await handler(new EntityState(entityId, newState));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Home Assistant state_changed handler failed for {entityId}");
            }
        });
    }

    private async Task<string> ReceiveTextAsync(CancellationToken cancellationToken)
    {
        if (_socket is null)
        {
            throw new InvalidOperationException("WebSocket is not connected.");
        }

        var buffer = new byte[16 * 1024];
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new WebSocketException("Home Assistant closed the WebSocket.");
            }

            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_socket is { State: WebSocketState.Open })
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bridge shutting down", CancellationToken.None);
            }
        }
        catch
        {
            _socket?.Abort();
        }
        finally
        {
            _socket?.Dispose();
            _sendLock.Dispose();
        }
    }
}

