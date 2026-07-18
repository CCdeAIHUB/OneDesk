using System.Text.Json;
using OneDesk.Desktop.Transport;
using Xunit;

namespace OneDesk.Desktop.Tests;

public sealed class MobileGatewayTransportContractTests
{
    [Fact]
    public async Task EnvelopeCodecRoundTripsPayloadLargerThanUdpDatagram()
    {
        // 场景：方案消息可能超过 UDP 单包上限，QUIC 流信封必须无损传输大消息。
        var payload = JsonSerializer.SerializeToElement(new { data = new string('x', 256 * 1024) });
        var expected = new MobileGatewayEnvelope(1, "request", "message-1", null, payload);
        await using var stream = new MemoryStream();

        await MobileGatewayEnvelopeCodec.WriteAsync(stream, expected, TestContext.Current.CancellationToken);
        stream.Position = 0;
        var actual = await MobileGatewayEnvelopeCodec.ReadAsync(stream, TestContext.Current.CancellationToken);

        Assert.NotNull(actual);
        Assert.Equal(expected.MessageId, actual.MessageId);
        Assert.Equal(expected.MessageType, actual.MessageType);
        Assert.Equal(payload.GetProperty("data").GetString(), actual.Payload.GetProperty("data").GetString());
    }

    [Fact]
    public async Task EnvelopeCodecRejectsOversizedFrameBeforeAllocation()
    {
        // 场景：恶意长度前缀不能让网关分配无上限内存。
        await using var stream = new MemoryStream();
        await stream.WriteAsync(BitConverter.GetBytes(int.MaxValue), TestContext.Current.CancellationToken);
        stream.Position = 0;

        var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await MobileGatewayEnvelopeCodec.ReadAsync(stream, TestContext.Current.CancellationToken));

        Assert.Contains("GatewayFrameTooLarge", error.Message, StringComparison.Ordinal);
    }
}
