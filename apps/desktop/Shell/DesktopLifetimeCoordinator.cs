namespace OneDesk.Desktop.Shell;

/// <summary>
/// 统一协调“关闭到托盘”和“确认后退出”。平台事件只转发意图，不能自行结束进程。
/// </summary>
public sealed class DesktopLifetimeCoordinator
{
    private readonly Func<Task<bool>> _confirmExit;
    private readonly Action _hideWindow;
    private readonly Action _showWindow;
    private readonly Func<Task> _shutdown;
    private readonly SemaphoreSlim _exitLock = new(1, 1);

    public DesktopLifetimeCoordinator(
        Func<Task<bool>> confirmExit,
        Action hideWindow,
        Action showWindow,
        Func<Task> shutdown)
    {
        _confirmExit = confirmExit;
        _hideWindow = hideWindow;
        _showWindow = showWindow;
        _shutdown = shutdown;
    }

    public bool IsExitApproved { get; private set; }

    public void CloseWindow()
    {
        if (!IsExitApproved)
        {
            _hideWindow();
        }
    }

    public async Task<bool> RequestExitAsync()
    {
        await _exitLock.WaitAsync();
        try
        {
            if (IsExitApproved)
            {
                return true;
            }

            _showWindow();
            if (!await _confirmExit())
            {
                return false;
            }

            // 先锁定退出状态再请求关闭，避免 Closing 事件把最终退出再次改成隐藏。
            IsExitApproved = true;
            await _shutdown();
            return true;
        }
        finally
        {
            _exitLock.Release();
        }
    }
}
