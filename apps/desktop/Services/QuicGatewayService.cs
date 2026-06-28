using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OneDesk.Desktop.Domain;
using OneDesk.Desktop.Storage;

namespace OneDesk.Desktop.Services;

public sealed class QuicGatewayService : IDisposable
{
    private readonly DeviceRegistry _devices;
    private readonly StructuredLogStore _logs;
    private readonly PairingService _pairing;
    private readonly OneDeskRepository _repository;
    private readonly ConcurrentDictionary<string, QuicPeerState> _peers = new();
    private readonly ConcurrentQueue<QueuedQuicRequest> _queuedRequests = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private CancellationTokenSource? _transportCts;
    private UdpClient? _udp;
    private Task? _receiveLoop;

    public QuicGatewayService(DeviceRegistry devices, StructuredLogStore logs, PairingService pairing, OneDeskRepository repository)
    {
        _devices = devices;
        _logs = logs;
        _pairing = pairing;
        _repository = repository;
    }

    public bool IsRunning { get; private set; }
    public int Port { get; private set; } = 48320;

    public IReadOnlyCollection<QuicPeerState> Peers => _peers.Values.ToArray();
    public IReadOnlyCollection<QueuedQuicRequest> QueuedRequests => _queuedRequests.ToArray();

    public Task StartAsync(int port = 48320, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        Port = port;
        _transportCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
        IsRunning = true;
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_transportCts.Token), CancellationToken.None);
        _logs.Append(_devices.DesktopIdentity.DeviceId, "Info", "Quic", "QUIC gateway transport started", new Dictionary<string, object?>
        {
            ["port"] = port,
            ["implementation"] = "UDP JSON transport active; MsQuic transport is still required for final QUIC compliance"
        });
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsRunning = false;
        _transportCts?.Cancel();
        _udp?.Dispose();
        if (_receiveLoop is not null)
        {
            try
            {
                await _receiveLoop.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch
            {
                // Stop should be best-effort; receive loop exits when UDP socket is disposed.
            }
        }

        _peers.Clear();
        _logs.Append(_devices.DesktopIdentity.DeviceId, "Info", "Quic", "QUIC gateway stopped");
    }

    public void RegisterPeer(DeviceIdentity identity, string endpoint, string trustCredentialHash)
    {
        _peers[identity.DeviceId] = new QuicPeerState(identity.DeviceId, endpoint, true, DateTimeOffset.UtcNow, trustCredentialHash);
        _logs.Append(_devices.DesktopIdentity.DeviceId, "Info", "Quic", "Registered gateway peer", new Dictionary<string, object?>
        {
            ["deviceId"] = identity.DeviceId,
            ["endpoint"] = endpoint
        });
    }

    public bool IsOnline(string deviceId)
    {
        return _peers.TryGetValue(deviceId, out var peer) && peer.Online;
    }

    public Task<JsApiResult> ForwardJsApiAsync(JsApiRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsRunning)
        {
            return Task.FromResult(JsApiResult.Error("GatewayOffline", "QUIC gateway is not running."));
        }

        if (!IsOnline(request.TargetDeviceId))
        {
            return Task.FromResult(JsApiResult.Error("TargetOffline", "Target device is not connected to the desktop gateway."));
        }

        _logs.Append(_devices.DesktopIdentity.DeviceId, "Info", "Quic", "Queued JSAPI request for transport forwarding", new Dictionary<string, object?>
        {
            ["requestId"] = request.RequestId,
            ["targetDeviceId"] = request.TargetDeviceId,
            ["capability"] = request.Capability
        });
        var queued = new QueuedQuicRequest(request.RequestId, request.TargetDeviceId, request.Capability, DateTimeOffset.UtcNow);
        _queuedRequests.Enqueue(queued);
        while (_queuedRequests.Count > 512 && _queuedRequests.TryDequeue(out _))
        {
        }

        return Task.FromResult(JsApiResult.Success(new
        {
            forwarded = true,
            queued.RequestId,
            queued.TargetDeviceId,
            queued.Capability,
            queued.CreatedAt
        }));
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _udp is not null)
        {
            try
            {
                var packet = await _udp.ReceiveAsync(cancellationToken);
                _ = Task.Run(() => HandlePacketAsync(packet, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logs.Append(_devices.DesktopIdentity.DeviceId, "Error", "Quic", "Gateway receive loop failed", new Dictionary<string, object?>
                {
                    ["error"] = ex.Message
                });
            }
        }
    }

    private async Task HandlePacketAsync(UdpReceiveResult packet, CancellationToken cancellationToken)
    {
        GatewayResponse response;
        try
        {
            var text = Encoding.UTF8.GetString(packet.Buffer);
            var request = JsonSerializer.Deserialize<GatewayRequest>(text, _jsonOptions) ?? throw new InvalidDataException("Invalid gateway request.");
            response = request.Type switch
            {
                "pair" => await HandlePairAsync(request, packet.RemoteEndPoint, cancellationToken),
                "connect" => await HandleTrustedConnectAsync(request, packet.RemoteEndPoint, cancellationToken),
                "scheme" => await HandleSchemeRequestAsync(request, packet.RemoteEndPoint, cancellationToken),
                _ => GatewayResponse.Fail("UnsupportedRequest", "不支持的网关请求")
            };
        }
        catch (Exception ex)
        {
            response = GatewayResponse.Fail("InvalidRequest", ex.Message);
        }

        if (_udp is null)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response, _jsonOptions));
        await _udp.SendAsync(bytes, bytes.Length, packet.RemoteEndPoint);
    }

    private async Task<GatewayResponse> HandlePairAsync(GatewayRequest request, IPEndPoint remote, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || !_pairing.ValidateCode(request.Code))
        {
            return GatewayResponse.Fail("InvalidVerificationCode", "验证码无效、过期或已被使用");
        }

        var identity = _devices.RegisterMobile(
            string.IsNullOrWhiteSpace(request.DisplayName) ? "OneDesk Mobile" : request.DisplayName,
            string.IsNullOrWhiteSpace(request.Platform) ? "android" : request.Platform,
            string.IsNullOrWhiteSpace(request.Architecture) ? "unknown" : request.Architecture);
        var credential = _pairing.CreateTrustCredential(identity.DeviceId, identity.DisplayName);
        RegisterPeer(identity, remote.ToString(), HashToken(credential.Token));
        AppendMobileLogs(identity.DeviceId, request.Logs);
        var scheme = await BuildSchemePayloadAsync(identity.DeviceId, cancellationToken);
        return GatewayResponse.Success(new
        {
            desktop = _devices.DesktopIdentity,
            assignedMobile = identity,
            trustCredential = credential.Token,
            scheme,
            cacheUpdated = true
        });
    }

    private async Task<GatewayResponse> HandleTrustedConnectAsync(GatewayRequest request, IPEndPoint remote, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceId) || string.IsNullOrWhiteSpace(request.TrustCredential) || !_pairing.ValidateTrustCredential(request.DeviceId, request.TrustCredential))
        {
            return GatewayResponse.Fail("InvalidTrustCredential", "长期信任凭据无效");
        }

        var identity = _devices.Find(request.DeviceId) ?? _devices.RegisterMobile(
            string.IsNullOrWhiteSpace(request.DisplayName) ? "OneDesk Mobile" : request.DisplayName,
            string.IsNullOrWhiteSpace(request.Platform) ? "android" : request.Platform,
            string.IsNullOrWhiteSpace(request.Architecture) ? "unknown" : request.Architecture,
            request.DeviceId);
        RegisterPeer(identity, remote.ToString(), HashToken(request.TrustCredential));
        AppendMobileLogs(identity.DeviceId, request.Logs);
        var scheme = await BuildSchemePayloadAsync(identity.DeviceId, cancellationToken);
        return GatewayResponse.Success(new
        {
            desktop = _devices.DesktopIdentity,
            assignedMobile = identity,
            scheme,
            cacheUpdated = true
        });
    }

    private async Task<GatewayResponse> HandleSchemeRequestAsync(GatewayRequest request, IPEndPoint remote, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.DeviceId) && !string.IsNullOrWhiteSpace(request.TrustCredential) && _pairing.ValidateTrustCredential(request.DeviceId, request.TrustCredential))
        {
            var identity = _devices.Find(request.DeviceId);
            if (identity is not null)
            {
                RegisterPeer(identity, remote.ToString(), HashToken(request.TrustCredential));
            }
        }

        return GatewayResponse.Success(new { scheme = await BuildSchemePayloadAsync(request.DeviceId, cancellationToken) });
    }

    private async Task<object> BuildSchemePayloadAsync(string? deviceId, CancellationToken cancellationToken)
    {
        var active = await _repository.GetActiveSchemeAsync(deviceId, cancellationToken);
        var scheme = active is null ? null : await _repository.GetSchemeAsync(active.SchemeId, cancellationToken);
        var pages = new List<PageDefinition>();
        var components = new List<ComponentDefinition>();
        if (scheme is not null)
        {
            foreach (var pageId in scheme.PageIds)
            {
                var page = await _repository.GetPageAsync(pageId, cancellationToken);
                if (page is null)
                {
                    continue;
                }

                pages.Add(page);
                foreach (var componentId in page.Cells.Select(cell => cell.ComponentId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var component = await _repository.GetComponentAsync(componentId!, cancellationToken);
                    if (component is not null)
                    {
                        components.Add(component);
                    }
                }
            }
        }

        var payload = new
        {
            activeSchemeId = active?.SchemeId,
            appliedAt = active?.AppliedAt,
            scheme,
            pages,
            components = components.DistinctBy(component => component.Id).ToArray()
        };
        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        return new
        {
            version = active?.AppliedAt.ToUnixTimeMilliseconds().ToString() ?? "0",
            hash,
            payload
        };
    }

    private void AppendMobileLogs(string deviceId, IReadOnlyList<JsonElement>? logs)
    {
        foreach (var log in logs ?? [])
        {
            if (log.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var level = ReadLogString(log, "level", "Info");
            var category = ReadLogString(log, "category", "Mobile");
            var message = ReadLogString(log, "message", "移动端断联日志");
            var context = new Dictionary<string, object?>
            {
                ["mobileLogId"] = ReadLogString(log, "logId", ""),
                ["mobileCreatedAt"] = ReadLogString(log, "createdAt", ""),
                ["originalSourceDeviceId"] = ReadLogString(log, "sourceDeviceId", deviceId)
            };
            _logs.Append(deviceId, level, category, message, context);
        }
    }

    private static string ReadLogString(JsonElement log, string key, string fallback)
    {
        return log.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    private static string HashToken(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }

    public void Dispose()
    {
        _transportCts?.Cancel();
        _udp?.Dispose();
        _transportCts?.Dispose();
    }
}

public sealed record GatewayRequest(
    string Type,
    string? Code,
    string? DeviceId,
    string? DisplayName,
    string? Platform,
    string? Architecture,
    string? TrustCredential,
    IReadOnlyList<JsonElement>? Logs);

public sealed record GatewayResponse(bool Ok, object? Payload, string? ErrorCode, string? Message)
{
    public static GatewayResponse Success(object payload) => new(true, payload, null, null);
    public static GatewayResponse Fail(string code, string message) => new(false, null, code, message);
}

public sealed record QuicPeerState(
    string DeviceId,
    string Endpoint,
    bool Online,
    DateTimeOffset LastSeenAt,
    string TrustCredentialHash);

public sealed record QueuedQuicRequest(
    string RequestId,
    string TargetDeviceId,
    string Capability,
    DateTimeOffset CreatedAt);
