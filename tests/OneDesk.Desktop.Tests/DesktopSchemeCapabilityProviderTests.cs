using System.Text.Json;
using OneDesk.Desktop.Domain;
using OneDesk.Desktop.Services;
using OneDesk.Desktop.Storage;
using Xunit;

namespace OneDesk.Desktop.Tests;

public sealed class DesktopSchemeCapabilityProviderTests
{
    [Fact]
    public async Task PreviousPageWrapsFromFirstPageToLastPage()
    {
        // 场景：方案第一页执行“上一页”时必须循环到最后一页。
        var root = Path.Combine(Path.GetTempPath(), $"onedesk-scheme-runtime-{Guid.NewGuid():N}");
        try
        {
            var paths = new OneDeskDataPaths(root);
            var store = new JsonFileStore();
            var repository = new OneDeskRepository(paths, store);
            var scheme = new SchemeDefinition
            {
                Id = "scheme-one",
                Name = "测试方案",
                Version = "1.0.0",
                PageIds = ["page-one", "page-two"],
                GlobalPrevious = Switch("previous"),
                GlobalNext = Switch("next"),
                Edges = [],
                PluginDependencies = [],
            };
            await repository.SaveSchemeAsync(scheme, TestContext.Current.CancellationToken);
            await repository.ApplySchemeAsync(scheme.Id, TestContext.Current.CancellationToken);
            var provider = new DesktopSchemeCapabilityProvider(repository, store, paths);
            var request = new JsApiRequest(
                "request-one",
                "desktop",
                new TrustedSource(null, null, null, null, "system"),
                "scheme.page.switch",
                JsonSerializer.SerializeToElement(new { direction = "previous" }));

            var result = await provider.ExecuteAsync(request, TestContext.Current.CancellationToken);

            Assert.True(result.Ok);
            Assert.Equal("page-two", Assert.IsType<DesktopSchemeRuntimeState>(result.Payload).PageId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static PageSwitchDefinition Switch(string id) => new()
    {
        Trigger = new TriggerDefinition { Id = id, Category = "touch.standard", DisplayName = id, FingerCount = 1 },
        Animation = "fade",
    };
}
