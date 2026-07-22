using OneDesk.Desktop.Storage;
using Xunit;

namespace OneDesk.Desktop.Tests;

public sealed class JsonFileStoreConcurrencyTests
{
    [Fact]
    public async Task ConcurrentReadersAndWritersNeverObserveLockedOrPartialJson()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), $"onedesk-json-store-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "active-devices", "mobile-test.json");
        var store = new JsonFileStore();

        try
        {
            await store.SaveAsync(path, new StoredState(0, "initial"), cancellationToken);
            var operations = Enumerable.Range(1, 120).Select(async index =>
            {
                if (index % 3 == 0)
                {
                    await store.SaveAsync(path, new StoredState(index, $"scheme-{index}"), cancellationToken);
                    return;
                }

                var state = await store.LoadAsync<StoredState>(path, cancellationToken);
                Assert.NotNull(state);
                Assert.False(string.IsNullOrWhiteSpace(state.SchemeId));
            });

            await Task.WhenAll(operations);
            Assert.NotNull(await store.LoadAsync<StoredState>(path, cancellationToken));
            Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed record StoredState(int Revision, string SchemeId);
}
