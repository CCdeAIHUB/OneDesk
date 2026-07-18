using System.Collections.Concurrent;
using System.Text.Json;
using OneDesk.Desktop.Services;
using Xunit;

namespace OneDesk.Desktop.Tests;

public sealed class PluginRpcChannelTests
{
    [Fact]
    public async Task ConcurrentResponsesAreMatchedByCorrelationId()
    {
        // 场景：插件并发调用乱序返回时，每个调用必须收到自己的响应，不能串线。
        var writes = new ConcurrentQueue<string>();
        var channel = new PluginRpcChannel((line, _) =>
        {
            writes.Enqueue(line);
            return Task.CompletedTask;
        });

        var cancellationToken = TestContext.Current.CancellationToken;
        var first = channel.InvokeAsync("first", new { value = 1 }, TimeSpan.FromSeconds(2), cancellationToken);
        var second = channel.InvokeAsync("second", new { value = 2 }, TimeSpan.FromSeconds(2), cancellationToken);
        await WaitForWritesAsync(writes, 2, cancellationToken);
        var requests = writes.Select(line => JsonDocument.Parse(line).RootElement.Clone()).ToArray();
        var firstId = requests.Single(request => request.GetProperty("method").GetString() == "first").GetProperty("id").GetInt64();
        var secondId = requests.Single(request => request.GetProperty("method").GetString() == "second").GetProperty("id").GetInt64();

        await channel.AcceptLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = secondId, result = new { name = "second" } }), cancellationToken);
        await channel.AcceptLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = firstId, result = new { name = "first" } }), cancellationToken);

        Assert.Equal("first", (await first).GetProperty("result").GetProperty("name").GetString());
        Assert.Equal("second", (await second).GetProperty("result").GetProperty("name").GetString());
    }

    [Fact]
    public async Task PluginOriginatedRequestUsesHostHandlerAndReturnsResponse()
    {
        // 场景：插件主动请求 JSAPI 时必须经过宿主处理，并用同一 JSON-RPC id 返回结果。
        var writes = new ConcurrentQueue<string>();
        var channel = new PluginRpcChannel(
            (line, _) =>
            {
                writes.Enqueue(line);
                return Task.CompletedTask;
            },
            (method, parameters, _) => Task.FromResult<object?>(new
            {
                ok = method == "onedesk.jsapi",
                capability = parameters.GetProperty("capability").GetString(),
            }));

        await channel.AcceptLineAsync("{\"jsonrpc\":\"2.0\",\"id\":42,\"method\":\"onedesk.jsapi\",\"params\":{\"capability\":\"device.identity\"}}", TestContext.Current.CancellationToken);

        Assert.True(writes.TryDequeue(out var responseLine));
        using var response = JsonDocument.Parse(responseLine!);
        Assert.Equal(42, response.RootElement.GetProperty("id").GetInt64());
        Assert.True(response.RootElement.GetProperty("result").GetProperty("ok").GetBoolean());
        Assert.Equal("device.identity", response.RootElement.GetProperty("result").GetProperty("capability").GetString());
    }

    [Fact]
    public async Task ChannelFailureCompletesPendingCallsWithError()
    {
        // 场景：插件进程崩溃时，所有等待调用必须立即失败，不能永久挂起。
        var channel = new PluginRpcChannel((_, _) => Task.CompletedTask);
        var pending = channel.InvokeAsync("pending", null, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        channel.FailPending(new InvalidOperationException("plugin exited"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        Assert.Contains("plugin exited", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WaitForWritesAsync(ConcurrentQueue<string> writes, int count, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (writes.Count < count && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(10, cancellationToken);
        }
        Assert.Equal(count, writes.Count);
    }
}
