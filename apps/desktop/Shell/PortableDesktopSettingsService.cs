using System.Security;
using System.Text.Json;
using Microsoft.Win32;
using OneDesk.Desktop.Storage;

namespace OneDesk.Desktop.Shell;

public sealed record DesktopAppSettings(bool StartWithWindows, int GatewayPort)
{
    public static DesktopAppSettings Default { get; } = new(false, 48320);
}

/// <summary>
/// 将通用设置文件与各系统的开机启动适配收敛到平台服务，前端不直接访问注册表或启动目录。
/// </summary>
public sealed class PortableDesktopSettingsService
{
    private const string WindowsStartupPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string _settingsPath;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public PortableDesktopSettingsService(OneDeskDataPaths paths)
    {
        paths.EnsureCreated();
        _settingsPath = Path.Combine(paths.Root, "desktop-settings.json");
    }

    public async Task<DesktopAppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        var settings = DesktopAppSettings.Default;
        if (File.Exists(_settingsPath))
        {
            try
            {
                await using var stream = File.OpenRead(_settingsPath);
                settings = await JsonSerializer.DeserializeAsync<DesktopAppSettings>(stream, _jsonOptions, cancellationToken)
                    ?? DesktopAppSettings.Default;
            }
            catch (JsonException)
            {
                // 损坏的设置不能阻断启动；默认值会在用户下一次保存时覆盖损坏文件。
                settings = DesktopAppSettings.Default;
            }
        }

        return settings with
        {
            GatewayPort = settings.GatewayPort is >= 1024 and <= 65535 ? settings.GatewayPort : 48320,
            StartWithWindows = IsAutoStartEnabled(),
        };
    }

    public async Task SaveAsync(DesktopAppSettings settings, CancellationToken cancellationToken = default)
    {
        if (settings.GatewayPort is < 1024 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(settings.GatewayPort), "监听端口必须在 1024 到 65535 之间");
        }

        SetAutoStartEnabled(settings.StartWithWindows);
        var temporary = $"{_settingsPath}.tmp-{Guid.NewGuid():N}";
        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, settings, _jsonOptions, cancellationToken);
        }
        File.Move(temporary, _settingsPath, overwrite: true);
    }

    private static bool IsAutoStartEnabled()
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.OpenSubKey(WindowsStartupPath, writable: false);
            return key?.GetValue("OneDesk") is string value && !string.IsNullOrWhiteSpace(value);
        }

        return File.Exists(AutoStartFilePath());
    }

    private static void SetAutoStartEnabled(bool enabled)
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.CreateSubKey(WindowsStartupPath, writable: true)
                ?? throw new InvalidOperationException("无法打开 Windows 开机启动注册表项");
            if (enabled) key.SetValue("OneDesk", $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue("OneDesk", throwOnMissingValue: false);
            return;
        }

        var path = AutoStartFilePath();
        if (!enabled)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var executable = SecurityElement.Escape(Environment.ProcessPath ?? throw new InvalidOperationException("无法确定 OneDesk 可执行文件路径"));
        var content = OperatingSystem.IsMacOS()
            ? $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><plist version=\"1.0\"><dict><key>Label</key><string>cc.onedesk.desktop</string><key>ProgramArguments</key><array><string>{executable}</string></array><key>RunAtLoad</key><true/></dict></plist>"
            : $"[Desktop Entry]\nType=Application\nName=OneDesk\nExec=\"{executable}\"\nTerminal=false\nX-GNOME-Autostart-enabled=true\n";
        File.WriteAllText(path, content);
    }

    private static string AutoStartFilePath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return OperatingSystem.IsMacOS()
            ? Path.Combine(home, "Library", "LaunchAgents", "cc.onedesk.desktop.plist")
            : Path.Combine(home, ".config", "autostart", "onedesk.desktop");
    }
}
