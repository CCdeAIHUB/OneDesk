using System.Text.Json;
using Microsoft.Win32;

namespace OneDesk.Windows;

/// <summary>
/// 管理仅属于 Windows 壳子的运行设置。前端只提交用户意图，注册表和文件写入始终由壳子完成。
/// </summary>
public sealed class DesktopSettingsService
{
    private const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "OneDesk";
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OneDesk",
        "desktop-settings.json");
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<DesktopAppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        DesktopAppSettings settings;
        if (!File.Exists(_settingsPath))
        {
            settings = DesktopAppSettings.Default;
        }
        else
        {
            try
            {
                await using var stream = File.OpenRead(_settingsPath);
                settings = await JsonSerializer.DeserializeAsync<DesktopAppSettings>(stream, _jsonOptions, cancellationToken)
                    ?? DesktopAppSettings.Default;
            }
            catch (JsonException)
            {
                // 设置文件损坏时使用安全默认值，不能阻止主窗口和连接服务启动。
                settings = DesktopAppSettings.Default;
            }
        }

        var validPort = settings.GatewayPort is >= 1024 and <= 65535 ? settings.GatewayPort : DesktopAppSettings.Default.GatewayPort;
        return settings with
        {
            GatewayPort = validPort,
            StartWithWindows = IsStartupEnabled()
        };
    }

    public async Task SaveAsync(DesktopAppSettings settings, CancellationToken cancellationToken = default)
    {
        if (settings.GatewayPort is < 1024 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(settings.GatewayPort), "监听端口必须在 1024 到 65535 之间");
        }

        SetStartupEnabled(settings.StartWithWindows);
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var tempPath = $"{_settingsPath}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, _jsonOptions, cancellationToken);
        }

        if (File.Exists(_settingsPath))
        {
            File.Replace(tempPath, _settingsPath, null);
        }
        else
        {
            File.Move(tempPath, _settingsPath);
        }
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, writable: false);
        return key?.GetValue(StartupValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    private static void SetStartupEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(StartupRegistryPath, writable: true)
            ?? throw new InvalidOperationException("无法打开 Windows 开机启动注册表项");
        if (enabled)
        {
            key.SetValue(StartupValueName, $"\"{Application.ExecutablePath}\"");
        }
        else
        {
            key.DeleteValue(StartupValueName, throwOnMissingValue: false);
        }
    }
}

public sealed record DesktopAppSettings(bool StartWithWindows, int GatewayPort)
{
    public static DesktopAppSettings Default { get; } = new(false, 48320);
}
