using System.Diagnostics;

namespace OneDesk.Desktop.Services;

public sealed class PluginHostService
{
    private readonly StructuredLogStore _logs;
    private readonly List<Process> _processes = [];

    public PluginHostService(StructuredLogStore logs)
    {
        _logs = logs;
    }

    public IReadOnlyList<Process> Processes => _processes;

    public Task RegisterManifestAsync(PluginManifest manifest, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logs.Append("desktop", "Info", "Plugin", "Registered plugin manifest", new Dictionary<string, object?>
        {
            ["pluginId"] = manifest.Id,
            ["persistent"] = manifest.Persistent
        });
        return Task.CompletedTask;
    }

    public Task<object?> InvokeAsync(string pluginId, string method, object? parameters, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logs.Append("desktop", "Info", "Plugin", "Invoked plugin method", new Dictionary<string, object?>
        {
            ["pluginId"] = pluginId,
            ["method"] = method
        });
        return Task.FromResult<object?>(new { ok = true });
    }
}
