using OneDesk.Desktop.Services;
using Xunit;

namespace OneDesk.Desktop.Tests;

public sealed class FileInstallTransactionTests
{
    [Fact]
    public void FailedInstallRestoresEveryReplacedDestination()
    {
        // 场景：方案依赖安装到一半失败时，已经替换的旧组件必须完整恢复。
        var root = Path.Combine(Path.GetTempPath(), $"onedesk-transaction-{Guid.NewGuid():N}");
        var staging = Path.Combine(root, "staging");
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(Path.Combine(staging, "first"));
        Directory.CreateDirectory(Path.Combine(target, "first"));
        File.WriteAllText(Path.Combine(staging, "first", "value.txt"), "new");
        File.WriteAllText(Path.Combine(target, "first", "value.txt"), "old");

        try
        {
            var operations = new[]
            {
                FileInstallOperation.Directory(Path.Combine(staging, "first"), Path.Combine(target, "first")),
                FileInstallOperation.Directory(Path.Combine(staging, "missing"), Path.Combine(target, "second")),
            };

            Assert.ThrowsAny<Exception>(() => FileInstallTransaction.Commit(operations, Path.Combine(root, "backup")));
            Assert.Equal("old", File.ReadAllText(Path.Combine(target, "first", "value.txt")));
            Assert.False(Directory.Exists(Path.Combine(target, "second")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SuccessfulInstallCommitsDirectoriesAndFiles()
    {
        // 场景：页面包包含组件和动作时，目录与单文件依赖必须在同一事务中提交。
        var root = Path.Combine(Path.GetTempPath(), $"onedesk-transaction-{Guid.NewGuid():N}");
        var staging = Path.Combine(root, "staging");
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(Path.Combine(staging, "component"));
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "component", "manifest.json"), "component");
        File.WriteAllText(Path.Combine(staging, "action.json"), "action");

        try
        {
            FileInstallTransaction.Commit(
            [
                FileInstallOperation.Directory(Path.Combine(staging, "component"), Path.Combine(target, "component")),
                FileInstallOperation.File(Path.Combine(staging, "action.json"), Path.Combine(target, "action.json")),
            ],
            Path.Combine(root, "backup"));

            Assert.Equal("component", File.ReadAllText(Path.Combine(target, "component", "manifest.json")));
            Assert.Equal("action", File.ReadAllText(Path.Combine(target, "action.json")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DeferredInstallRollsBackWhenExternalValidationFails()
    {
        // 场景：插件文件已切换，但宿主握手失败时，释放未提交事务必须恢复旧插件。
        var root = Path.Combine(Path.GetTempPath(), $"onedesk-deferred-transaction-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "source");
        var destination = Path.Combine(root, "destination");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(source, "version.txt"), "new");
        File.WriteAllText(Path.Combine(destination, "version.txt"), "old");

        try
        {
            using (FileInstallTransaction.Begin(
                       [FileInstallOperation.Directory(source, destination)],
                       Path.Combine(root, "backup")))
            {
                Assert.Equal("new", File.ReadAllText(Path.Combine(destination, "version.txt")));
            }

            Assert.Equal("old", File.ReadAllText(Path.Combine(destination, "version.txt")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
