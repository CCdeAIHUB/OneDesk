using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace OneDesk.Desktop.Services;

public sealed class PluginHostService : IDisposable
{
    private readonly StructuredLogStore _logs;
    private readonly ConcurrentDictionary<string, PluginRegistration> _plugins = new();
    private readonly List<Process> _processes = [];
    private int _rpcSequence;

    public PluginHostService(StructuredLogStore logs)
    {
        _logs = logs;
    }

    public IReadOnlyList<Process> Processes => _processes;

    public IReadOnlyList<PluginManifest> InstalledPlugins => _plugins.Values.Select(registration => registration.Manifest).ToArray();

    public async Task RegisterManifestAsync(PluginManifest manifest, string packageDirectory = "", CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var registration = new PluginRegistration(manifest, packageDirectory);
        _plugins[manifest.Id] = registration;
        _logs.Append("desktop", "Info", "Plugin", "Registered plugin manifest", new Dictionary<string, object?>
        {
            ["pluginId"] = manifest.Id,
            ["persistent"] = manifest.Backend?.Persistent ?? manifest.Persistent,
            ["hasFrontend"] = manifest.Frontend is not null,
            ["hasBackend"] = manifest.Backend is not null
        });

        if (manifest.Backend?.Persistent == true)
        {
            await EnsureProcessAsync(registration, cancellationToken);
        }
    }

    public async Task<object?> InvokeAsync(string pluginId, string method, object? parameters, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_plugins.TryGetValue(pluginId, out var registration))
        {
            _logs.Append("desktop", "Warning", "Plugin", "Plugin invocation failed because plugin is not installed", new Dictionary<string, object?>
            {
                ["pluginId"] = pluginId,
                ["method"] = method
            });
            return new { ok = false, errorCode = "PluginNotInstalled", message = "插件未安装" };
        }

        if (registration.Manifest.Backend is null)
        {
            return new { ok = false, errorCode = "PluginBackendMissing", message = "插件没有后端能力" };
        }

        var process = await EnsureProcessAsync(registration, cancellationToken);
        var payload = new
        {
            jsonrpc = "2.0",
            id = Interlocked.Increment(ref _rpcSequence),
            method = "onedesk.invoke",
            @params = new
            {
                method,
                @params = parameters,
                source = new { kind = "system" }
            }
        };

        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(payload, JsonOptions));
        await process.StandardInput.FlushAsync();
        var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
        _logs.Append("desktop", "Info", "Plugin", "Invoked plugin method", new Dictionary<string, object?>
        {
            ["pluginId"] = pluginId,
            ["method"] = method
        });

        return string.IsNullOrWhiteSpace(line)
            ? new { ok = false, errorCode = "PluginNoResponse", message = "插件进程没有返回响应" }
            : JsonSerializer.Deserialize<JsonElement>(line);
    }

    private Task<Process> EnsureProcessAsync(PluginRegistration registration, CancellationToken cancellationToken)
    {
        if (registration.Process is { HasExited: false } process)
        {
            return Task.FromResult(process);
        }

        var artifact = SelectArtifact(registration.Manifest.Backend?.Artifacts ?? []);
        if (artifact is null)
        {
            throw new InvalidOperationException($"Plugin {registration.Manifest.Id} does not provide an artifact for this platform.");
        }

        var executable = ResolveArtifactExecutable(registration.PackageDirectory, artifact);
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? registration.PackageDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in artifact.Command.Skip(1))
        {
            startInfo.ArgumentList.Add(argument);
        }

        var started = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start plugin {registration.Manifest.Id}.");
        registration.Process = started;
        _processes.Add(started);
        _logs.Append("desktop", "Info", "Plugin", "Started plugin backend process", new Dictionary<string, object?>
        {
            ["pluginId"] = registration.Manifest.Id,
            ["path"] = executable
        });
        return Task.FromResult(started);
    }

    private static PluginPlatformArtifact? SelectArtifact(IReadOnlyList<PluginPlatformArtifact> artifacts)
    {
        var platform = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        var architecture = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        return artifacts.FirstOrDefault(artifact =>
            string.Equals(artifact.Platform, platform, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(artifact.Architecture, architecture, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveArtifactExecutable(string packageDirectory, PluginPlatformArtifact artifact)
    {
        var commandHead = artifact.Command.FirstOrDefault();
        var path = string.IsNullOrWhiteSpace(commandHead) ? artifact.Path : commandHead;
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(packageDirectory, path));
    }

    public void Dispose()
    {
        foreach (var process in _processes)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                process.Dispose();
            }
            catch
            {
                // Plugin cleanup should never prevent OneDesk from closing.
            }
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record PluginRegistration(PluginManifest Manifest, string PackageDirectory)
    {
        public Process? Process { get; set; }
    }
}
