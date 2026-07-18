using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace OneDesk.Desktop.Services;

internal sealed class PluginProcessSession : IAsyncDisposable
{
    private readonly Process _process;
    private readonly StructuredLogStore _logs;
    private readonly string _pluginId;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly PluginRpcChannel _channel;
    private readonly Task _stdoutLoop;
    private readonly Task _stderrLoop;
    private readonly Task _exitLoop;
    private readonly Task _resourceLoop;
    private int _stopping;

    private PluginProcessSession(
        Process process,
        string pluginId,
        StructuredLogStore logs,
        Func<string, JsonElement, CancellationToken, Task<object?>> requestHandler)
    {
        _process = process;
        _pluginId = pluginId;
        _logs = logs;
        _channel = new PluginRpcChannel(WriteLineAsync, requestHandler);
        _stdoutLoop = ReadStdoutAsync();
        _stderrLoop = ReadStderrAsync();
        _exitLoop = ObserveExitAsync();
        _resourceLoop = MonitorResourcesAsync();
    }

    public Process Process => _process;
    public bool IsRunning => !_process.HasExited && Volatile.Read(ref _stopping) == 0;
    public Task Completion => _exitLoop;

    public static PluginProcessSession Start(
        ProcessStartInfo startInfo,
        string pluginId,
        StructuredLogStore logs,
        Func<string, JsonElement, CancellationToken, Task<object?>> requestHandler)
    {
        var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"PluginStartFailed:{pluginId}");
        return new PluginProcessSession(process, pluginId, logs, requestHandler);
    }

    public Task<JsonElement> InvokeAsync(
        string method,
        object? parameters,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        _channel.InvokeAsync(method, parameters, timeout, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0) return;
        _lifetime.Cancel();
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }
        _channel.FailPending(new OperationCanceledException($"PluginStopped:{_pluginId}"));
        await IgnoreLoopFailuresAsync(Task.WhenAll(_stdoutLoop, _stderrLoop, _exitLoop, _resourceLoop));
        _process.Dispose();
        _writeGate.Dispose();
        _lifetime.Dispose();
    }

    private async Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsRunning) throw new InvalidOperationException($"PluginProcessExited:{_pluginId}");
            await _process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken);
            await _process.StandardInput.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task ReadStdoutAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var line = await ReadBoundedLineAsync(_process.StandardOutput, 1024 * 1024, _lifetime.Token);
                if (line is null) break;
                try
                {
                    await _channel.AcceptLineAsync(line, _lifetime.Token);
                }
                catch (JsonException error)
                {
                    _logs.Append("desktop", "Error", "Plugin", "插件输出了无效 JSON-RPC 数据", new Dictionary<string, object?>
                    {
                        ["pluginId"] = _pluginId,
                        ["error"] = error.Message,
                    });
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            StopForProtocolViolation("插件标准输出读取失败", error);
        }
    }

    private async Task ReadStderrAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var line = await ReadBoundedLineAsync(_process.StandardError, 16 * 1024, _lifetime.Token);
                if (line is null) break;
                _logs.Append("desktop", "Warning", "PluginStderr", "插件后端输出错误流", new Dictionary<string, object?>
                {
                    ["pluginId"] = _pluginId,
                    ["message"] = line.Length > 2_000 ? line[..2_000] : line,
                });
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            StopForProtocolViolation("插件错误输出读取失败", error);
        }
    }

    private async Task ObserveExitAsync()
    {
        try
        {
            await _process.WaitForExitAsync(_lifetime.Token);
            var error = new InvalidOperationException($"PluginProcessExited:{_pluginId}:ExitCode={_process.ExitCode}");
            _channel.FailPending(error);
            if (Volatile.Read(ref _stopping) == 0)
            {
                _logs.Append("desktop", "Error", "Plugin", "插件后端进程意外退出", new Dictionary<string, object?>
                {
                    ["pluginId"] = _pluginId,
                    ["exitCode"] = _process.ExitCode,
                });
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async Task MonitorResourcesAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested && !_process.HasExited)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), _lifetime.Token);
                _process.Refresh();
                if (_process.WorkingSet64 <= 512L * 1024 * 1024) continue;
                StopForProtocolViolation(
                    "插件后端超过 512 MiB 内存限制",
                    new InvalidOperationException($"PluginMemoryLimitExceeded:{_process.WorkingSet64}"));
                return;
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private void StopForProtocolViolation(string message, Exception error)
    {
        _logs.Append("desktop", "Error", "Plugin", message, new Dictionary<string, object?>
        {
            ["pluginId"] = _pluginId,
            ["error"] = error.Message,
        });
        _channel.FailPending(error);
        if (!_process.HasExited) _process.Kill(entireProcessTree: true);
    }

    private static async Task<string?> ReadBoundedLineAsync(StreamReader reader, int maximumCharacters, CancellationToken cancellationToken)
    {
        var line = new StringBuilder(Math.Min(maximumCharacters, 4096));
        var buffer = new char[1];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);
            if (read == 0) return line.Length == 0 ? null : line.ToString();
            if (buffer[0] == '\n') return line.ToString();
            if (buffer[0] != '\r') line.Append(buffer[0]);
            if (line.Length > maximumCharacters) throw new InvalidDataException("PluginOutputLineTooLarge");
        }
    }

    private async Task IgnoreLoopFailuresAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            _logs.Append("desktop", "Error", "Plugin", "插件进程清理期间发生错误", new Dictionary<string, object?>
            {
                ["pluginId"] = _pluginId,
                ["error"] = error.Message,
            });
        }
    }
}
