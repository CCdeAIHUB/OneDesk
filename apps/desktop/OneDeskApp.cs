using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using OneDesk.Desktop.Services;
using OneDesk.Desktop.Shell;
using OneDesk.Desktop.Storage;

namespace OneDesk.Desktop;

public sealed partial class OneDeskApp : Application
{
    private ServiceProvider? _services;
    private TrayIcon? _trayIcon;
    private DesktopLifetimeCoordinator? _lifetime;
    private bool _servicesDisposed;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var collection = new ServiceCollection();
        collection.AddSingleton<DeviceRegistry>();
        collection.AddSingleton<CapabilityDirectoryService>();
        collection.AddSingleton<PermissionService>();
        collection.AddSingleton<StructuredLogStore>();
        collection.AddSingleton<PairingService>();
        collection.AddSingleton<QuicGatewayService>();
        collection.AddSingleton<SchemePackageService>();
        collection.AddSingleton<PluginHostService>();
        collection.AddSingleton<PluginFrontendSessionRegistry>();
        collection.AddSingleton<DesktopCredentialVault>();
        collection.AddSingleton<IDesktopCapabilityProvider, PortableDesktopCapabilityProvider>();
        collection.AddSingleton<IDesktopCapabilityProvider, DesktopSchemeCapabilityProvider>();
        collection.AddSingleton<JsApiRouter>();
        collection.AddSingleton<FrontendNetworkPolicy>();
        collection.AddSingleton<PortableDesktopSettingsService>();
        collection.AddSingleton<AvaloniaDesktopShellPlatform>();
        collection.AddSingleton<IDesktopShellPlatform>(services => services.GetRequiredService<AvaloniaDesktopShellPlatform>());
        collection.AddSingleton<DesktopBridgeDispatcher>();
        collection.AddSingleton<DesktopNativeBridge>();
        collection.AddSingleton<OneDeskDataPaths>();
        collection.AddSingleton<JsonFileStore>();
        collection.AddSingleton<OneDeskRepository>();
        collection.AddSingleton<MainWindow>();
        _services = collection.BuildServiceProvider();
        var gateway = _services.GetRequiredService<QuicGatewayService>();
        gateway.AttachJsApiRouter(_services.GetRequiredService<JsApiRouter>());
        var dispatcher = _services.GetRequiredService<DesktopBridgeDispatcher>();
        dispatcher.LoadInstalledPluginsAsync().GetAwaiter().GetResult();
        var settings = _services.GetRequiredService<PortableDesktopSettingsService>().LoadAsync().GetAwaiter().GetResult();
        gateway.StartAsync(settings.GatewayPort).GetAwaiter().GetResult();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var window = _services.GetRequiredService<MainWindow>();
            var platform = _services.GetRequiredService<AvaloniaDesktopShellPlatform>();
            desktop.MainWindow = window;
            _lifetime = new DesktopLifetimeCoordinator(
                () => ConfirmExitAsync(window),
                window.Hide,
                platform.ShowWindow,
                () => ShutdownAsync(desktop, gateway));

            window.Closing += (_, eventArgs) =>
            {
                if (_lifetime.IsExitApproved)
                {
                    return;
                }

                eventArgs.Cancel = true;
                _lifetime.CloseWindow();
            };
            _trayIcon = CreateTrayIcon(platform, _lifetime);
            TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static TrayIcon CreateTrayIcon(
        AvaloniaDesktopShellPlatform platform,
        DesktopLifetimeCoordinator lifetime)
    {
        var show = new NativeMenuItem("显示程序");
        show.Click += (_, _) => platform.ShowWindow();
        var exit = new NativeMenuItem("退出程序");
        exit.Click += async (_, _) => await lifetime.RequestExitAsync();

        return new TrayIcon
        {
            Icon = TrayIconFactory.CreateOneDeskIcon(),
            ToolTipText = "OneDesk",
            IsVisible = true,
            Menu = new NativeMenu { Items = { show, exit } },
        };
    }

    private static Task<bool> ConfirmExitAsync(Window owner)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return new ExitConfirmationDialog(owner).ShowDialog<bool>(owner);
        }

        return Dispatcher.UIThread.InvokeAsync(
            () => new ExitConfirmationDialog(owner).ShowDialog<bool>(owner));
    }

    private async Task ShutdownAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        QuicGatewayService gateway)
    {
        if (_servicesDisposed)
        {
            return;
        }

        _servicesDisposed = true;
        _trayIcon!.IsVisible = false;
        await gateway.StopAsync();
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }
        desktop.Shutdown();
    }
}
