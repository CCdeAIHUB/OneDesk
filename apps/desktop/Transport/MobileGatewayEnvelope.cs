using System.Buffers.Binary;
using System.Text.Json;

namespace OneDesk.Desktop.Transport;

public static class MobileGatewayEnvelopeCodec
{
    public const int MaximumFrameBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async ValueTask WriteAsync(
        Stream stream,
        MobileGatewayEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        if (payload.Length > MaximumFrameBytes)
        {
            throw new InvalidDataException($"GatewayFrameTooLarge: {payload.Length}");
        }

        var prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(prefix, payload.Length);
        await stream.WriteAsync(prefix, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async ValueTask<MobileGatewayEnvelope> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var prefix = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(prefix, cancellationToken);
        var length = BinaryPrimitives.ReadInt32BigEndian(prefix);
        if (length <= 0 || length > MaximumFrameBytes)
        {
            // 长度在分配缓冲区前验证，避免远端用伪造前缀造成内存耗尽。
            throw new InvalidDataException($"GatewayFrameTooLarge: {length}");
        }

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return JsonSerializer.Deserialize<MobileGatewayEnvelope>(payload, JsonOptions)
            ?? throw new InvalidDataException("GatewayEnvelopeInvalid: 消息信封为空");
    }
}
