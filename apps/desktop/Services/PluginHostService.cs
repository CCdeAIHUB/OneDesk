using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace OneDesk.Desktop.Services;

public sealed class PluginHostService : IDisposable
{
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan InvocationTimeout = TimeSpan.FromSeconds(30);
    private readonly StructuredLogStore _logs;
    private readonly ConcurrentDictionary<string, PluginRegistration> _plugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _registrationGate = new(1, 1);
    private Func<string, string, JsonElement, CancellationToken, Task<object?>>? _originatedRequestHandler;
    private int _disposed;

    public PluginHostService(StructuredLogStore logs)
    {
        _logs = logs;
    }

    public IReadOnlyList<Process> Processes => _plugins.Values
        .Select(registration => registration.Session?.Process)
        .Where(process => process is not null)
        .Cast<Process>()
        .ToArray();

    public IReadOnlyList<PluginManifest> InstalledPlugins => _plugins.Values
        .Select(registration => registration.Manifest)
        .OrderBy(manifest => manifest.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>
    /// 插件主动调用系统能力时，插件身份由宿主注册记录注入，插件 JSON 不能自行声明来源身份。
    /// </summary>
    public void ConfigureOriginatedRequestHandler(
        Func<string, string, JsonElement, CancellationToken, Task<object?>> handler) =>
        _originatedRequestHandler = handler;

    public async Task<bool> RemoveAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PluginRegistration? registration;
        await _registrationGate.WaitAsync(cancellationToken);
        try
        {
            if (!_plugins.TryRemove(pluginId, out registration)) return false;
            registration.Removed = true;
        }
        finally
        {
            _registrationGate.Release();
        }

        await StopSessionAsync(registration);

        if (!string.IsNullOrWhiteSpace(registration.PackageDirectory) && Directory.Exists(registration.PackageDirectory))
        {
            Directory.Delete(registration.PackageDirectory, recursive: true);
        }

        _logs.Append("desktop", "Info", "Plugin", "已移除插件", new Dictionary<string, object?> { ["pluginId"] = pluginId });
        return true;
    }

    public async Task RegisterManifestAsync(PluginManifest manifest, string packageDirectory = "", CancellationToken cancellationToken = default)
    {
        await using var prepared = await PrepareManifestAsync(manifest, packageDirectory, cancellationToken);
        await prepared.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// 先验证清单并启动常驻后端，但不替换当前注册；包安装可在所有插件均准备成功后统一提交。
    /// </summary>
    public async Task<PreparedPluginRegistration> PrepareManifestAsync(
        PluginManifest manifest,
        string packageDirectory = "",
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ValidateManifest(manifest, packageDirectory);
        var candidate = new PluginRegistration(manifest, packageDirectory);
        try
        {
            // 常驻插件先完成启动和握手，不能为了尝试新版本而提前停止仍可用的旧版本。
            if (manifest.Backend?.Persistent == true)
            {
                await EnsureSessionAsync(candidate, cancellationToken);
            }
            return new PreparedPluginRegistration(this, candidate);
        }
        catch
        {
            candidate.Removed = true;
            await StopSessionAsync(candidate);
            throw;
        }
    }

    private async Task CommitPreparedAsync(PluginRegistration candidate, CancellationToken cancellationToken)
    {
        PluginRegistration? previous;
        await _registrationGate.WaitAsync(cancellationToken);
        try
        {
            _plugins.TryGetValue(candidate.Manifest.Id, out previous);
            _plugins[candidate.Manifest.Id] = candidate;
            candidate.Committed = true;
            if (previous is not null) previous.Removed = true;
        }
        finally
        {
            _registrationGate.Release();
        }

        if (candidate.Session is { } session && candidate.Manifest.Backend?.Persistent == true)
        {
            _ = MonitorPersistentSessionAsync(candidate, session);
        }

        if (previous is not null)
        {
            try
            {
                await StopSessionAsync(previous);
            }
            catch (Exception error)
            {
                // 新注册已原子切换，旧进程清理失败只能记录，不能反向破坏已通过握手的新版本。
                _logs.Append("desktop", "Error", "Plugin", "旧插件进程清理失败", new Dictionary<string, object?>
                {
                    ["pluginId"] = candidate.Manifest.Id,
                    ["error"] = error.Message,
                });
            }
        }

        _logs.Append("desktop", "Info", "Plugin", "已注册插件清单", new Dictionary<string, object?>
        {
            ["pluginId"] = candidate.Manifest.Id,
            ["persistent"] = candidate.Manifest.Backend?.Persistent ?? candidate.Manifest.Persistent,
            ["hasFrontend"] = candidate.Manifest.Frontend is not null,
            ["hasBackend"] = candidate.Manifest.Backend is not null,
        });
    }

    private static async ValueTask DiscardPreparedAsync(PluginRegistration candidate)
    {
        candidate.Removed = true;
        await StopSessionAsync(candidate);
    }

    public async Task<object?> InvokeAsync(string pluginId, string method, object? parameters, CancellationToken cancellationToken = default)
    {
        if (!_plugins.TryGetValue(pluginId, out var registration)) return Error("PluginNotInstalled", "插件未安装");
        if (registration.Manifest.Backend is null) return Error("PluginBackendMissing", "插件没有后端能力");

        try
        {
            var session = await EnsureSessionAsync(registration, cancellationToken);
            var response = await session.InvokeAsync(
                "onedesk.invoke",
                new { method, @params = parameters, source = new { kind = "system" } },
                InvocationTimeout,
                cancellationToken);
            _logs.Append("desktop", "Info", "Plugin", "已调用插件方法", new Dictionary<string, object?>
            {
                ["pluginId"] = pluginId,
                ["method"] = method,
            });
            return response;
        }
        catch (TimeoutException error)
        {
            return Error("PluginTimeout", error.Message);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _logs.Append("desktop", "Error", "Plugin", "插件调用失败", new Dictionary<string, object?>
            {
                ["pluginId"] = pluginId,
                ["method"] = method,
                ["error"] = error.Message,
            });
            return Error("PluginExecutionFailed", error.Message);
        }
        finally
        {
            if (registration.Manifest.Backend?.Persistent != true) await StopSessionAsync(registration);
        }
    }

    public async Task<object?> SubmitSettingsAsync(string pluginId, object? settings, CancellationToken cancellationToken = default)
    {
        if (!_plugins.TryGetValue(pluginId, out var registration)) return Error("PluginNotInstalled", "插件未安装");
        if (!string.IsNullOrWhiteSpace(registration.PackageDirectory))
        {
            Directory.CreateDirectory(registration.PackageDirectory);
            var path = Path.Combine(registration.PackageDirectory, "onedesk.settings.json");
            var temporary = $"{path}.tmp-{Guid.NewGuid():N}";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(settings, JsonOptions), cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        if (registration.Manifest.Backend is null) return new { ok = true, persisted = true, delivered = false };

        try
        {
            var session = await EnsureSessionAsync(registration, cancellationToken);
            return await session.InvokeAsync(
                "onedesk.configure",
                new { settings, source = new { kind = "system" } },
                InvocationTimeout,
                cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return Error("PluginConfigureFailed", error.Message);
        }
        finally
        {
            if (registration.Manifest.Backend?.Persistent != true) await StopSessionAsync(registration);
        }
    }

    private async Task<PluginProcessSession> EnsureSessionAsync(PluginRegistration registration, CancellationToken cancellationToken)
    {
        await registration.Lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (registration.Session is { IsRunning: true } active) return active;
            if (registration.Session is { } stopped)
            {
                registration.Session = null;
                await stopped.DisposeAsync();
            }
            if (registration.Removed) throw new InvalidOperationException("PluginRegistrationRemoved");
            var artifact = SelectArtifact(registration.Manifest.Backend?.Artifacts ?? [])
                ?? throw new InvalidOperationException($"PluginArtifactMissing:{registration.Manifest.Id}");
            var startInfo = BuildStartInfo(registration.PackageDirectory, artifact);
            var session = PluginProcessSession.Start(
                startInfo,
                registration.Manifest.Id,
                _logs,
                (method, parameters, token) => HandleOriginatedRequestAsync(registration.Manifest.Id, method, parameters, token));
            registration.Session = session;
            try
            {
                var handshake = await session.InvokeAsync(
                    "onedesk.handshake",
                    new { pluginId = registration.Manifest.Id, protocolVersion = 1, host = "OneDesk" },
                    HandshakeTimeout,
                    cancellationToken);
                if (handshake.TryGetProperty("error", out var error))
                {
                    throw new InvalidOperationException($"PluginHandshakeRejected:{error}");
                }
                registration.RestartAttempts = 0;
                if (registration.Committed) _ = MonitorPersistentSessionAsync(registration, session);
                return session;
            }
            catch
            {
                registration.Session = null;
                await session.DisposeAsync();
                throw;
            }
        }
        finally
        {
            registration.Lifecycle.Release();
        }
    }

    private async Task MonitorPersistentSessionAsync(PluginRegistration registration, PluginProcessSession session)
    {
        await session.Completion;
        if (registration.Removed || registration.Manifest.Backend?.Persistent != true || Volatile.Read(ref _disposed) != 0) return;
        while (!registration.Removed && registration.Manifest.Backend?.Persistent == true && Volatile.Read(ref _disposed) == 0)
        {
            if (Interlocked.Increment(ref registration.RestartAttempts) > 3)
            {
                _logs.Append("desktop", "Error", "Plugin", "常驻插件连续崩溃，已停止自动重启", new Dictionary<string, object?>
                {
                    ["pluginId"] = registration.Manifest.Id,
                });
                return;
            }
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, registration.RestartAttempts)));
            try
            {
                await EnsureSessionAsync(registration, CancellationToken.None);
                return;
            }
            catch (Exception error)
            {
                _logs.Append("desktop", "Error", "Plugin", "常驻插件重启失败", new Dictionary<string, object?>
                {
                    ["pluginId"] = registration.Manifest.Id,
                    ["attempt"] = registration.RestartAttempts,
                    ["error"] = error.Message,
                });
            }
        }
    }

    private Task<object?> HandleOriginatedRequestAsync(string pluginId, string method, JsonElement parameters, CancellationToken cancellationToken)
    {
        var handler = _originatedRequestHandler;
        if (handler is null) return Task.FromResult<object?>(Error("PluginHostMethodUnavailable", "宿主尚未注册插件请求处理器"));
        return handler(pluginId, method, parameters, cancellationToken);
    }

    private static ProcessStartInfo BuildStartInfo(string packageDirectory, PluginPlatformArtifact artifact)
    {
        var commandHead = artifact.Command.FirstOrDefault();
        var executable = string.IsNullOrWhiteSpace(commandHead) ? ResolvePackagePath(packageDirectory, artifact.Path) : commandHead;
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = string.IsNullOrWhiteSpace(packageDirectory) ? Environment.CurrentDirectory : Path.GetFullPath(packageDirectory),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in artifact.Command.Skip(1)) startInfo.ArgumentList.Add(argument);
        if (!string.IsNullOrWhiteSpace(commandHead) && !string.IsNullOrWhiteSpace(artifact.Path) && artifact.Command.Count == 1)
        {
            startInfo.ArgumentList.Add(ResolvePackagePath(packageDirectory, artifact.Path));
        }
        return startInfo;
    }

    private static string ResolvePackagePath(string packageDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(packageDirectory)) return Path.GetFullPath(relativePath);
        var root = Path.GetFullPath(packageDirectory);
        var path = Path.GetFullPath(Path.IsPathRooted(relativePath) ? relativePath : Path.Combine(root, relativePath));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && !string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("PluginArtifactEscapesPackage");
        }
        if (!File.Exists(path)) throw new FileNotFoundException("PluginArtifactMissing", path);
        return path;
    }

    private static PluginPlatformArtifact? SelectArtifact(IReadOnlyList<PluginPlatformArtifact> artifacts)
    {
        var platform = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        var architecture = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        return artifacts.FirstOrDefault(artifact =>
            string.Equals(artifact.Platform, platform, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(artifact.Architecture, architecture, StringComparison.OrdinalIgnoreCase));
    }

    public static void ValidateManifest(PluginManifest manifest, string packageDirectory)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Name) || string.IsNullOrWhiteSpace(manifest.Version))
            throw new InvalidDataException("PluginManifestIdentityMissing");
        if (manifest.Frontend is not null) ResolvePackagePath(packageDirectory, manifest.Frontend.Entry);
        if (manifest.Backend is not null && !string.Equals(manifest.Backend.Protocol, "json-rpc", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("PluginProtocolUnsupported");
    }

    private static async Task StopSessionAsync(PluginRegistration registration)
    {
        await registration.Lifecycle.WaitAsync();
        try
        {
            var session = registration.Session;
            registration.Session = null;
            if (session is not null) await session.DisposeAsync();
        }
        finally
        {
            registration.Lifecycle.Release();
        }
    }

    private static object Error(string errorCode, string message) => new { ok = false, errorCode, message };

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var registration in _plugins.Values)
        {
            registration.Removed = true;
            StopSessionAsync(registration).GetAwaiter().GetResult();
            registration.Lifecycle.Dispose();
        }
        _plugins.Clear();
        _registrationGate.Dispose();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public sealed class PreparedPluginRegistration : IAsyncDisposable
    {
        private readonly PluginHostService _host;
        private readonly PluginRegistration _candidate;
        private int _state;

        internal PreparedPluginRegistration(PluginHostService host, PluginRegistration candidate)
        {
            _host = host;
            _candidate = candidate;
        }

        public PluginManifest Manifest => _candidate.Manifest;

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            {
                throw new InvalidOperationException("PluginRegistrationAlreadyFinished");
            }

            try
            {
                await _host.CommitPreparedAsync(_candidate, cancellationToken);
                Volatile.Write(ref _state, 2);
            }
            catch
            {
                // 等待注册锁时取消并不代表候选已提交；恢复为可释放状态，确保候选进程不会泄漏。
                // 如果原子替换已经完成，则保持提交状态，避免 DisposeAsync 误停当前生效的插件。
                Volatile.Write(ref _state, _candidate.Committed ? 2 : 0);
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.CompareExchange(ref _state, 3, 0) != 0) return;
            await DiscardPreparedAsync(_candidate);
        }
    }

    internal sealed record PluginRegistration(PluginManifest Manifest, string PackageDirectory)
    {
        public SemaphoreSlim Lifecycle { get; } = new(1, 1);
        public PluginProcessSession? Session { get; set; }
        public int RestartAttempts;
        public volatile bool Removed;
        public volatile bool Committed;
    }
}
