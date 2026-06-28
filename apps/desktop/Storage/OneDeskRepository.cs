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

    public async Task SaveComponentFilesAsync(string componentId, IReadOnlyDictionary<string, string> files, CancellationToken cancellationToken = default)
    {
        var root = ComponentRoot(componentId);
        Directory.CreateDirectory(root);
        foreach (var (relativePath, content) in files)
        {
            var target = ResolveComponentFile(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? root);
            await File.WriteAllTextAsync(target, content, cancellationToken);
        }
    }

    public async Task<IReadOnlyDictionary<string, string>> ReadComponentFilesAsync(string componentId, CancellationToken cancellationToken = default)
    {
        var root = ComponentRoot(componentId);
        if (!Directory.Exists(root))
        {
            return new Dictionary<string, string>();
        }

        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            files[relative] = await File.ReadAllTextAsync(file, cancellationToken);
        }

        return files;
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

    public async Task<MediaResourceDefinition> AddMediaResourceAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Media file does not exist.", sourcePath);
        }

        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        var kind = extension is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp" ? "image" :
            extension is ".mp4" or ".webm" or ".mov" or ".mkv" or ".avi" ? "video" : "file";
        if (kind == "file")
        {
            throw new InvalidDataException("Only image and video resources are supported.");
        }

        var id = $"resource-{Guid.NewGuid():N}";
        var root = Path.Combine(_paths.Resources, id);
        Directory.CreateDirectory(root);
        var fileName = $"{id}{extension}";
        var target = Path.Combine(root, fileName);
        File.Copy(sourcePath, target, overwrite: true);
        var info = new FileInfo(target);
        var resource = new MediaResourceDefinition
        {
            Id = id,
            Name = Path.GetFileNameWithoutExtension(sourcePath),
            Kind = kind,
            FileName = fileName,
            Extension = extension,
            SizeBytes = info.Length,
            FileUri = new Uri(target).AbsoluteUri,
        };

        await _store.SaveAsync(Path.Combine(root, "onedesk.resource.json"), resource, cancellationToken);
        return resource;
    }

    public Task<IReadOnlyList<MediaResourceDefinition>> ListMediaResourcesAsync(CancellationToken cancellationToken = default)
    {
        return _store.LoadDirectoryAsync<MediaResourceDefinition>(_paths.Resources, "onedesk.resource.json", cancellationToken);
    }

    public void DeleteMediaResource(string resourceId)
    {
        DeleteDirectory(Path.Combine(_paths.Resources, SafeFileName(resourceId, "resource id")));
    }

    public async Task<MediaResourceCopyResult> CopyMediaResourceToComponentAsync(string resourceId, string componentId, CancellationToken cancellationToken = default)
    {
        var resource = await GetMediaResourceAsync(resourceId, cancellationToken) ?? throw new InvalidDataException("Resource does not exist.");
        return CopyMediaResource(resource, Path.Combine(ComponentRoot(componentId), "assets"));
    }

    public async Task<MediaResourceCopyResult> CopyMediaResourceToPageAsync(string resourceId, string pageId, CancellationToken cancellationToken = default)
    {
        var resource = await GetMediaResourceAsync(resourceId, cancellationToken) ?? throw new InvalidDataException("Resource does not exist.");
        return CopyMediaResource(resource, Path.Combine(PageRoot(pageId), "assets"));
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
        return ApplySchemeAsync(schemeId, null, cancellationToken);
    }

    public Task ApplySchemeAsync(string schemeId, string? deviceId, CancellationToken cancellationToken = default)
    {
        return _store.SaveAsync(ActiveSchemePath(deviceId), new ActiveSchemeState(schemeId, DateTimeOffset.UtcNow, string.IsNullOrWhiteSpace(deviceId) ? null : deviceId), cancellationToken);
    }

    public Task<ActiveSchemeState?> GetActiveSchemeAsync(CancellationToken cancellationToken = default)
    {
        return GetActiveSchemeAsync(null, cancellationToken);
    }

    public async Task<ActiveSchemeState?> GetActiveSchemeAsync(string? deviceId, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            var deviceActive = await _store.LoadAsync<ActiveSchemeState>(ActiveSchemePath(deviceId), cancellationToken);
            if (deviceActive is not null)
            {
                return deviceActive;
            }
        }

        return await _store.LoadAsync<ActiveSchemeState>(ActiveSchemePath(null), cancellationToken);
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

    private string ComponentRoot(string componentId)
    {
        return Path.GetFullPath(Path.Combine(_paths.Components, SafeFileName(componentId, "component id")));
    }

    private string PageRoot(string pageId)
    {
        return Path.GetFullPath(Path.Combine(_paths.Pages, SafeFileName(pageId, "page id")));
    }

    private async Task<MediaResourceDefinition?> GetMediaResourceAsync(string resourceId, CancellationToken cancellationToken)
    {
        var id = SafeFileName(resourceId, "resource id");
        return await _store.LoadAsync<MediaResourceDefinition>(Path.Combine(_paths.Resources, id, "onedesk.resource.json"), cancellationToken);
    }

    private MediaResourceCopyResult CopyMediaResource(MediaResourceDefinition resource, string targetRoot)
    {
        Directory.CreateDirectory(targetRoot);
        var source = Path.Combine(_paths.Resources, SafeFileName(resource.Id, "resource id"), resource.FileName);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("Resource file does not exist.", source);
        }

        var fileName = $"{resource.Id}{resource.Extension}";
        var target = Path.Combine(targetRoot, fileName);
        File.Copy(source, target, overwrite: true);
        var relative = Path.GetRelativePath(Path.GetDirectoryName(targetRoot) ?? targetRoot, target).Replace('\\', '/');
        return new MediaResourceCopyResult
        {
            ResourceId = resource.Id,
            RelativePath = relative,
            FileUri = new Uri(target).AbsoluteUri,
        };
    }

    private static string SafeFileName(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException($"Invalid {label}.");
        }

        return value;
    }

    private string ActiveSchemePath(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return Path.Combine(_paths.Schemes, "active-scheme.json");
        }

        var root = Path.Combine(_paths.Schemes, "active-devices");
        Directory.CreateDirectory(root);
        var safeDeviceId = string.Join("_", deviceId.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return Path.Combine(root, $"{safeDeviceId}.json");
    }

    private static string ResolveComponentFile(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("Component file path must be relative.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Component file path cannot escape component directory.");
        }

        return fullPath;
    }
}

public sealed record ActiveSchemeState(string SchemeId, DateTimeOffset AppliedAt, string? DeviceId = null);
