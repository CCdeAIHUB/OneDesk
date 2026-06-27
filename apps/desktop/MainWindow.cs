using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using OneDesk.Desktop.Services;

namespace OneDesk.Desktop;

public sealed class MainWindow : Window
{
    public MainWindow(PairingService pairing, FrontendNetworkPolicy networkPolicy)
    {
        Title = "OneDesk";
        Width = 1280;
        Height = 820;
        MinWidth = 1024;
        MinHeight = 720;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
        TransparencyLevelHint =
        [
            WindowTransparencyLevel.Mica,
            WindowTransparencyLevel.AcrylicBlur,
            WindowTransparencyLevel.Blur,
            WindowTransparencyLevel.Transparent
        ];
        Background = new SolidColorBrush(Color.FromArgb(160, 240, 249, 255));

        var code = pairing.GenerateVerificationCode();
        var qrPayload = pairing.CreateQrPayload("127.0.0.1", 48320, code);
        networkPolicy.BlockDirectFrontendNetworking = true;

        Content = new Border
        {
            Margin = new Thickness(14),
            CornerRadius = new CornerRadius(24),
            Background = Brushes.White,
            BoxShadow = BoxShadows.Parse("0 24 80 0 #220EA5E9"),
            Child = new Grid
            {
                RowDefinitions = RowDefinitions.Parse("Auto,*"),
                Children =
                {
                    new TextBlock
                    {
                        Text = "OneDesk desktop shell",
                        FontSize = 26,
                        FontWeight = FontWeight.SemiBold,
                        Margin = new Thickness(24, 20, 24, 8)
                    },
                    new TextBlock
                    {
                        Text = $"Chromium file frontend host placeholder\nNetwork blocked: {networkPolicy.BlockDirectFrontendNetworking}\nPairing QR payload: {qrPayload}",
                        Margin = new Thickness(24, 74, 24, 24),
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        };
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            lifetime.ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
    }
}
