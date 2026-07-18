using System.Net;
using System.Net.Quic;
using System.Text.Json;
using OneDesk.Desktop.Transport;
using Xunit;

namespace OneDesk.Desktop.Tests;

public sealed class MsQuicServerTransportTests
{
    [Fact]
    public async Task RequestAndServerEventUseAuthenticatedQuicConnection()
    {
        // 场景：移动端在一条 QUIC 连接上发送请求，桌面既能响应也能主动推送事件。
        Assert.True(QuicConnection.IsSupported, "当前 Windows 运行时必须提供 MsQuic");
        var cancellationToken = TestContext.Current.CancellationToken;
        var requestSeen = new TaskCompletionSource<MobileGatewaySession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var eventSeen = new TaskCompletionSource<MobileGatewayEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var identity = QuicServerIdentity.CreateEphemeral("OneDesk Transport Test");
        await using var server = new MsQuicServerTransport(identity, (session, envelope, _) =>
        {
            requestSeen.TrySetResult(session);
            return ValueTask.FromResult<MobileGatewayEnvelope?>(new MobileGatewayEnvelope(
                1,
                "response",
                $"response-{envelope.MessageId}",
                envelope.MessageId,
                JsonSerializer.SerializeToElement(new { ok = true })));
        });
        await server.StartAsync(new IPEndPoint(IPAddress.Loopback, 0), cancellationToken);

        await using var client = await MsQuicClientTransport.ConnectAsync(
            server.BoundEndPoint,
            _ => true,
            (envelope, _) =>
            {
                eventSeen.TrySetResult(envelope);
                return ValueTask.CompletedTask;
            },
            cancellationToken);
        var request = new MobileGatewayEnvelope(1, "request", "request-1", null, JsonSerializer.SerializeToElement(new { type = "heartbeat" }));
        var response = await client.RequestAsync(request, cancellationToken);
        var session = await requestSeen.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

        Assert.Equal(request.MessageId, response.CorrelationId);
        Assert.True(response.Payload.GetProperty("ok").GetBoolean());

        var pushed = new MobileGatewayEnvelope(1, "event", "event-1", null, JsonSerializer.SerializeToElement(new { eventType = "scheme.updated" }));
        await server.SendEventAsync(session.Id, pushed, cancellationToken);
        var actualEvent = await eventSeen.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        Assert.Equal("scheme.updated", actualEvent.Payload.GetProperty("eventType").GetString());
    }
}
