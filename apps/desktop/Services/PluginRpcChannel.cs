using System.Collections.Concurrent;
using System.Text.Json;

namespace OneDesk.Desktop.Services;

/// <summary>
/// 与具体进程解耦的 JSON-RPC 2.0 通道。响应只按 id 关联，插件主动请求必须进入宿主处理器。
/// </summary>
public sealed class PluginRpcChannel
{
    private readonly Func<string, CancellationToken, Task> _writeLine;
    private readonly Func<string, JsonElement, CancellationToken, Task<object?>>? _requestHandler;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private long _sequence;

    public PluginRpcChannel(
        Func<string, CancellationToken, Task> writeLine,
        Func<string, JsonElement, CancellationToken, Task<object?>>? requestHandler = null)
    {
        _writeLine = writeLine;
        _requestHandler = requestHandler;
    }

    public async Task<JsonElement> InvokeAsync(
        string method,
        object? parameters,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var id = Interlocked.Increment(ref _sequence);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion)) throw new InvalidOperationException("PluginRpcIdCollision");

        try
        {
            var payload = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters }, JsonOptions);
            await _writeLine(payload, cancellationToken);
            return await completion.Task.WaitAsync(timeout, cancellationToken);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public async Task AcceptLineAsync(string line, CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (root.TryGetProperty("method", out var methodElement))
        {
            await HandleRequestAsync(root, methodElement.GetString() ?? string.Empty, cancellationToken);
            return;
        }

        if (!root.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id)) return;
        if (_pending.TryRemove(id, out var completion)) completion.TrySetResult(root.Clone());
    }

    public void FailPending(Exception error)
    {
        foreach (var pair in _pending.ToArray())
        {
            if (_pending.TryRemove(pair.Key, out var completion)) completion.TrySetException(error);
        }
    }

    private async Task HandleRequestAsync(JsonElement request, string method, CancellationToken cancellationToken)
    {
        if (!request.TryGetProperty("id", out var id)) return;
        var parameters = request.TryGetProperty("params", out var value) ? value.Clone() : EmptyObject;
        object response;
        try
        {
            response = _requestHandler is null
                ? new { jsonrpc = "2.0", id, error = new { code = -32601, message = "Host method is not registered." } }
                : new { jsonrpc = "2.0", id, result = await _requestHandler(method, parameters, cancellationToken) };
        }
        catch (Exception error)
        {
            response = new { jsonrpc = "2.0", id, error = new { code = -32000, message = error.Message } };
        }
        await _writeLine(JsonSerializer.Serialize(response, JsonOptions), cancellationToken);
    }

    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
