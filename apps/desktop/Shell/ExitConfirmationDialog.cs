using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace OneDesk.Desktop.Shell;

internal sealed class ExitConfirmationDialog : Window
{
    public ExitConfirmationDialog(Window owner)
    {
        var dark = owner.ActualThemeVariant == ThemeVariant.Dark;
        var surface = new SolidColorBrush(Color.Parse(dark ? "#111827" : "#ffffff"));
        var foreground = new SolidColorBrush(Color.Parse(dark ? "#f8fafc" : "#0f172a"));
        var secondary = new SolidColorBrush(Color.Parse(dark ? "#94a3b8" : "#64748b"));
        var neutral = new SolidColorBrush(Color.Parse(dark ? "#1e293b" : "#f1f5f9"));
        var primary = new SolidColorBrush(Color.Parse("#0ea5e9"));

        Title = "退出程序";
        Width = 360;
        Height = 184;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        var cancel = CreateButton("取消", neutral, foreground);
        cancel.Click += (_, _) => Close(false);
        var confirm = CreateButton("退出", primary, Brushes.White);
        confirm.Click += (_, _) => Close(true);

        Content = new Border
        {
            Background = surface,
            BorderBrush = new SolidColorBrush(Color.Parse(dark ? "#334155" : "#dbe4ee")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(22),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,*"),
                Children =
                {
                    new TextBlock
                    {
                        Text = "退出 OneDesk？",
                        FontSize = 16,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = foreground,
                    },
                    new TextBlock
                    {
                        Text = "退出后，移动设备连接和后台任务将停止。",
                        FontSize = 13,
                        Foreground = secondary,
                        Margin = new Thickness(0, 10, 0, 0),
                        [Grid.RowProperty] = 1,
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Spacing = 10,
                        Children = { cancel, confirm },
                        [Grid.RowProperty] = 2,
                    },
                },
            },
        };
    }

    private static Button CreateButton(string text, IBrush background, IBrush foreground) => new()
    {
        Content = text,
        Width = 76,
        Height = 34,
        CornerRadius = new CornerRadius(6),
        Background = background,
        Foreground = foreground,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
    };
}
