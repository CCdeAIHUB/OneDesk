using System.Drawing;
using System.Runtime.InteropServices;

namespace OneDesk.Windows;

public sealed partial class MainForm
{
    private const int BaseResizeGripSize = 14;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const int HtClient = 1;
    private const int HtCaption = 2;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const int WmNcCalcSize = 0x0083;
    private const int WmNcHitTest = 0x0084;
    private const int WmNcPaint = 0x0085;
    private const int WmNcActivate = 0x0086;
    private const int WmSetCursor = 0x0020;
    private const int WmNcLButtonDown = 0x00A1;
    private const int WmExitSizeMove = 0x0232;
    private const int WsCaption = 0x00C00000;
    private const int WsThickFrame = 0x00040000;
    private const int IdcSizenwse = 32642;
    private const int IdcSizenesw = 32643;
    private const int IdcSizewe = 32644;
    private const int IdcSizens = 32645;
    private const int DwmwaNcRenderingPolicy = 2;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmncrpDisabled = 2;
    private const int DwmwcpRound = 2;
    private const int WcaAccentPolicy = 19;
    private const int AccentEnableAcrylicBlurBehind = 4;

    protected override CreateParams CreateParams
    {
        get
        {
            var createParams = base.CreateParams;
            createParams.Style &= ~WsCaption;
            createParams.Style |= WsThickFrame;
            return createParams;
        }
    }

    private void EnsureInitialWindowBounds()
    {
        var workingArea = Screen.FromControl(this).WorkingArea;
        var width = Math.Max(Math.Min(Size.Width, workingArea.Width - 48), MinimumSize.Width);
        var height = Math.Max(Math.Min(Size.Height, workingArea.Height - 48), MinimumSize.Height);
        var left = workingArea.Left + (workingArea.Width - width) / 2;
        var top = workingArea.Top + (workingArea.Height - height) / 2;
        SetWindowPos(Handle, nint.Zero, left, top, width, height, SwpNoZOrder | SwpNoActivate);
        AppDiagnostics.Write($"Initial window bounds applied: {width}x{height} at {left},{top}.");
    }

    protected override void OnSizeChanged(EventArgs eventArgs)
    {
        base.OnSizeChanged(eventArgs);
        LayoutBrowser();
        ApplyBlurBehind();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs eventArgs)
    {
        base.OnDpiChanged(eventArgs);
        var scale = eventArgs.DeviceDpiNew / 96d;
        MinimumSize = new Size((int)Math.Round(1120 * scale), (int)Math.Round(720 * scale));
        AppDiagnostics.Write($"DPI changed: {eventArgs.DeviceDpiOld} -> {eventArgs.DeviceDpiNew}.");
    }

    private void LayoutBrowser()
    {
        if (_browser is null) return;
        // WebView 必须覆盖完整客户区，圆角裁切只由 DWM 窗口负责，避免两套圆角产生空隙。
        _browser.Bounds = ClientRectangle;
    }

    protected override void WndProc(ref Message message)
    {
        if (_windowsCapabilityProvider?.HandleWindowMessage(message.Msg, message.WParam) == true)
        {
            message.Result = nint.Zero;
            return;
        }

        if (message.Msg == WmExitSizeMove) ApplyBlurBehind();
        if (message.Msg is WmNcPaint or WmNcActivate)
        {
            message.Result = (nint)1;
            return;
        }

        if (message.Msg == WmNcLButtonDown)
        {
            base.WndProc(ref message);
            BeginInvoke(new Action(ApplyBlurBehind));
            return;
        }

        if (message.Msg == WmSetCursor && message.WParam == Handle && ApplyResizeCursor(message.LParam))
        {
            message.Result = (nint)1;
            return;
        }

        if (message.Msg == WmNcCalcSize && message.WParam != nint.Zero)
        {
            message.Result = nint.Zero;
            return;
        }

        if (message.Msg == WmNcHitTest)
        {
            var screenPoint = new Point(
                (short)(message.LParam.ToInt64() & 0xFFFF),
                (short)((message.LParam.ToInt64() >> 16) & 0xFFFF));
            var hitTest = HitTestResizeBorder(PointToClient(screenPoint));
            if (hitTest != HtClient)
            {
                message.Result = hitTest;
                return;
            }
        }

        base.WndProc(ref message);
    }

    private bool ApplyResizeCursor(nint parameter)
    {
        var hitTest = (int)(parameter.ToInt64() & 0xFFFF);
        var cursorId = hitTest switch
        {
            HtLeft or HtRight => IdcSizewe,
            HtTop or HtBottom => IdcSizens,
            HtTopLeft or HtBottomRight => IdcSizenwse,
            HtTopRight or HtBottomLeft => IdcSizenesw,
            _ => 0,
        };
        if (cursorId == 0) return false;
        var cursor = LoadCursor(nint.Zero, cursorId);
        return cursor != nint.Zero && SetCursor(cursor);
    }

    private int HitTestResizeBorder(Point point)
    {
        if (WindowState == FormWindowState.Maximized) return HtClient;
        var grip = (int)Math.Round(BaseResizeGripSize * (DeviceDpi / 96d));
        var left = point.X <= grip;
        var right = point.X >= ClientSize.Width - grip;
        var top = point.Y <= grip;
        var bottom = point.Y >= ClientSize.Height - grip;
        return (top, bottom, left, right) switch
        {
            (true, false, true, false) => HtTopLeft,
            (true, false, false, true) => HtTopRight,
            (false, true, true, false) => HtBottomLeft,
            (false, true, false, true) => HtBottomRight,
            (true, false, false, false) => HtTop,
            (false, true, false, false) => HtBottom,
            (false, false, true, false) => HtLeft,
            (false, false, false, true) => HtRight,
            _ => HtClient,
        };
    }

    internal Task MinimizeShellAsync() => InvokeOnUiAsync(() => WindowState = FormWindowState.Minimized);

    internal Task<bool> ToggleMaximizeShellAsync() => InvokeOnUiAsync(() =>
    {
        if (WindowState == FormWindowState.Maximized)
        {
            WindowState = FormWindowState.Normal;
            ApplyRoundedWindow();
            return false;
        }

        MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;
        WindowState = FormWindowState.Maximized;
        return true;
    });

    internal Task StartWindowDragAsync() => InvokeOnUiAsync(() =>
    {
        ReleaseCapture();
        SendMessage(Handle, WmNcLButtonDown, HtCaption, 0);
    });

    internal Task StartWindowResizeAsync(string edge) => InvokeOnUiAsync(() =>
    {
        var hitTest = edge switch
        {
            "left" => HtLeft,
            "right" => HtRight,
            "top" => HtTop,
            "bottom" => HtBottom,
            "top-left" => HtTopLeft,
            "top-right" => HtTopRight,
            "bottom-left" => HtBottomLeft,
            "bottom-right" => HtBottomRight,
            _ => HtClient,
        };
        if (hitTest == HtClient) return;
        ReleaseCapture();
        SendMessage(Handle, WmNcLButtonDown, hitTest, 0);
    });

    internal Task MoveWindowByAsync(double deltaX, double deltaY) => InvokeOnUiAsync(() =>
    {
        Location = new Point(Location.X + (int)Math.Round(deltaX), Location.Y + (int)Math.Round(deltaY));
        ApplyBlurBehind();
    });

    internal Task CloseToTrayAsync() => InvokeOnUiAsync(HideToTray);

    internal Task SetShellThemeAsync(string theme) => InvokeOnUiAsync(() =>
    {
        BackColor = Color.Black;
        Opacity = 1d;
        ApplyDwmTheme(string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase));
        ApplyRoundedWindow();
    });

    internal Task InvokeOnUiAsync(Action action) => InvokeOnUiAsync(() =>
    {
        action();
        return true;
    });

    internal Task<T> InvokeOnUiAsync<T>(Func<T> action)
    {
        if (!InvokeRequired) return Task.FromResult(action());
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        BeginInvoke(new Action(() =>
        {
            try { completion.SetResult(action()); }
            catch (Exception error) { completion.SetException(error); }
        }));
        return completion.Task;
    }

    private void ApplyRoundedWindow()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        var preference = DwmwcpRound;
        _ = DwmSetWindowAttribute(Handle, DwmwaWindowCornerPreference, ref preference, sizeof(int));
    }

    private void ApplyDwmTheme(bool dark)
    {
        _isDarkTheme = dark;
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) return;
        try
        {
            var darkMode = dark ? 1 : 0;
            _ = DwmSetWindowAttribute(Handle, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
            var policy = DwmncrpDisabled;
            _ = DwmSetWindowAttribute(Handle, DwmwaNcRenderingPolicy, ref policy, sizeof(int));
            var margins = new DwmMargins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            _ = DwmExtendFrameIntoClientArea(Handle, ref margins);
            ApplyRoundedWindow();
        }
        catch (Exception error)
        {
            AppDiagnostics.Write($"DWM theme application failed: {error}");
        }
        ApplyBlurBehind();
    }

    private void ApplyBlurBehind()
    {
        if (!IsHandleCreated) return;
        var accent = new AccentPolicy
        {
            AccentState = AccentEnableAcrylicBlurBehind,
            GradientColor = _isDarkTheme ? unchecked((int)0x800F172A) : unchecked((int)0x80F1F5F9),
        };
        var size = Marshal.SizeOf(accent);
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, pointer, false);
            var data = new WindowCompositionAttributeData { Attribute = WcaAccentPolicy, Data = pointer, SizeOfData = size };
            _ = SetWindowCompositionAttribute(Handle, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, int message, int wParam, int lParam);

    [DllImport("user32.dll")]
    private static extern nint LoadCursor(nint instance, int cursorName);

    [DllImport("user32.dll")]
    private static extern bool SetCursor(nint cursor);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint window, ref DwmMargins margins);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(nint window, ref WindowCompositionAttributeData data);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData { public int Attribute; public nint Data; public int SizeOfData; }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy { public int AccentState; public int AccentFlags; public int GradientColor; public int AnimationId; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmMargins { public int Left; public int Right; public int Top; public int Bottom; }
}
