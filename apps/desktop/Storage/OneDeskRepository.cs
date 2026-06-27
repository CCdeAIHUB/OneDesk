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

    public Task<IReadOnlyList<ComponentDefinition>> ListComponentsAsync(CancellationToken cancellationToken = default)
    {
        return _store.LoadDirectoryAsync<ComponentDefinition>(_paths.Components, "onedesk.component.json", cancellationToken);
    }

    public Task SaveActionAsync(ActionDefinition action, CancellationToken cancellationToken = default)
    {
        return _store.SaveAsync(Path.Combine(_paths.Actions, $"{action.Id}.json"), action, cancellationToken);
    }

    public Task<IReadOnlyList<ActionDefinition>> ListActionsAsync(CancellationToken cancellationToken = default)
    {
        return _store.LoadDirectoryAsync<ActionDefinition>(_paths.Actions, "*.json", cancellationToken);
    }

    public Task SavePageAsync(PageDefinition page, CancellationToken cancellationToken = default)
    {
        return _store.SaveAsync(Path.Combine(_paths.Pages, page.Id, "onedesk.page.json"), page, cancellationToken);
    }

    public Task<IReadOnlyList<PageDefinition>> ListPagesAsync(CancellationToken cancellationToken = default)
    {
        return _store.LoadDirectoryAsync<PageDefinition>(_paths.Pages, "onedesk.page.json", cancellationToken);
    }

    public Task SaveSchemeAsync(SchemeDefinition scheme, CancellationToken cancellationToken = default)
    {
        return _store.SaveAsync(Path.Combine(_paths.Schemes, scheme.Id, "onedesk.scheme.json"), scheme, cancellationToken);
    }

    public Task<IReadOnlyList<SchemeDefinition>> ListSchemesAsync(CancellationToken cancellationToken = default)
    {
        return _store.LoadDirectoryAsync<SchemeDefinition>(_paths.Schemes, "onedesk.scheme.json", cancellationToken);
    }
}
