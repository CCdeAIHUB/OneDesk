using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OneDesk.Desktop.Services;
using Xunit;

namespace OneDesk.Desktop.Tests;

public sealed class CodeComponentArtifactTests
{
    [Fact]
    public void ValidArtifactIsAcceptedAndTamperedArtifactIsRejected()
    {
        // 场景：代码组件进入方案前必须校验构建清单和哈希，源码或产物变化后不能继续使用旧清单。
        const string code = "globalThis.componentLoaded=true;";
        const string style = ".component{color:white}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{code}\n/* onedesk-style */\n{style}"))).ToLowerInvariant();
        var files = new Dictionary<string, string>
        {
            ["dist/onedesk-component.js"] = code,
            ["dist/onedesk-component.css"] = style,
            ["dist/onedesk.runtime.json"] = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                entryFile = "src/Component.vue",
                codeFile = "dist/onedesk-component.js",
                styleFile = "dist/onedesk-component.css",
                sha256 = hash
            })
        };

        Assert.True(CodeComponentArtifactValidator.TryRead(files, out var artifact, out var error), error);
        Assert.Equal(code, artifact!.Code);

        files["dist/onedesk-component.js"] = code + "// tampered";
        Assert.False(CodeComponentArtifactValidator.TryRead(files, out _, out var tamperedError));
        Assert.Equal("CodeComponentArtifactHashMismatch", tamperedError);
    }
}
