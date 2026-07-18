using OneDesk.Desktop.Shell;
using Xunit;

namespace OneDesk.Desktop.Tests;

public sealed class DesktopLifetimeCoordinatorTests
{
    [Fact]
    public void CloseWindow_HidesToTrayWithoutExiting()
    {
        var hidden = 0;
        var shutdown = 0;
        var coordinator = new DesktopLifetimeCoordinator(
            () => Task.FromResult(true),
            () => hidden++,
            () => { },
            () =>
            {
                shutdown++;
                return Task.CompletedTask;
            });

        coordinator.CloseWindow();

        Assert.Equal(1, hidden);
        Assert.Equal(0, shutdown);
        Assert.False(coordinator.IsExitApproved);
    }

    [Fact]
    public async Task RequestExit_CancelledByUser_KeepsProcessRunning()
    {
        var shown = 0;
        var shutdown = 0;
        var coordinator = new DesktopLifetimeCoordinator(
            () => Task.FromResult(false),
            () => { },
            () => shown++,
            () =>
            {
                shutdown++;
                return Task.CompletedTask;
            });

        var exited = await coordinator.RequestExitAsync();

        Assert.False(exited);
        Assert.Equal(1, shown);
        Assert.Equal(0, shutdown);
        Assert.False(coordinator.IsExitApproved);
    }

    [Fact]
    public async Task RequestExit_Confirmed_ShutsDownExactlyOnce()
    {
        var shutdown = 0;
        var coordinator = new DesktopLifetimeCoordinator(
            () => Task.FromResult(true),
            () => { },
            () => { },
            () =>
            {
                shutdown++;
                return Task.CompletedTask;
            });

        var first = await coordinator.RequestExitAsync();
        var second = await coordinator.RequestExitAsync();

        Assert.True(first);
        Assert.True(second);
        Assert.Equal(1, shutdown);
        Assert.True(coordinator.IsExitApproved);
    }
}
