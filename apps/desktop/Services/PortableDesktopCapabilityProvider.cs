using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace OneDesk.Desktop.Services;

public sealed class PortableDesktopCapabilityProvider : IDesktopCapabilityProvider
{
    private readonly DesktopCredentialVault _credentials;

    public PortableDesktopCapabilityProvider(DesktopCredentialVault credentials)
    {
        _credentials = credentials;
    }

    public IReadOnlySet<string> CapabilityIds => DesktopCapabilityContracts.Portable;

    public Task<JsApiResult> ExecuteAsync(JsApiRequest request, CancellationToken cancellationToken = default) => request.Capability switch
    {
        "device.platform" => Task.FromResult(Platform()),
        "file.external.read" => ReadExternalAsync(request, cancellationToken),
        "file.external.write" => WriteExternalAsync(request, cancellationToken),
        "file.external.delete" => Task.FromResult(DeleteExternal(request)),
        "process.launch" => Task.FromResult(Launch(request)),
        "process.control" => Task.FromResult(ControlProcess(request)),
        "shell.execute" => ExecuteShellAsync(request, cancellationToken),
        "credential.access" => AccessCredentialAsync(request, cancellationToken),
        _ => Task.FromResult(JsApiResult.Error("CapabilityPlatformHandlerMissing", "桌面能力提供器未注册该能力。")),
    };

    private static JsApiResult Platform() => JsApiResult.Success(new
    {
        os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : OperatingSystem.IsLinux() ? "linux" : "unknown",
        version = Environment.OSVersion.VersionString,
        architecture = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
        processArchitecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
        framework = RuntimeInformation.FrameworkDescription,
    });

    private static async Task<JsApiResult> ReadExternalAsync(JsApiRequest request, CancellationToken cancellationToken)
    {
        var path = AbsolutePath(request);
        if (!File.Exists(path)) return JsApiResult.Error("FileNotFound", "外部文件不存在。");
        var info = new FileInfo(path);
        if (info.Length > 64L * 1024 * 1024) return JsApiResult.Error("FileTooLarge", "外部文件读取上限为 64 MiB。");
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        return JsApiResult.Success(new { path, sizeBytes = bytes.Length, base64 = Convert.ToBase64String(bytes) });
    }

    private static async Task<JsApiResult> WriteExternalAsync(JsApiRequest request, CancellationToken cancellationToken)
    {
        var path = AbsolutePath(request);
        var base64 = ReadString(request.Payload, "base64", "");
        var content = ReadString(request.Payload, "content", "");
        byte[] bytes;
        try
        {
            bytes = string.IsNullOrWhiteSpace(base64) ? Encoding.UTF8.GetBytes(content) : Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return JsApiResult.Error("InvalidPayload", "base64 内容格式不正确。");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        return JsApiResult.Success(new { path, sizeBytes = bytes.Length });
    }

    private static JsApiResult DeleteExternal(JsApiRequest request)
    {
        var path = AbsolutePath(request);
        if (!File.Exists(path)) return JsApiResult.Success(new { path, deleted = false });
        File.Delete(path);
        return JsApiResult.Success(new { path, deleted = true });
    }

    private static JsApiResult Launch(JsApiRequest request)
    {
        var fileName = ReadString(request.Payload, "fileName", ReadString(request.Payload, "uri", ""));
        if (string.IsNullOrWhiteSpace(fileName)) return JsApiResult.Error("InvalidPayload", "process.launch 需要 fileName 或 uri。");
        var startInfo = new ProcessStartInfo { FileName = fileName, UseShellExecute = ReadBool(request.Payload, "useShellExecute", true) };
        foreach (var argument in ReadStringArray(request.Payload, "arguments")) startInfo.ArgumentList.Add(argument);
        var process = Process.Start(startInfo);
        return process is null
            ? JsApiResult.Error("ExecutionFailed", "系统未能启动目标进程。")
            : JsApiResult.Success(new { processId = process.Id, processName = process.ProcessName });
    }

    private static JsApiResult ControlProcess(JsApiRequest request)
    {
        var processId = ReadInt(request.Payload, "processId", 0);
        var action = ReadString(request.Payload, "action", "terminate");
        if (processId <= 0) return JsApiResult.Error("InvalidPayload", "process.control 需要有效的 processId。");
        if (!string.Equals(action, "terminate", StringComparison.OrdinalIgnoreCase))
            return JsApiResult.Error("CapabilityNotSupported", "当前跨平台实现只支持 terminate 操作。");
        var process = Process.GetProcessById(processId);
        process.Kill(entireProcessTree: ReadBool(request.Payload, "entireProcessTree", true));
        return JsApiResult.Success(new { processId, action = "terminate" });
    }

    private static async Task<JsApiResult> ExecuteShellAsync(JsApiRequest request, CancellationToken cancellationToken)
    {
        var command = ReadString(request.Payload, "command", "");
        if (string.IsNullOrWhiteSpace(command)) return JsApiResult.Error("InvalidPayload", "shell.execute 需要 command。");
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(OperatingSystem.IsWindows() ? "/d" : "-c");
        if (OperatingSystem.IsWindows()) startInfo.ArgumentList.Add("/s");
        if (OperatingSystem.IsWindows()) startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(command);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("ShellProcessStartFailed");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(ReadInt(request.Payload, "timeoutSeconds", 15), 1, 30)));
        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            return JsApiResult.Error("ExecutionTimeout", "Shell 命令执行超时。");
        }
        var output = await outputTask;
        var error = await errorTask;
        return JsApiResult.Success(new
        {
            exitCode = process.ExitCode,
            stdout = Limit(output),
            stderr = Limit(error),
            truncated = output.Length > 256_000 || error.Length > 256_000,
        });
    }

    private async Task<JsApiResult> AccessCredentialAsync(JsApiRequest request, CancellationToken cancellationToken)
    {
        var operation = ReadString(request.Payload, "operation", "read").ToLowerInvariant();
        var key = ReadString(request.Payload, "key", "");
        var sourceKey = PermissionService.SourceKey(request.Source);
        return operation switch
        {
            "read" => JsApiResult.Success(new { key, value = await _credentials.ReadAsync(sourceKey, key, cancellationToken) }),
            "write" => await WriteCredentialAsync(sourceKey, key, ReadString(request.Payload, "value", ""), cancellationToken),
            "delete" => JsApiResult.Success(new { key, deleted = _credentials.Delete(sourceKey, key) }),
            _ => JsApiResult.Error("InvalidPayload", "credential.access operation 必须是 read、write 或 delete。"),
        };
    }

    private async Task<JsApiResult> WriteCredentialAsync(string sourceKey, string key, string value, CancellationToken cancellationToken)
    {
        await _credentials.WriteAsync(sourceKey, key, value, cancellationToken);
        return JsApiResult.Success(new { key, written = true });
    }

    private static string AbsolutePath(JsApiRequest request)
    {
        var path = ReadString(request.Payload, "path", "");
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) throw new InvalidDataException("ExternalPathMustBeAbsolute");
        return Path.GetFullPath(path);
    }

    private static string Limit(string value) => value.Length > 256_000 ? value[..256_000] : value;

    private static JsonElement? ReadElement(object? payload, string key) =>
        payload is JsonElement { ValueKind: JsonValueKind.Object } element && element.TryGetProperty(key, out var value) ? value : null;
    private static string ReadString(object? payload, string key, string fallback) =>
        ReadElement(payload, key) is { ValueKind: JsonValueKind.String } value ? value.GetString() ?? fallback : fallback;
    private static int ReadInt(object? payload, string key, int fallback) =>
        ReadElement(payload, key) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var number) ? number : fallback;
    private static bool ReadBool(object? payload, string key, bool fallback) =>
        ReadElement(payload, key) is { ValueKind: JsonValueKind.True } ? true : ReadElement(payload, key) is { ValueKind: JsonValueKind.False } ? false : fallback;
    private static IReadOnlyList<string> ReadStringArray(object? payload, string key) =>
        ReadElement(payload, key) is { ValueKind: JsonValueKind.Array } value
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToArray()
            : [];
}
