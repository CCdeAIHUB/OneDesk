using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using OneDesk.Desktop.Domain;
using OneDesk.Desktop.Services;
using OneDesk.Desktop.Storage;
using Xunit;

namespace OneDesk.Desktop.Tests;

public sealed class MobileGatewayIntegrationTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"onedesk-tests-{Guid.NewGuid():N}");

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task AssignedSchemeUsesChunkedPushAndGlobalSchemeNeverLeaksToNewMobile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var paths = new OneDeskDataPaths(_root);
        var repository = new OneDeskRepository(paths, new JsonFileStore());
        var logs = new StructuredLogStore(paths);
        var devices = new DeviceRegistry(paths);
        var pairing = new PairingService(paths);
        using var gateway = new QuicGatewayService(devices, logs, pairing, repository);
        var port = FreeUdpPort();
        await gateway.StartAsync(port, cancellationToken);

        try
        {
            var page = CreatePage("page-test");
            var scheme = CreateScheme("scheme-test", page.Id);
            await repository.SavePageAsync(page, cancellationToken);
            await repository.SaveSchemeAsync(scheme, cancellationToken);
            await repository.ApplySchemeAsync(scheme.Id, cancellationToken); // 桌面全局方案不得成为移动端默认方案。

            var pairCode = pairing.GenerateVerificationCode();
            using var subscriber = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var pairResponse = await SendAndReceiveAsync(subscriber, port, new
            {
                type = "pair",
                requestId = "pair-1",
                code = pairCode,
                deviceId = "android-local",
                displayName = "Android Test",
                platform = "android",
                architecture = "arm64",
                logs = Array.Empty<object>()
            });
            Assert.True(pairResponse.GetProperty("ok").GetBoolean());
            var pairPayload = pairResponse.GetProperty("payload");
            var mobileId = pairPayload.GetProperty("assignedMobile").GetProperty("deviceId").GetString()!;
            var token = pairPayload.GetProperty("trustCredential").GetString()!;
            Assert.False(pairPayload.GetProperty("scheme").GetProperty("hasScheme").GetBoolean());

            var subscribeResponse = await SendAndReceiveAsync(subscriber, port, Authorized("subscribe", mobileId, token));
            Assert.True(subscribeResponse.GetProperty("ok").GetBoolean());
            Assert.False(subscribeResponse.GetProperty("payload").GetProperty("scheme").GetProperty("hasScheme").GetBoolean());

            var registrationsBeforeHeartbeat = logs.Recent(50).Count(item => item.Message == "Registered mobile gateway peer");
            var heartbeatResponse = await SendAndReceiveAsync(subscriber, port, Authorized("heartbeat", mobileId, token));
            Assert.True(heartbeatResponse.GetProperty("ok").GetBoolean());
            Assert.Equal(
                registrationsBeforeHeartbeat,
                logs.Recent(50).Count(item => item.Message == "Registered mobile gateway peer"));

            using (var logClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            {
                var logResponse = await SendAndReceiveAsync(logClient, port, Authorized("logs", mobileId, token, new
                {
                    logs = new[] { new { logId = "mobile-log-1", createdAt = DateTimeOffset.UtcNow, sourceDeviceId = mobileId, level = "Info", category = "Mobile", message = "在线日志" } }
                }));
                Assert.True(logResponse.GetProperty("ok").GetBoolean());
                Assert.Contains(logs.Recent(20), item => item.SourceDeviceId == mobileId && item.Message == "在线日志");
            }

            await repository.ApplySchemeAsync(scheme.Id, mobileId, cancellationToken);
            var firstPush = gateway.PushSchemeUpdateAsync(mobileId, cancellationToken);
            var firstEvent = await ReceiveEventAsync(subscriber, "scheme.updated");
            var eventPayload = firstEvent.GetProperty("payload");
            Assert.Equal("scheme.updated", eventPayload.GetProperty("eventType").GetString());
            var eventId = eventPayload.GetProperty("eventId").GetString()!;
            var descriptor = eventPayload.GetProperty("scheme");
            Assert.True(descriptor.GetProperty("hasScheme").GetBoolean());

            var snapshot = await DownloadSnapshotAsync(port, mobileId, token, descriptor);
            Assert.Equal(scheme.Id, snapshot.GetProperty("activeSchemeId").GetString());
            Assert.Equal(page.Id, snapshot.GetProperty("pages").EnumerateArray().First().GetProperty("id").GetString());
            await SendAsync(subscriber, port, Authorized("scheme-ack", mobileId, token, new { eventId }));
            Assert.True((await firstPush).Acknowledged);

            // 分块下载使用过临时端口后，第二次主动推送仍必须到达持续订阅套接字。
            await repository.ApplySchemeAsync(scheme.Id, mobileId, cancellationToken);
            var secondPush = gateway.PushSchemeUpdateAsync(mobileId, cancellationToken);
            var secondEvent = await ReceiveEventAsync(subscriber, "scheme.updated");
            var secondEventId = secondEvent.GetProperty("payload").GetProperty("eventId").GetString()!;
            await SendAsync(subscriber, port, Authorized("scheme-ack", mobileId, token, new { eventId = secondEventId }));
            Assert.True((await secondPush).Acknowledged);
        }
        finally
        {
            await gateway.StopAsync(cancellationToken);
        }
    }

    private static async Task<JsonElement> DownloadSnapshotAsync(int port, string deviceId, string token, JsonElement descriptor)
    {
        var total = descriptor.GetProperty("totalBytes").GetInt64();
        var hash = descriptor.GetProperty("hash").GetString()!;
        using var output = new MemoryStream();
        var offset = 0L;
        while (offset < total)
        {
            using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var response = await SendAndReceiveAsync(client, port, Authorized("scheme-chunk", deviceId, token, new { hash, offset, length = 24 * 1024 }));
            Assert.True(response.GetProperty("ok").GetBoolean());
            var chunk = response.GetProperty("payload");
            var bytes = Convert.FromBase64String(chunk.GetProperty("data").GetString()!);
            await output.WriteAsync(bytes);
            offset += bytes.Length;
        }
        return JsonDocument.Parse(output.ToArray()).RootElement.Clone();
    }

    private static object Authorized(string type, string deviceId, string token, object? extra = null)
    {
        var values = new Dictionary<string, object?>
        {
            ["type"] = type,
            ["requestId"] = $"request-{Guid.NewGuid():N}",
            ["deviceId"] = deviceId,
            ["displayName"] = "Android Test",
            ["platform"] = "android",
            ["architecture"] = "arm64",
            ["trustCredential"] = token,
        };
        if (extra is not null)
        {
            foreach (var property in extra.GetType().GetProperties()) values[property.Name] = property.GetValue(extra);
        }
        return values;
    }

    private static async Task<JsonElement> SendAndReceiveAsync(UdpClient client, int port, object message)
    {
        await SendAsync(client, port, message);
        return await ReceiveAsync(client);
    }

    private static async Task SendAsync(UdpClient client, int port, object message)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await client.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, port));
    }

    private static async Task<JsonElement> ReceiveAsync(UdpClient client)
    {
        var packet = await client.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        return JsonDocument.Parse(packet.Buffer).RootElement.Clone();
    }

    private static async Task<JsonElement> ReceiveEventAsync(UdpClient client, string eventType)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var packet = await client.ReceiveAsync(timeout.Token);
            var message = JsonDocument.Parse(packet.Buffer).RootElement.Clone();
            if (message.TryGetProperty("payload", out var payload) &&
                payload.TryGetProperty("eventType", out var type) &&
                type.GetString() == eventType)
            {
                return message;
            }
        }
    }

    private static int FreeUdpPort()
    {
        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)client.Client.LocalEndPoint!).Port;
    }

    private static PageDefinition CreatePage(string id) => new()
    {
        Id = id,
        Name = "测试页面",
        Rows = 1,
        Columns = 1,
        GridHorizontalAlign = "center",
        GridVerticalAlign = "center",
        Spacing = new GridSpacing { Padding = 0, RowGap = 0, ColumnGap = 0 },
        BackgroundKind = "solid",
        BackgroundValue = "#0ea5e9",
        Cells = []
    };

    private static SchemeDefinition CreateScheme(string id, string pageId)
    {
        var previous = new TriggerDefinition { Id = "swipe-right", Category = "touch.standard", DisplayName = "右滑", FingerCount = 1 };
        var next = new TriggerDefinition { Id = "swipe-left", Category = "touch.standard", DisplayName = "左滑", FingerCount = 1 };
        return new SchemeDefinition
        {
            Id = id,
            Name = "测试方案",
            Version = "1.0.0",
            PageIds = [pageId],
            GlobalPrevious = new PageSwitchDefinition { Trigger = previous, Animation = "fade" },
            GlobalNext = new PageSwitchDefinition { Trigger = next, Animation = "fade" },
            Edges = [],
            PluginDependencies = []
        };
    }
}
