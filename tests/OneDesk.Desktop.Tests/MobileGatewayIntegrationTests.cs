using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using OneDesk.Desktop.Domain;
using OneDesk.Desktop.Services;
using OneDesk.Desktop.Storage;
using OneDesk.Desktop.Transport;
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
        var capabilities = new CapabilityDirectoryService();
        var permissions = new PermissionService(capabilities, paths);
        using var gateway = new QuicGatewayService(devices, logs, pairing, repository, paths, permissions);
        await gateway.StartAsync(0, cancellationToken);

        try
        {
            var page = CreatePage("page-test");
            var scheme = CreateScheme("scheme-test", page.Id);
            await repository.SavePageAsync(page, cancellationToken);
            await repository.SaveSchemeAsync(scheme, cancellationToken);
            await repository.ApplySchemeAsync(scheme.Id, cancellationToken); // 桌面全局方案不得成为移动端默认方案。

            var pairCode = pairing.GenerateVerificationCode();
            var events = Channel.CreateUnbounded<MobileGatewayEnvelope>();
            await using var subscriber = await MsQuicClientTransport.ConnectAsync(
                new IPEndPoint(IPAddress.Loopback, gateway.Port),
                _ => true,
                (envelope, _) =>
                {
                    events.Writer.TryWrite(envelope);
                    return ValueTask.CompletedTask;
                },
                cancellationToken);
            var pairResponse = await SendAndReceiveAsync(subscriber, new
            {
                type = "pair",
                requestId = "pair-1",
                code = pairCode,
                deviceId = "android-local",
                stableDeviceKey = "android:stable-device-test",
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

            var subscribeResponse = await SendAndReceiveAsync(subscriber, Authorized("subscribe", mobileId, token));
            Assert.True(subscribeResponse.GetProperty("ok").GetBoolean());
            Assert.False(subscribeResponse.GetProperty("payload").GetProperty("scheme").GetProperty("hasScheme").GetBoolean());

            var registrationsBeforeHeartbeat = logs.Recent(50).Count(item => item.Message == "Registered mobile gateway peer");
            var heartbeatResponse = await SendAndReceiveAsync(subscriber, Authorized("heartbeat", mobileId, token));
            Assert.True(heartbeatResponse.GetProperty("ok").GetBoolean());
            Assert.Equal(
                registrationsBeforeHeartbeat,
                logs.Recent(50).Count(item => item.Message == "Registered mobile gateway peer"));

            var logResponse = await SendAndReceiveAsync(subscriber, Authorized("logs", mobileId, token, new
            {
                logs = new[] { new { logId = "mobile-log-1", createdAt = DateTimeOffset.UtcNow, sourceDeviceId = mobileId, level = "Info", category = "Mobile", message = "在线日志" } }
            }));
            Assert.True(logResponse.GetProperty("ok").GetBoolean());
            Assert.Contains(logs.Recent(20), item => item.SourceDeviceId == mobileId && item.Message == "在线日志");

            await repository.ApplySchemeAsync(scheme.Id, mobileId, cancellationToken);
            var firstPush = gateway.PushSchemeUpdateAsync(mobileId, cancellationToken);
            var firstEvent = await ReceiveEventAsync(events.Reader, "scheme.updated", cancellationToken);
            var eventPayload = firstEvent.GetProperty("payload");
            Assert.Equal("scheme.updated", eventPayload.GetProperty("eventType").GetString());
            var eventId = eventPayload.GetProperty("eventId").GetString()!;
            var descriptor = eventPayload.GetProperty("scheme");
            Assert.True(descriptor.GetProperty("hasScheme").GetBoolean());

            var snapshot = await DownloadSnapshotAsync(subscriber, mobileId, token, descriptor);
            Assert.Equal(scheme.Id, snapshot.GetProperty("activeSchemeId").GetString());
            Assert.Equal(page.Id, snapshot.GetProperty("pages").EnumerateArray().First().GetProperty("id").GetString());
            Assert.Equal(JsonValueKind.Array, snapshot.GetProperty("permissionGrants").ValueKind);
            await SendAsync(subscriber, Authorized("scheme-ack", mobileId, token, new { eventId }));
            Assert.True((await firstPush).Acknowledged);

            // 分块下载使用过临时端口后，第二次主动推送仍必须到达持续订阅套接字。
            await repository.ApplySchemeAsync(scheme.Id, mobileId, cancellationToken);
            var secondPush = gateway.PushSchemeUpdateAsync(mobileId, cancellationToken);
            var secondEvent = await ReceiveEventAsync(events.Reader, "scheme.updated", cancellationToken);
            var secondEventId = secondEvent.GetProperty("payload").GetProperty("eventId").GetString()!;
            await SendAsync(subscriber, Authorized("scheme-ack", mobileId, token, new { eventId = secondEventId }));
            Assert.True((await secondPush).Acknowledged);

            // 卸载重装会清除令牌和桌面分配 ID，但 Android 稳定键不变；重新配对必须复用原身份。
            var reinstallCode = pairing.GenerateVerificationCode();
            await using var reinstalledClient = await MsQuicClientTransport.ConnectAsync(
                new IPEndPoint(IPAddress.Loopback, gateway.Port),
                _ => true,
                (_, _) => ValueTask.CompletedTask,
                cancellationToken);
            var reinstallResponse = await SendAndReceiveAsync(reinstalledClient, new
            {
                type = "pair",
                requestId = "pair-after-reinstall",
                code = reinstallCode,
                deviceId = "android-new-install-local-id",
                stableDeviceKey = "android:stable-device-test",
                displayName = "Android Test",
                platform = "android",
                architecture = "arm64",
                logs = Array.Empty<object>()
            });
            Assert.True(reinstallResponse.GetProperty("ok").GetBoolean());
            Assert.Equal(mobileId, reinstallResponse.GetProperty("payload").GetProperty("assignedMobile").GetProperty("deviceId").GetString());
            Assert.Single(pairing.TrustedDevices());
        }
        finally
        {
            await gateway.StopAsync(cancellationToken);
        }
    }

    private static async Task<JsonElement> DownloadSnapshotAsync(MsQuicClientTransport client, string deviceId, string token, JsonElement descriptor)
    {
        var total = descriptor.GetProperty("totalBytes").GetInt64();
        var hash = descriptor.GetProperty("hash").GetString()!;
        using var output = new MemoryStream();
        var offset = 0L;
        while (offset < total)
        {
            var response = await SendAndReceiveAsync(client, Authorized("scheme-chunk", deviceId, token, new { hash, offset, length = 24 * 1024 }));
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

    private static async Task<JsonElement> SendAndReceiveAsync(MsQuicClientTransport client, object message)
    {
        var payload = JsonSerializer.SerializeToElement(message, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var messageId = payload.TryGetProperty("requestId", out var requestId)
            ? requestId.GetString() ?? $"request-{Guid.NewGuid():N}"
            : $"request-{Guid.NewGuid():N}";
        var envelope = new MobileGatewayEnvelope(1, "request", messageId, null, payload);
        var response = await client.RequestAsync(envelope, TestContext.Current.CancellationToken);
        Assert.Equal(messageId, response.CorrelationId);
        return response.Payload;
    }

    private static async Task SendAsync(MsQuicClientTransport client, object message)
    {
        _ = await SendAndReceiveAsync(client, message);
    }

    private static async Task<JsonElement> ReceiveEventAsync(
        ChannelReader<MobileGatewayEnvelope> events,
        string eventType,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (true)
        {
            var message = (await events.ReadAsync(timeout.Token)).Payload;
            if (message.TryGetProperty("payload", out var payload) &&
                payload.TryGetProperty("eventType", out var type) &&
                type.GetString() == eventType)
            {
                return message;
            }
        }
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
