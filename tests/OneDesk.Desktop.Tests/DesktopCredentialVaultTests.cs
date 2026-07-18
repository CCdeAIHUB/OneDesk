using OneDesk.Desktop.Services;
using OneDesk.Desktop.Storage;
using Xunit;

namespace OneDesk.Desktop.Tests;

public sealed class DesktopCredentialVaultTests
{
    [Fact]
    public async Task CredentialIsEncryptedAndScopedByCallerIdentity()
    {
        // 场景：相同键名在不同插件间必须隔离，磁盘文件不得包含明文凭据。
        var root = Path.Combine(Path.GetTempPath(), $"onedesk-credential-{Guid.NewGuid():N}");
        try
        {
            var vault = new DesktopCredentialVault(new OneDeskDataPaths(root));
            await vault.WriteAsync("plugin:first", "token", "first-secret", TestContext.Current.CancellationToken);
            await vault.WriteAsync("plugin:second", "token", "second-secret", TestContext.Current.CancellationToken);

            Assert.Equal("first-secret", await vault.ReadAsync("plugin:first", "token", TestContext.Current.CancellationToken));
            Assert.Equal("second-secret", await vault.ReadAsync("plugin:second", "token", TestContext.Current.CancellationToken));
            var storedBytes = Directory.EnumerateFiles(Path.Combine(root, "credentials"), "*.credential")
                .SelectMany(File.ReadAllBytes)
                .ToArray();
            Assert.True(storedBytes.AsSpan().IndexOf("first-secret"u8) < 0);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
