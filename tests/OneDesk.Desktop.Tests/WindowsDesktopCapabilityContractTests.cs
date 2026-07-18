using OneDesk.Desktop.Services;
using Xunit;

namespace OneDesk.Desktop.Tests;

public sealed class WindowsDesktopCapabilityContractTests
{
    [Fact]
    public void EveryWindowsSupportedCapabilityHasAnExecutableHandlerContract()
    {
        // 场景：能力目录标记 Windows 支持时，必须存在核心、跨平台、方案或 Win32 处理器，不能落入默认 unsupported。
        var executable = DesktopCapabilityContracts.BuiltIn
            .Concat(DesktopCapabilityContracts.Portable)
            .Concat(DesktopCapabilityContracts.Scheme)
            .Concat(DesktopCapabilityContracts.Windows)
            .ToHashSet(StringComparer.Ordinal);
        var declared = new CapabilityDirectoryService().All()
            .Where(capability => capability.Desktop.Supported)
            .Select(capability => capability.Id)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Empty(declared.Except(executable));
        Assert.Empty(executable.Except(declared));
    }
}
