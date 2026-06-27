using System.IO;
using OneDesk.Desktop.Domain;

namespace OneDesk.Desktop.Storage;

public sealed class OneDeskRepository
{
    private readonly OneDeskDataPaths _paths;
    private readonly JsonFileStore _store;

    public OneDeskRepository(OneDeskDataPaths paths, JsonFileStore store)
    {
        _paths = paths;
        _store = store;
        _paths.EnsureCreated();
    }

    public Task SaveComponentAsync(ComponentDefinition component, CancellationToken cancellationToken = default)
    {
        return _store.SaveAsync(Path.Combine(_paths.Components, component.Id, "onedesk.component.json"), component, cancellationToken);
    }

    public Task<ComponentDefinition?> GetComponentAsync(string componentId, CancellationToken cancellationToken = default)
    {
        return _store.LoadAsync<ComponentDefinition>(Path.Combine(_paths.Components, componentId, "onedesk.component.json"), cancellationToken);
    }

    public Task<IReadOnlyList<ComponentDefinition>> ListComponentsAsync(CancellationToken cancellationToken = default)
    {
        return _store.LoadDirectoryAsync<ComponentDefinition>(_paths.Components, "onedesk.component.json", cancellationToken);
    }

    public void DeleteComponent(string componentId)
    {
        DeleteDirectory(Path.Combine(_paths.Components, componentId));
    }

    public Task SaveActionAsync(ActionDefinition action, CancellationToken cancellationToken = default)
    {
        return _store.SaveAsync(Path.Combine(_paths.Actions, $"{action.Id}.json"), action, cancellationToken);
    }

    public Task<IReadOnlyList<ActionDefinition>> ListActionsAsync(CancellationToken cancellationToken = default)
    {
        return _store.LoadDirectoryAsync<ActionDefinition>(_paths.Actions, "*.json", cancellationToken);
    }

    public void DeleteAction(string actionId)
    {
        var path = Path.Combine(_paths.Actions, $"{actionId}.json");
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public Task SavePageAsync(PageDefinition page, CancellationToken cancellationToken = default)
    {
        return _store.SaveAsync(Path.Combine(_paths.Pages, page.Id, "onedesk.page.json"), page, cancellationToken);
    }

    public Task<PageDefinition?> GetPageAsync(string pageId, CancellationToken cancellationToken = default)
    {
        return _store.LoadAsync<PageDefinition>(Path.Combine(_paths.Pages, pageId, "onedesk.page.json"), cancellationToken);
    }

    public Task<IReadOnlyList<PageDefinition>> ListPagesAsync(CancellationToken cancellationToken = default)
    {
        return _store.LoadDirectoryAsync<PageDefinition>(_paths.Pages, "onedesk.page.json", cancellationToken);
    }

    public void DeletePage(string pageId)
    {
        DeleteDirectory(Path.Combine(_paths.Pages, pageId));
    }

    public Task SaveSchemeAsync(SchemeDefinition scheme, CancellationToken cancellationToken = default)
    {
        return _store.SaveAsync(Path.Combine(_paths.Schemes, scheme.Id, "onedesk.scheme.json"), scheme, cancellationToken);
    }

    public Task<SchemeDefinition?> GetSchemeAsync(string schemeId, CancellationToken cancellationToken = default)
    {
        return _store.LoadAsync<SchemeDefinition>(Path.Combine(_paths.Schemes, schemeId, "onedesk.scheme.json"), cancellationToken);
    }

    public Task<IReadOnlyList<SchemeDefinition>> ListSchemesAsync(CancellationToken cancellationToken = default)
    {
        return _store.LoadDirectoryAsync<SchemeDefinition>(_paths.Schemes, "onedesk.scheme.json", cancellationToken);
    }

    public Task ApplySchemeAsync(string schemeId, CancellationToken cancellationToken = default)
    {
        return _store.SaveAsync(Path.Combine(_paths.Schemes, "active-scheme.json"), new ActiveSchemeState(schemeId, DateTimeOffset.UtcNow), cancellationToken);
    }

    public Task<ActiveSchemeState?> GetActiveSchemeAsync(CancellationToken cancellationToken = default)
    {
        return _store.LoadAsync<ActiveSchemeState>(Path.Combine(_paths.Schemes, "active-scheme.json"), cancellationToken);
    }

    public void DeleteScheme(string schemeId)
    {
        DeleteDirectory(Path.Combine(_paths.Schemes, schemeId));
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

public sealed record ActiveSchemeState(string SchemeId, DateTimeOffset AppliedAt);
