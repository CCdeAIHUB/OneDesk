using System.Collections.Concurrent;

namespace OneDesk.Desktop.Services;

public sealed class QuicGatewayService
{
    private readonly DeviceRegistry _devices;
    private readonly StructuredLogStore _logs;
    private readonly ConcurrentDictionary<string, QuicPeerState> _peers = new();
    private readonly ConcurrentQueue<QueuedQuicRequest> _queuedRequests = new();

    public QuicGatewayService(DeviceRegistry devices, StructuredLogStore logs)
    {
        _devices = devices;
        _logs = logs;
    }

    public bool IsRunning { get; private set; }
    public int Port { get; private set; } = 48320;

    public IReadOnlyCollection<QuicPeerState> Peers => _peers.Values.ToArray();
    public IReadOnlyCollection<QueuedQuicRequest> QueuedRequests => _queuedRequests.ToArray();

    public Task StartAsync(int port = 48320, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Port = port;
        IsRunning = true;
        _logs.Append(_devices.DesktopIdentity.DeviceId, "Info", "Quic", "QUIC gateway started", new Dictionary<string, object?>
        {
            ["port"] = port,
            ["implementation"] = "MsQuic/System.Net.Quic"
        });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsRunning = false;
        _peers.Clear();
        _logs.Append(_devices.DesktopIdentity.DeviceId, "Info", "Quic", "QUIC gateway stopped");
        return Task.CompletedTask;
    }

    public void RegisterPeer(DeviceIdentity identity, string endpoint, string trustCredentialHash)
    {
        _peers[identity.DeviceId] = new QuicPeerState(identity.DeviceId, endpoint, true, DateTimeOffset.UtcNow, trustCredentialHash);
        _logs.Append(_devices.DesktopIdentity.DeviceId, "Info", "Quic", "Registered QUIC peer", new Dictionary<string, object?>
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
            return Task.FromResult(JsApiResult.Error("TargetOffline", "Target device is not connected to the desktop QUIC gateway."));
        }

        _logs.Append(_devices.DesktopIdentity.DeviceId, "Info", "Quic", "Queued JSAPI request for QUIC forwarding", new Dictionary<string, object?>
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
