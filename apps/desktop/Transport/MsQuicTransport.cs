using System.Collections.Concurrent;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

#pragma warning disable CA1416 // OneDesk 桌面产物只面向 System.Net.Quic 明确支持的 Windows、macOS 与 Linux。

namespace OneDesk.Desktop.Transport;

public sealed record MobileGatewaySession(string Id, EndPoint RemoteEndPoint);

public delegate ValueTask<MobileGatewayEnvelope?> MobileGatewayRequestHandler(
    MobileGatewaySession session,
    MobileGatewayEnvelope envelope,
    CancellationToken cancellationToken);

public sealed class QuicServerIdentity : IDisposable
{
    private QuicServerIdentity(X509Certificate2 certificate)
    {
        Certificate = certificate;
        Fingerprint = Convert.ToHexString(SHA256.HashData(certificate.RawData)).ToLowerInvariant();
    }

    public X509Certificate2 Certificate { get; }
    public string Fingerprint { get; }

    public static QuicServerIdentity CreateEphemeral(string commonName)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={commonName}",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(5));
        var certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
        return new QuicServerIdentity(certificate);
    }

    public static QuicServerIdentity LoadOrCreate(string directory, string commonName)
    {
        Directory.CreateDirectory(directory);
        var certificatePath = Path.Combine(directory, "gateway-identity.pfx");
        if (File.Exists(certificatePath))
        {
            return new QuicServerIdentity(X509CertificateLoader.LoadPkcs12FromFile(
                certificatePath,
                password: null,
                X509KeyStorageFlags.Exportable));
        }

        using var generated = CreateEphemeral(commonName);
        var temporaryPath = $"{certificatePath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes(temporaryPath, generated.Certificate.Export(X509ContentType.Pfx));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        File.Move(temporaryPath, certificatePath, overwrite: false);
        return new QuicServerIdentity(X509CertificateLoader.LoadPkcs12FromFile(
            certificatePath,
            password: null,
            X509KeyStorageFlags.Exportable));
    }

    public void Dispose() => Certificate.Dispose();
}

public sealed class MsQuicServerTransport : IAsyncDisposable
{
    public static readonly SslApplicationProtocol ApplicationProtocol = new("onedesk/1");
    private readonly QuicServerIdentity _identity;
    private readonly MobileGatewayRequestHandler _handler;
    private readonly ConcurrentDictionary<string, QuicConnection> _sessions = new();
    private readonly ConcurrentDictionary<string, Task> _connectionTasks = new();
    private readonly CancellationTokenSource _lifetime = new();
    private QuicListener? _listener;
    private Task? _acceptLoop;

    public MsQuicServerTransport(QuicServerIdentity identity, MobileGatewayRequestHandler handler)
    {
        _identity = identity;
        _handler = handler;
    }

    public IPEndPoint BoundEndPoint { get; private set; } = new(IPAddress.None, 0);
    public event Action<Exception>? TransportFaulted;
    public event Action<string>? SessionClosed;

    public async Task StartAsync(IPEndPoint endPoint, CancellationToken cancellationToken = default)
    {
        if (_listener is not null)
        {
            throw new InvalidOperationException("MsQuicTransportAlreadyStarted");
        }

        var options = new QuicListenerOptions
        {
            ListenEndPoint = endPoint,
            ApplicationProtocols = [ApplicationProtocol],
            ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(new QuicServerConnectionOptions
            {
                DefaultCloseErrorCode = 0x100,
                DefaultStreamErrorCode = 0x101,
                IdleTimeout = TimeSpan.FromMinutes(2),
                MaxInboundBidirectionalStreams = 128,
                MaxInboundUnidirectionalStreams = 16,
                ServerAuthenticationOptions = new SslServerAuthenticationOptions
                {
                    ApplicationProtocols = [ApplicationProtocol],
                    EnabledSslProtocols = SslProtocols.Tls13,
                    ServerCertificate = _identity.Certificate,
                },
            }),
        };
        _listener = await QuicListener.ListenAsync(options, cancellationToken);
        BoundEndPoint = (IPEndPoint)_listener.LocalEndPoint;
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_lifetime.Token), CancellationToken.None);
    }

    public async ValueTask SendEventAsync(
        string sessionId,
        MobileGatewayEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var connection))
        {
            throw new InvalidOperationException("GatewaySessionOffline");
        }

        await using var stream = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, cancellationToken);
        await MobileGatewayEnvelopeCodec.WriteAsync(stream, envelope, cancellationToken);
        stream.CompleteWrites();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is not null)
        {
            try
            {
                var connection = await _listener.AcceptConnectionAsync(cancellationToken);
                var sessionId = $"quic-{Guid.NewGuid():N}";
                _sessions[sessionId] = connection;
                var task = HandleConnectionAsync(sessionId, connection, cancellationToken);
                _connectionTasks[sessionId] = task;
                _ = task.ContinueWith(
                    completed =>
                    {
                        _connectionTasks.TryRemove(sessionId, out _);
                        if (completed.Exception is not null)
                        {
                            TransportFaulted?.Invoke(completed.Exception.GetBaseException());
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                TransportFaulted?.Invoke(error);
            }
        }
    }

    private async Task HandleConnectionAsync(string sessionId, QuicConnection connection, CancellationToken cancellationToken)
    {
        var session = new MobileGatewaySession(sessionId, connection.RemoteEndPoint);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var stream = await connection.AcceptInboundStreamAsync(cancellationToken);
                _ = HandleRequestStreamAsync(session, stream, cancellationToken).ContinueWith(
                    completed =>
                    {
                        if (completed.Exception is not null)
                        {
                            TransportFaulted?.Invoke(completed.Exception.GetBaseException());
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (QuicException error) when (error.QuicError is QuicError.ConnectionAborted or QuicError.ConnectionIdle)
        {
            // 对端正常退出或空闲超时属于连接生命周期结束，不应污染错误日志。
        }
        finally
        {
            _sessions.TryRemove(sessionId, out _);
            SessionClosed?.Invoke(sessionId);
            await connection.DisposeAsync();
        }
    }

    private async Task HandleRequestStreamAsync(
        MobileGatewaySession session,
        QuicStream stream,
        CancellationToken cancellationToken)
    {
        await using (stream)
        {
            var request = await MobileGatewayEnvelopeCodec.ReadAsync(stream, cancellationToken);
            var response = await _handler(session, request, cancellationToken);
            if (response is not null && stream.CanWrite)
            {
                await MobileGatewayEnvelopeCodec.WriteAsync(stream, response, cancellationToken);
                stream.CompleteWrites();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        if (_listener is not null)
        {
            await _listener.DisposeAsync();
        }
        foreach (var connection in _sessions.Values)
        {
            await connection.DisposeAsync();
        }
        if (_acceptLoop is not null)
        {
            await Task.WhenAny(_acceptLoop, Task.Delay(TimeSpan.FromSeconds(2)));
        }
        if (_connectionTasks.Count > 0)
        {
            await Task.WhenAny(Task.WhenAll(_connectionTasks.Values), Task.Delay(TimeSpan.FromSeconds(2)));
        }
        _lifetime.Dispose();
    }
}

public sealed class MsQuicClientTransport : IAsyncDisposable
{
    private readonly QuicConnection _connection;
    private readonly Func<MobileGatewayEnvelope, CancellationToken, ValueTask> _eventHandler;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _eventLoop;

    private MsQuicClientTransport(
        QuicConnection connection,
        Func<MobileGatewayEnvelope, CancellationToken, ValueTask> eventHandler)
    {
        _connection = connection;
        _eventHandler = eventHandler;
        _eventLoop = Task.Run(() => EventLoopAsync(_lifetime.Token), CancellationToken.None);
    }

    public static async Task<MsQuicClientTransport> ConnectAsync(
        IPEndPoint endPoint,
        Func<X509Certificate2, bool> certificateValidator,
        Func<MobileGatewayEnvelope, CancellationToken, ValueTask> eventHandler,
        CancellationToken cancellationToken = default)
    {
        var options = new QuicClientConnectionOptions
        {
            RemoteEndPoint = endPoint,
            DefaultCloseErrorCode = 0x100,
            DefaultStreamErrorCode = 0x101,
            IdleTimeout = TimeSpan.FromMinutes(2),
            MaxInboundBidirectionalStreams = 16,
            MaxInboundUnidirectionalStreams = 128,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                ApplicationProtocols = [MsQuicServerTransport.ApplicationProtocol],
                EnabledSslProtocols = SslProtocols.Tls13,
                TargetHost = endPoint.Address.ToString(),
                RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                    certificate is not null && certificateValidator(new X509Certificate2(certificate)),
            },
        };
        var connection = await QuicConnection.ConnectAsync(options, cancellationToken);
        return new MsQuicClientTransport(connection, eventHandler);
    }

    public async Task<MobileGatewayEnvelope> RequestAsync(
        MobileGatewayEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        await using var stream = await _connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cancellationToken);
        await MobileGatewayEnvelopeCodec.WriteAsync(stream, envelope, cancellationToken);
        stream.CompleteWrites();
        return await MobileGatewayEnvelopeCodec.ReadAsync(stream, cancellationToken);
    }

    private async Task EventLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await using var stream = await _connection.AcceptInboundStreamAsync(cancellationToken);
                var envelope = await MobileGatewayEnvelopeCodec.ReadAsync(stream, cancellationToken);
                await _eventHandler(envelope, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (QuicException error) when (error.QuicError is QuicError.ConnectionAborted or QuicError.ConnectionIdle)
        {
            // 客户端关闭连接后，服务器事件读取循环自然结束。
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        await _connection.DisposeAsync();
        await Task.WhenAny(_eventLoop, Task.Delay(TimeSpan.FromSeconds(2)));
        _lifetime.Dispose();
    }
}

#pragma warning restore CA1416
