using Avalonia;
using OneDesk.Desktop.Storage;
using Xilium.CefGlue;
using Xilium.CefGlue.Common;

namespace OneDesk.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var paths = new OneDeskDataPaths();
        paths.EnsureCreated();
        CefRuntimeLoader.Initialize(
            new CefSettings
            {
                RootCachePath = Path.Combine(paths.Cache, "cef"),
                WindowlessRenderingEnabled = true,
            },
            [KeyValuePair.Create("allow-file-access-from-files", "1")]);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<OneDeskApp>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
