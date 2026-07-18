using System.Text.Json;

namespace OneDesk.Windows;

public sealed partial class MainForm
{
    internal void ShowNativeNotification(string title, string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ShowNativeNotification(title, message));
            return;
        }

        EnsureTrayIcon();
        _notifyIcon?.ShowBalloonTip(4000, title, message, ToolTipIcon.Info);
    }

    private void ShowInAppNotification(string message)
    {
        if (_browser?.CoreWebView2 is null) return;
        var encoded = JsonSerializer.Serialize(message, BridgeJsonOptions);
        _ = _browser.CoreWebView2.ExecuteScriptAsync(
            $"window.dispatchEvent(new CustomEvent('onedesk-in-app-notification', {{ detail: {encoded} }}))");
    }

    private void EnsureTrayIcon()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = true;
            return;
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add("显示程序", null, (_, _) => ShowMainWindow());
        menu.Items.Add("退出程序", null, async (_, _) => await ConfirmExitFromTrayAsync());
        _notifyIcon = new NotifyIcon
        {
            Icon = Icon ?? SystemIcons.Application,
            Visible = true,
            Text = "OneDesk",
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        Show();
        ShowInTaskbar = true;
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    private void HideToTray()
    {
        EnsureTrayIcon();
        Hide();
        ShowInTaskbar = false;
    }

    private async Task ConfirmExitFromTrayAsync()
    {
        ShowMainWindow();
        var result = MessageBox.Show(
            this,
            "是否退出 OneDesk 程序？",
            "退出程序",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (result != DialogResult.Yes) return;

        _allowExit = true;
        if (_notifyIcon is not null) _notifyIcon.Visible = false;
        if (_gateway is not null) await _gateway.StopAsync();
        Close();
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_allowExit) return;
        eventArgs.Cancel = true;
        HideToTray();
    }
}
