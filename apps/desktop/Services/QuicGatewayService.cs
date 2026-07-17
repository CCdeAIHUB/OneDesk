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
    private const int MaximumChunkBytes = 24 * 1024;
    private static readonly TimeSpan PeerTimeout = TimeSpan.FromSeconds(45);
    private readonly DeviceRegistry _devices;
    private readonly StructuredLogStore _logs;
    private readonly PairingService _pairing;
    private readonly OneDeskRepository _repository;
    private readonly ConcurrentDictionary<string, QuicPeerState> _peers = new();
    private readonly ConcurrentQueue<QueuedQuicRequest> _queuedRequests = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pendingSchemeAcks = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsApiResult>> _pendingJsApiResponses = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private CancellationTokenSource? _transportCts;
    private UdpClient? _udp;
    private Task? _receiveLoop;
    private JsApiRouter? _jsApiRouter;

    public QuicGatewayService(DeviceRegistry devices, StructuredLogStore logs, PairingService pairing, OneDeskRepository repository)
    {
        _devices = devices;
        _logs = logs;
        _pairing = pairing;
        _repository = repository;
    }

    public bool IsRunning { get; private set; }
    public int Port { get; private set; } = 48320;

    public IReadOnlyCollection<QuicPeerState> Peers => _peers.Values
        .Select(CurrentPeerState)
        .ToArray();

    public IReadOnlyCollection<QueuedQuicRequest> QueuedRequests => _queuedRequests.ToArray();

    public void AttachJsApiRouter(JsApiRouter router)
    {
        _jsApiRouter = router;
    }

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
        _logs.Append(_devices.DesktopIdentity.DeviceId, "Info", "Gateway", "Mobile gateway transport started", new Dictionary<string, object?>
        {
            ["port"] = port,
            ["transport"] = "udp-json-chunked"
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
                // 释放套接字后接收循环会自行结束，停止流程不应阻塞窗口退出。
            }
        }

        _peers.Clear();
        foreach (var pending in _pendingSchemeAcks.Values)
        {
            pending.TrySetCanceled(cancellationToken);
        }
        _pendingSchemeAcks.Clear();
        foreach (var pending in _pendingJsApiResponses.Values)
        {
            pending.TrySetCanceled(cancellationToken);
        }
        _pendingJsApiResponses.Clear();
        _logs.Append(_devices.DesktopIdentity.DeviceId, "Info", "Gateway", "Mobile gateway stopped");
    }

    public void RegisterPeer(DeviceIdentity identity, IPEndPoint endpoint, string trustCredentialHash)
    {
        _peers[identity.DeviceId] = new QuicPeerState(identity.DeviceId, endpoint.ToString(), true, DateTimeOffset.UtcNow, trustCredentialHash);
        _logs.Append(_devices.DesktopIdentity.DeviceId, "Info", "Gateway", "Registered mobile gateway peer", new Dictionary<string, object?>
        {
            ["deviceId"] = identity.DeviceId,
            ["endpoint"] = endpoint.ToString()
        });
    }

    // 方案分块和资源下载使用短生命周期 UDP 端口。此处只刷新在线状态，避免覆盖服务端主动推送所需的订阅端口。
    private void TouchPeer(DeviceIdentity identity, IPEndPoint fallbackEndpoint, string trustCredentialHash)
    {
        if (_peers.TryGetValue(identity.DeviceId, out var current))
        {
            _peers[identity.DeviceId] = current with
            {
                Online = true,
                LastSeenAt = DateTimeOffset.UtcNow,
                TrustCredentialHash = trustCredentialHash
            };
            return;
        }

        RegisterPeer(identity, fallbackEndpoint, trustCredentialHash);
    }

    public bool IsOnline(string deviceId)
    {
        return _peers.TryGetValue(deviceId, out var peer) && CurrentPeerState(peer).Online;
    }

    public async Task<SchemePushResult> PushSchemeUpdateAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (_udp is null || !IsRunning || !_peers.TryGetValue(deviceId, out var storedPeer))
        {
            return new SchemePushResult(false, false, "设备当前离线，方案已记录并将在下次连接时同步");
        }

        var peer = CurrentPeerState(storedPeer);
        if (!peer.Online || !IPEndPoint.TryParse(peer.Endpoint, out var endpoint))
        {
            return new SchemePushResult(false, false, "设备当前离线，方案已记录并将在下次连接时同步");
        }

        var snapshot = await BuildSchemeSnapshotAsync(deviceId, cancellationToken);
        var eventId = $"scheme-event-{Guid.NewGuid():N}";
        var acknowledgement = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingSchemeAcks[eventId] = acknowledgement;
        try
        {
            var message = GatewayResponse.Success(new
            {
                eventType = "scheme.updated",
                eventId,
                deviceId,
                scheme = snapshot.Descriptor
            });
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, _jsonOptions));
            await _udp.SendAsync(bytes, bytes.Length, endpoint);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // 确认代表移动端已经完整下载并原子替换缓存；视频等大资源不能沿用普通请求的短超时。
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            await acknowledgement.Task.WaitAsync(timeout.Token);
            return new SchemePushResult(true, true, "在线设备已接收并缓存新方案");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SchemePushResult(true, false, "方案已记录，但在线设备未在规定时间内确认接收");
        }
        finally
        {
            _pendingSchemeAcks.TryRemove(eventId, out _);
        }
    }

    public async Task<JsApiResult> ForwardJsApiAsync(JsApiRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_udp is null || !IsRunning)
        {
            return JsApiResult.Error("GatewayOffline", "移动网关未运行。");
        }

        if (!IsOnline(request.TargetDeviceId) || !_peers.TryGetValue(request.TargetDeviceId, out var storedPeer))
        {
            return JsApiResult.Error("TargetOffline", "目标移动设备未连接。");
        }

        var peer = CurrentPeerState(storedPeer);
        if (!peer.Online || !IPEndPoint.TryParse(peer.Endpoint, out var endpoint))
        {
            return JsApiResult.Error("TargetOffline", "目标移动设备未连接。");
        }

        var queued = new QueuedQuicRequest(request.RequestId, request.TargetDeviceId, request.Capability, DateTimeOffset.UtcNow);
        _queuedRequests.Enqueue(queued);
        while (_queuedRequests.Count > 512 && _queuedRequests.TryDequeue(out _))
        {
        }

        var responseSource = new TaskCompletionSource<JsApiResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingJsApiResponses[request.RequestId] = responseSource;
        try
        {
            var message = GatewayResponse.Success(new
            {
                eventType = "jsapi.request",
                request.RequestId,
                request.TargetDeviceId,
                request.Capability,
                request.Payload
            });
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, _jsonOptions));
            await _udp.SendAsync(bytes, bytes.Length, endpoint);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(7));
            return await responseSource.Task.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return JsApiResult.Error("TargetNoResponse", "目标移动设备未在规定时间内返回 JSAPI 结果。");
        }
        finally
        {
            _pendingJsApiResponses.TryRemove(request.RequestId, out _);
        }
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
                _logs.Append(_devices.DesktopIdentity.DeviceId, "Error", "Gateway", "Gateway receive loop failed", new Dictionary<string, object?> { ["error"] = ex.Message });
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
                "subscribe" or "heartbeat" => await HandleSubscriptionAsync(request, packet.RemoteEndPoint, cancellationToken),
                "scheme" => await HandleSchemeRequestAsync(request, packet.RemoteEndPoint, cancellationToken),
                "scheme-chunk" => await HandleSchemeChunkAsync(request, packet.RemoteEndPoint, cancellationToken),
                "scheme-ack" => HandleSchemeAcknowledgement(request, packet.RemoteEndPoint),
                "asset" => await HandleAssetRequestAsync(request, packet.RemoteEndPoint, cancellationToken),
                "logs" => HandleMobileLogs(request, packet.RemoteEndPoint),
                "jsapi" => await HandleJsApiRequestAsync(request, packet.RemoteEndPoint, cancellationToken),
                "jsapi-response" => HandleJsApiResponse(request, packet.RemoteEndPoint),
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
        RegisterPeer(identity, remote, HashToken(credential.Token));
        AppendMobileLogs(identity.DeviceId, request.Logs);
        var snapshot = await BuildSchemeSnapshotAsync(identity.DeviceId, cancellationToken);
        return GatewayResponse.Success(new
        {
            desktop = _devices.DesktopIdentity,
            assignedMobile = identity,
            trustCredential = credential.Token,
            scheme = snapshot.Descriptor
        });
    }

    private async Task<GatewayResponse> HandleTrustedConnectAsync(GatewayRequest request, IPEndPoint remote, CancellationToken cancellationToken)
    {
        if (!ValidateTrustedRequest(request))
        {
            return GatewayResponse.Fail("InvalidTrustCredential", "长期信任凭据无效");
        }

        var identity = EnsureMobileIdentity(request);
        RegisterPeer(identity, remote, HashToken(request.TrustCredential!));
        AppendMobileLogs(identity.DeviceId, request.Logs);
        var snapshot = await BuildSchemeSnapshotAsync(identity.DeviceId, cancellationToken);
        return GatewayResponse.Success(new
        {
            desktop = _devices.DesktopIdentity,
            assignedMobile = identity,
            scheme = snapshot.Descriptor
        });
    }

    private async Task<GatewayResponse> HandleSubscriptionAsync(GatewayRequest request, IPEndPoint remote, CancellationToken cancellationToken)
    {
        if (!ValidateTrustedRequest(request))
        {
            return GatewayResponse.Fail("InvalidTrustCredential", "长期信任凭据无效");
        }

        var identity = EnsureMobileIdentity(request);
        var trustCredentialHash = HashToken(request.TrustCredential!);
        // 首次订阅需要保存用于服务端主动推送的端点；心跳只刷新在线时间，避免日志刷屏。
        if (string.Equals(request.Type, "heartbeat", StringComparison.Ordinal))
        {
            TouchPeer(identity, remote, trustCredentialHash);
        }
        else
        {
            RegisterPeer(identity, remote, trustCredentialHash);
        }
        var snapshot = await BuildSchemeSnapshotAsync(identity.DeviceId, cancellationToken);
        return GatewayResponse.Success(new { subscribed = true, scheme = snapshot.Descriptor });
    }

    private async Task<GatewayResponse> HandleSchemeRequestAsync(GatewayRequest request, IPEndPoint remote, CancellationToken cancellationToken)
    {
        if (!ValidateTrustedRequest(request))
        {
            return GatewayResponse.Fail("InvalidTrustCredential", "长期信任凭据无效");
        }

        TouchPeer(EnsureMobileIdentity(request), remote, HashToken(request.TrustCredential!));
        var snapshot = await BuildSchemeSnapshotAsync(request.DeviceId!, cancellationToken);
        return GatewayResponse.Success(new { scheme = snapshot.Descriptor });
    }

    private async Task<GatewayResponse> HandleSchemeChunkAsync(GatewayRequest request, IPEndPoint remote, CancellationToken cancellationToken)
    {
        if (!ValidateTrustedRequest(request))
        {
            return GatewayResponse.Fail("InvalidTrustCredential", "长期信任凭据无效");
        }

        TouchPeer(EnsureMobileIdentity(request), remote, HashToken(request.TrustCredential!));
        var snapshot = await BuildSchemeSnapshotAsync(request.DeviceId!, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.Hash) && !string.Equals(request.Hash, snapshot.Hash, StringComparison.OrdinalIgnoreCase))
        {
            return GatewayResponse.Fail("SchemeChanged", "方案在下载过程中发生变化，请重新获取");
        }

        var offset = Math.Clamp(request.Offset ?? 0, 0, snapshot.Bytes.LongLength);
        var length = Math.Clamp(request.Length ?? MaximumChunkBytes, 1, MaximumChunkBytes);
        var count = Math.Min(length, snapshot.Bytes.Length - (int)offset);
        var bytes = snapshot.Bytes.AsSpan((int)offset, count).ToArray();
        return GatewayResponse.Success(new
        {
            snapshot.Version,
            snapshot.Hash,
            offset,
            totalBytes = snapshot.Bytes.LongLength,
            complete = offset + count >= snapshot.Bytes.LongLength,
            data = Convert.ToBase64String(bytes)
        });
    }

    private GatewayResponse HandleSchemeAcknowledgement(GatewayRequest request, IPEndPoint remote)
    {
        if (!ValidateTrustedRequest(request) || string.IsNullOrWhiteSpace(request.EventId))
        {
            return GatewayResponse.Fail("InvalidAcknowledgement", "方案确认信息无效");
        }

        TouchPeer(EnsureMobileIdentity(request), remote, HashToken(request.TrustCredential!));
        if (_pendingSchemeAcks.TryRemove(request.EventId, out var pending))
        {
            pending.TrySetResult(true);
        }
        return GatewayResponse.Success(new { acknowledged = true, request.EventId });
    }

    private async Task<GatewayResponse> HandleAssetRequestAsync(GatewayRequest request, IPEndPoint remote, CancellationToken cancellationToken)
    {
        if (!ValidateTrustedRequest(request))
        {
            return GatewayResponse.Fail("InvalidTrustCredential", "长期信任凭据无效");
        }
        if (string.IsNullOrWhiteSpace(request.OwnerKind) || string.IsNullOrWhiteSpace(request.OwnerId) || string.IsNullOrWhiteSpace(request.FileName))
        {
            return GatewayResponse.Fail("InvalidAssetRequest", "资源请求缺少所有者或文件名");
        }
        if (!await IsAssetInAssignedSchemeAsync(request.DeviceId!, request.OwnerKind, request.OwnerId, cancellationToken))
        {
            return GatewayResponse.Fail("AssetNotAuthorized", "资源不属于当前设备已分配方案");
        }

        TouchPeer(EnsureMobileIdentity(request), remote, HashToken(request.TrustCredential!));
        var chunk = await _repository.ReadSchemeAssetChunkAsync(
            request.OwnerKind,
            request.OwnerId,
            request.FileName,
            request.Offset ?? 0,
            request.Length ?? MaximumChunkBytes,
            cancellationToken);
        return chunk is null
            ? GatewayResponse.Fail("AssetNotFound", "方案资源不存在")
            : GatewayResponse.Success(new
            {
                chunk.FileName,
                chunk.ContentType,
                chunk.Offset,
                chunk.TotalBytes,
                chunk.Complete,
                data = Convert.ToBase64String(chunk.Bytes)
            });
    }

    private GatewayResponse HandleMobileLogs(GatewayRequest request, IPEndPoint remote)
    {
        if (!ValidateTrustedRequest(request))
        {
            return GatewayResponse.Fail("InvalidTrustCredential", "长期信任凭据无效");
        }

        var identity = EnsureMobileIdentity(request);
        TouchPeer(identity, remote, HashToken(request.TrustCredential!));
        AppendMobileLogs(identity.DeviceId, request.Logs);
        return GatewayResponse.Success(new { accepted = request.Logs?.Count ?? 0 });
    }

    private async Task<GatewayResponse> HandleJsApiRequestAsync(GatewayRequest request, IPEndPoint remote, CancellationToken cancellationToken)
    {
        if (!ValidateTrustedRequest(request))
        {
            return GatewayResponse.Fail("InvalidTrustCredential", "长期信任凭据无效");
        }
        if (_jsApiRouter is null)
        {
            return GatewayResponse.Fail("RouterUnavailable", "JSAPI 路由尚未就绪");
        }
        if (string.IsNullOrWhiteSpace(request.ComponentId) || string.IsNullOrWhiteSpace(request.PageId) || string.IsNullOrWhiteSpace(request.SchemeId) || string.IsNullOrWhiteSpace(request.Capability))
        {
            return GatewayResponse.Fail("InvalidJsApiRequest", "JSAPI 请求缺少可信来源信息");
        }
        if (!await IsTrustedComponentSourceAsync(request, cancellationToken))
        {
            return GatewayResponse.Fail("InvalidComponentSource", "组件不属于当前设备已分配方案");
        }

        TouchPeer(EnsureMobileIdentity(request), remote, HashToken(request.TrustCredential!));
        var targetDeviceId = string.Equals(request.TargetDeviceId, "desktop", StringComparison.OrdinalIgnoreCase)
            ? _devices.DesktopIdentity.DeviceId
            : request.TargetDeviceId!;
        var jsApiRequest = new JsApiRequest(
            request.RequestId ?? $"req-{Guid.NewGuid():N}",
            targetDeviceId,
            new TrustedSource(request.SchemeId, request.PageId, request.ComponentId, null, "component"),
            request.Capability,
            request.Payload?.Clone());
        var result = await _jsApiRouter.RouteAsync(jsApiRequest, cancellationToken);
        return result.Ok
            ? GatewayResponse.Success(result.Payload ?? new { })
            : GatewayResponse.Fail(result.ErrorCode ?? "JsApiFailed", result.Message ?? "JSAPI 调用失败");
    }

    private GatewayResponse HandleJsApiResponse(GatewayRequest request, IPEndPoint remote)
    {
        if (!ValidateTrustedRequest(request) || string.IsNullOrWhiteSpace(request.RequestId))
        {
            return GatewayResponse.Fail("InvalidJsApiResponse", "移动端 JSAPI 响应身份无效");
        }

        TouchPeer(EnsureMobileIdentity(request), remote, HashToken(request.TrustCredential!));
        if (_pendingJsApiResponses.TryRemove(request.RequestId, out var pending))
        {
            var result = request.ResponseOk == true
                ? JsApiResult.Success(request.Payload?.Clone())
                : JsApiResult.Error(request.ErrorCode ?? "MobileExecutionFailed", request.Message ?? "移动端能力执行失败");
            pending.TrySetResult(result);
        }

        return GatewayResponse.Success(new { acknowledged = true, request.RequestId });
    }

    private async Task<SchemeTransportSnapshot> BuildSchemeSnapshotAsync(string deviceId, CancellationToken cancellationToken)
    {
        var active = await _repository.GetAssignedSchemeAsync(deviceId, cancellationToken);
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

        var componentBundles = new List<object>();
        foreach (var component in components.DistinctBy(item => item.Id))
        {
            var files = await _repository.ReadComponentFilesAsync(component.Id, cancellationToken);
            JsonElement? visualConfig = null;
            if (files.TryGetValue("onedesk.visual.json", out var visualJson) && !string.IsNullOrWhiteSpace(visualJson))
            {
                try
                {
                    using var document = JsonDocument.Parse(visualJson);
                    visualConfig = document.RootElement.Clone();
                }
                catch (JsonException ex)
                {
                    _logs.Append(_devices.DesktopIdentity.DeviceId, "Warning", "Scheme", "Component visual configuration is invalid", new Dictionary<string, object?>
                    {
                        ["componentId"] = component.Id,
                        ["error"] = ex.Message
                    });
                }
            }
            componentBundles.Add(new { definition = component, visualConfig });
        }

        var actionIds = components.SelectMany(component => component.ActionIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actions = (await _repository.ListActionsAsync(cancellationToken)).Where(action => actionIds.Contains(action.Id)).ToArray();
        var payload = new
        {
            activeSchemeId = active?.SchemeId,
            appliedAt = active?.AppliedAt,
            scheme,
            pages,
            components = componentBundles,
            actions
        };
        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var version = active?.AppliedAt.ToUnixTimeMilliseconds().ToString() ?? "0";
        return new SchemeTransportSnapshot(version, hash, bytes, new SchemeDescriptor(version, hash, bytes.LongLength, active is not null));
    }

    private async Task<bool> IsAssetInAssignedSchemeAsync(string deviceId, string ownerKind, string ownerId, CancellationToken cancellationToken)
    {
        var active = await _repository.GetAssignedSchemeAsync(deviceId, cancellationToken);
        var scheme = active is null ? null : await _repository.GetSchemeAsync(active.SchemeId, cancellationToken);
        if (scheme is null)
        {
            return false;
        }
        if (ownerKind.Equals("page", StringComparison.OrdinalIgnoreCase))
        {
            return scheme.PageIds.Contains(ownerId, StringComparer.OrdinalIgnoreCase);
        }
        if (!ownerKind.Equals("component", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        foreach (var pageId in scheme.PageIds)
        {
            var page = await _repository.GetPageAsync(pageId, cancellationToken);
            if (page?.Cells.Any(cell => string.Equals(cell.ComponentId, ownerId, StringComparison.OrdinalIgnoreCase)) == true)
            {
                return true;
            }
        }
        return false;
    }

    private async Task<bool> IsTrustedComponentSourceAsync(GatewayRequest request, CancellationToken cancellationToken)
    {
        var active = await _repository.GetAssignedSchemeAsync(request.DeviceId!, cancellationToken);
        if (active is null || !string.Equals(active.SchemeId, request.SchemeId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var scheme = await _repository.GetSchemeAsync(active.SchemeId, cancellationToken);
        if (scheme is null || !scheme.PageIds.Contains(request.PageId!, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }
        var page = await _repository.GetPageAsync(request.PageId!, cancellationToken);
        return page?.Cells.Any(cell => string.Equals(cell.ComponentId, request.ComponentId, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private void AppendMobileLogs(string deviceId, IReadOnlyList<JsonElement>? logs)
    {
        foreach (var log in logs ?? [])
        {
            if (log.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            _logs.Append(deviceId, ReadLogString(log, "level", "Info"), ReadLogString(log, "category", "Mobile"), ReadLogString(log, "message", "移动端断联日志"), new Dictionary<string, object?>
            {
                ["mobileLogId"] = ReadLogString(log, "logId", ""),
                ["mobileCreatedAt"] = ReadLogString(log, "createdAt", ""),
                ["originalSourceDeviceId"] = ReadLogString(log, "sourceDeviceId", deviceId)
            });
        }
    }

    private bool ValidateTrustedRequest(GatewayRequest request)
    {
        return !string.IsNullOrWhiteSpace(request.DeviceId)
            && !string.IsNullOrWhiteSpace(request.TrustCredential)
            && _pairing.ValidateTrustCredential(request.DeviceId, request.TrustCredential);
    }

    private DeviceIdentity EnsureMobileIdentity(GatewayRequest request)
    {
        return _devices.Find(request.DeviceId!) ?? _devices.RegisterMobile(
            string.IsNullOrWhiteSpace(request.DisplayName) ? "OneDesk Mobile" : request.DisplayName,
            string.IsNullOrWhiteSpace(request.Platform) ? "android" : request.Platform,
            string.IsNullOrWhiteSpace(request.Architecture) ? "unknown" : request.Architecture,
            request.DeviceId);
    }

    private QuicPeerState CurrentPeerState(QuicPeerState peer)
    {
        var online = peer.Online && DateTimeOffset.UtcNow - peer.LastSeenAt <= PeerTimeout;
        return online == peer.Online ? peer : peer with { Online = online };
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
    string? RequestId,
    string? Code,
    string? DeviceId,
    string? DisplayName,
    string? Platform,
    string? Architecture,
    string? TrustCredential,
    IReadOnlyList<JsonElement>? Logs,
    string? EventId,
    string? Hash,
    long? Offset,
    int? Length,
    string? OwnerKind,
    string? OwnerId,
    string? FileName,
    string? SchemeId,
    string? PageId,
    string? ComponentId,
    string? TargetDeviceId,
    string? Capability,
    JsonElement? Payload,
    bool? ResponseOk,
    string? ErrorCode,
    string? Message);

public sealed record GatewayResponse(bool Ok, object? Payload, string? ErrorCode, string? Message)
{
    public static GatewayResponse Success(object payload) => new(true, payload, null, null);
    public static GatewayResponse Fail(string code, string message) => new(false, null, code, message);
}

public sealed record QuicPeerState(string DeviceId, string Endpoint, bool Online, DateTimeOffset LastSeenAt, string TrustCredentialHash);
public sealed record QueuedQuicRequest(string RequestId, string TargetDeviceId, string Capability, DateTimeOffset CreatedAt);
public sealed record SchemeDescriptor(string Version, string Hash, long TotalBytes, bool HasScheme);
public sealed record SchemeTransportSnapshot(string Version, string Hash, byte[] Bytes, SchemeDescriptor Descriptor);
public sealed record SchemePushResult(bool Online, bool Acknowledged, string Message);
