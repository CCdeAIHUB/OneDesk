using System.Security.Cryptography;
using System.Text;
using OneDesk.Desktop.Transport;
using Xunit;

namespace OneDesk.Desktop.Tests;

public sealed class ProtocolGenerationContractTests
{
    [Fact]
    public void EveryPlatformContractMatchesCanonicalSchemaHash()
    {
        // 场景：任意平台生成文件未随单一协议源更新时，构建必须直接失败而不能带着漂移继续运行。
        var workspace = FindWorkspaceRoot();
        var schemaPath = Path.Combine(workspace, "packages", "protocol", "schema", "onedesk.protocol.json");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(File.ReadAllText(schemaPath)))).ToLowerInvariant();
        var generatedFiles = new[]
        {
            Path.Combine(workspace, "packages", "protocol", "src", "generated", "protocol.ts"),
            Path.Combine(workspace, "apps", "desktop", "Protocol", "GeneratedProtocolContracts.cs"),
            Path.Combine(workspace, "apps", "mobile", "android", "app", "src", "main", "java", "cc", "onedesk", "mobile", "GeneratedProtocolContracts.kt"),
            Path.Combine(workspace, "apps", "mobile", "ios", "GeneratedProtocolContracts.swift"),
        };

        Assert.Equal(OneDeskProtocol.SchemaSha256, hash);
        foreach (var generatedFile in generatedFiles)
        {
            Assert.Contains($"schema-sha256: {hash}", File.ReadAllText(generatedFile), StringComparison.Ordinal);
        }
    }

    private static string FindWorkspaceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "pnpm-workspace.yaml")))
        {
            current = current.Parent;
        }
        return current?.FullName ?? throw new DirectoryNotFoundException("OneDeskWorkspaceRootNotFound");
    }
}
