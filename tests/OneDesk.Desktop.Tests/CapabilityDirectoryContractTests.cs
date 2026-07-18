using System.Text.Json;
using OneDesk.Desktop.Services;
using OneDesk.Desktop.Storage;
using Xunit;

namespace OneDesk.Desktop.Tests;

public sealed class CapabilityDirectoryContractTests
{
    [Fact]
    public void DesktopDirectoryExactlyMatchesCanonicalProtocolCatalog()
    {
        // 场景：权限、路由和各平台处理器必须共享同一组能力 ID，禁止端侧各自命名。
        var contractPath = Path.Combine(AppContext.BaseDirectory, "contracts", "capabilities.json");
        using var document = JsonDocument.Parse(File.ReadAllText(contractPath));
        var canonicalIds = document.RootElement
            .GetProperty("capabilities")
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetString())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToHashSet(StringComparer.Ordinal);

        var desktopIds = new CapabilityDirectoryService()
            .All()
            .Select(capability => capability.Id)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(canonicalIds.Order(StringComparer.Ordinal), desktopIds.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void LegacyCapabilityAliasesAreMigratedToCanonicalIds()
    {
        // 场景：升级前保存的旧权限 ID 必须继续生效，并在下一次写入时迁移为协议标准 ID。
        var root = Path.Combine(Path.GetTempPath(), $"onedesk-permission-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(
                Path.Combine(root, "permission-grants.json"),
                """
                {
                  "grants": [
                    {
                      "sourceKey": "component:legacy-component",
                      "capabilities": ["file.readPrivate"]
                    }
                  ]
                }
                """);

            var service = new PermissionService(new CapabilityDirectoryService(), new OneDeskDataPaths(root));
            var source = new TrustedSource(null, null, "legacy-component", null, "component");

            Assert.True(service.IsGranted(source, "file.private.read"));
            Assert.Equal(["file.private.read"], service.ListGrants().Single().Capabilities);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
