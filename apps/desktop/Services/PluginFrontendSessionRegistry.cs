using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace OneDesk.Desktop.Services;

/// <summary>
/// 维护前端插件沙箱与插件身份的壳子侧映射。插件脚本只会看到消息 API，不能自行声明插件 ID。
/// </summary>
public sealed class PluginFrontendSessionRegistry
{
    private readonly ConcurrentDictionary<string, string> _sessionToPlugin = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _pluginToSession = new(StringComparer.OrdinalIgnoreCase);

    public string Create(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId)) throw new ArgumentException("PluginIdentityMissing", nameof(pluginId));
        Revoke(pluginId);
        var sessionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        _sessionToPlugin[sessionId] = pluginId;
        _pluginToSession[pluginId] = sessionId;
        return sessionId;
    }

    public bool TryResolve(string sessionId, out string pluginId) =>
        _sessionToPlugin.TryGetValue(sessionId, out pluginId!);

    public void Revoke(string pluginId)
    {
        if (!_pluginToSession.TryRemove(pluginId, out var sessionId)) return;
        _sessionToPlugin.TryRemove(sessionId, out _);
    }

    public void Clear()
    {
        _pluginToSession.Clear();
        _sessionToPlugin.Clear();
    }
}
