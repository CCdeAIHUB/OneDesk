using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using OneDesk.Desktop.Services;

namespace OneDesk.Desktop;

public sealed partial class OneDeskApp : Application
{
    private ServiceProvider? _services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var collection = new ServiceCollection();
        collection.AddSingleton<DeviceRegistry>();
        collection.AddSingleton<PermissionService>();
        collection.AddSingleton<StructuredLogStore>();
        collection.AddSingleton<PairingService>();
        collection.AddSingleton<SchemePackageService>();
        collection.AddSingleton<PluginHostService>();
        collection.AddSingleton<JsApiRouter>();
        collection.AddSingleton<FrontendNetworkPolicy>();
        collection.AddSingleton<MainWindow>();
        _services = collection.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _services.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
