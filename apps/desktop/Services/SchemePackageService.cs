using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.IO;
using OneDesk.Desktop.Domain;
using OneDesk.Desktop.Storage;

namespace OneDesk.Desktop.Services;

public sealed class SchemePackageService
{
    private readonly OneDeskDataPaths _paths;
    private const int MaxPackageEntries = 4096;
    private const long MaxPackageUncompressedBytes = 512L * 1024 * 1024;

    public SchemePackageService(OneDeskDataPaths paths)
    {
        _paths = paths;
        _paths.EnsureCreated();
    }

    public async Task<string> ComputeHashAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public void ExportComponent(string componentDirectory, string destinationPackage)
    {
        EnsureManifest(componentDirectory, "onedesk.component.json");
        CreatePackage(componentDirectory, destinationPackage);
    }

    public void ExportPage(string pageDirectory, string destinationPackage)
    {
        EnsureManifest(pageDirectory, "onedesk.page.json");
        CreatePackage(pageDirectory, destinationPackage);
    }

    public void ExportScheme(string sourceDirectory, string destinationPackage)
    {
        EnsureManifest(sourceDirectory, "onedesk.scheme.json");
        CreatePackage(sourceDirectory, destinationPackage);
    }

    public async Task<PackageExportResult> ExportComponentByIdAsync(string componentId, CancellationToken cancellationToken = default)
    {
        var source = Path.Combine(_paths.Components, componentId);
        EnsureManifest(source, "onedesk.component.json");
        var destination = ExportPath(componentId, "component");
        var temp = CreateTempExportRoot(componentId);
        try
        {
            CopyDirectory(source, temp);
            var component = await ReadManifestAsync<ComponentDefinition>(Path.Combine(temp, "onedesk.component.json"), cancellationToken);
            CopyActions(component?.ActionIds ?? [], Path.Combine(temp, "actions"));
            CreatePackage(temp, destination);
            return await ExportResultAsync(destination, "component", cancellationToken);
        }
        finally
        {
            DeleteDirectory(temp);
        }
    }

    public async Task<PackageExportResult> ExportPageByIdAsync(string pageId, CancellationToken cancellationToken = default)
    {
        var source = Path.Combine(_paths.Pages, pageId);
        EnsureManifest(source, "onedesk.page.json");
        var destination = ExportPath(pageId, "page");
        var temp = CreateTempExportRoot(pageId);
        try
        {
            CopyDirectory(source, temp);
            var page = await ReadManifestAsync<PageDefinition>(Path.Combine(temp, "onedesk.page.json"), cancellationToken);
            var componentIds = (page?.Cells ?? [])
                .Select(cell => cell.ComponentId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var actionIds = await CopyComponentsAsync(componentIds, Path.Combine(temp, "components"), cancellationToken);
            CopyActions(actionIds, Path.Combine(temp, "actions"));
            CreatePackage(temp, destination);
            return await ExportResultAsync(destination, "page", cancellationToken);
        }
        finally
        {
            DeleteDirectory(temp);
        }
    }

    public async Task<PackageExportResult> ExportSchemeByIdAsync(string schemeId, CancellationToken cancellationToken = default)
    {
        var source = Path.Combine(_paths.Schemes, schemeId);
        EnsureManifest(source, "onedesk.scheme.json");
        var destination = ExportPath(schemeId, "scheme");
        var temp = CreateTempExportRoot(schemeId);
        try
        {
            CopyDirectory(source, temp);
            var scheme = await ReadManifestAsync<SchemeDefinition>(Path.Combine(temp, "onedesk.scheme.json"), cancellationToken);
            var pageIds = (scheme?.PageIds ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var componentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pageId in pageIds)
            {
                var pageSource = Path.Combine(_paths.Pages, pageId);
                if (!File.Exists(Path.Combine(pageSource, "onedesk.page.json")))
                {
                    continue;
                }

                var pageDestination = Path.Combine(temp, "pages", pageId);
                CopyDirectory(pageSource, pageDestination);
                var page = await ReadManifestAsync<PageDefinition>(Path.Combine(pageSource, "onedesk.page.json"), cancellationToken);
                foreach (var componentId in (page?.Cells ?? []).Select(cell => cell.ComponentId).Where(id => !string.IsNullOrWhiteSpace(id)))
                {
                    componentIds.Add(componentId!);
                }
            }

            var actionIds = await CopyComponentsAsync(componentIds, Path.Combine(temp, "components"), cancellationToken);
            CopyActions(actionIds, Path.Combine(temp, "actions"));
            await WriteDependencyReportAsync(temp, scheme?.PluginDependencies ?? [], cancellationToken);
            CreatePackage(temp, destination);
            return await ExportResultAsync(destination, "scheme", cancellationToken);
        }
        finally
        {
            DeleteDirectory(temp);
        }
    }

    public PackageImportResult ImportComponent(string packagePath)
    {
        var destination = Path.Combine(_paths.Components, Path.GetFileNameWithoutExtension(packagePath));
        ImportPackage(packagePath, destination, "onedesk.component.json");
        return new PackageImportResult(true, destination, []);
    }

    public PackageImportResult ImportPage(string packagePath)
    {
        var destination = Path.Combine(_paths.Pages, Path.GetFileNameWithoutExtension(packagePath));
        ImportPackage(packagePath, destination, "onedesk.page.json");
        return new PackageImportResult(true, destination, []);
    }

    public PackageImportResult ImportScheme(string packagePath, IReadOnlySet<string> installedPluginIds)
    {
        var destination = Path.Combine(_paths.Schemes, Path.GetFileNameWithoutExtension(packagePath));
        ImportPackage(packagePath, destination, "onedesk.scheme.json");
        var manifestPath = Path.Combine(destination, "onedesk.scheme.json");
        var missing = ReadSchemePluginDependencies(manifestPath)
            .Where(dependency => !installedPluginIds.Contains(dependency.Id))
            .Select(dependency => dependency.Id)
            .ToArray();

        return new PackageImportResult(missing.Length == 0, destination, missing);
    }

    private static void CreatePackage(string sourceDirectory, string destinationPackage)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPackage) ?? ".");
        if (File.Exists(destinationPackage))
        {
            File.Delete(destinationPackage);
        }

        ZipFile.CreateFromDirectory(sourceDirectory, destinationPackage, CompressionLevel.Optimal, includeBaseDirectory: false);
    }

    private void ImportPackage(string packagePath, string destinationDirectory, string requiredManifest)
    {
        var temp = $"{destinationDirectory}.tmp-{Guid.NewGuid():N}";
        SafeExtractPackage(packagePath, temp);
        EnsureManifest(temp, requiredManifest);

        if (Directory.Exists(destinationDirectory))
        {
            Directory.Delete(destinationDirectory, recursive: true);
        }

        Directory.Move(temp, destinationDirectory);
    }

    private static void SafeExtractPackage(string packagePath, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        using var archive = ZipFile.OpenRead(packagePath);
        if (archive.Entries.Count > MaxPackageEntries)
        {
            throw new InvalidDataException("Package contains too many files.");
        }

        var totalSize = archive.Entries.Sum(entry => entry.Length);
        if (totalSize > MaxPackageUncompressedBytes)
        {
            throw new InvalidDataException("Package is too large after extraction.");
        }

        var destinationRoot = Path.GetFullPath(destinationDirectory);
        foreach (var entry in archive.Entries)
        {
            var targetPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            if (!targetPath.StartsWith(destinationRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(targetPath, destinationRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Package contains a path outside the extraction directory.");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? destinationRoot);
            entry.ExtractToFile(targetPath, overwrite: true);
        }
    }

    private static void EnsureManifest(string directory, string manifestName)
    {
        var path = Path.Combine(directory, manifestName);
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"Package manifest is missing: {manifestName}");
        }
    }

    private static IReadOnlyList<DependencyDefinition> ReadSchemePluginDependencies(string manifestPath)
    {
        using var stream = File.OpenRead(manifestPath);
        var scheme = JsonSerializer.Deserialize<SchemeDefinition>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return scheme?.PluginDependencies ?? [];
    }

    private string ExportPath(string id, string kind)
    {
        var safeId = string.Concat(id.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '-' : ch));
        return Path.Combine(_paths.Exports, $"{safeId}.{kind}.zip");
    }

    private string CreateTempExportRoot(string id)
    {
        var safeId = string.Concat(id.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '-' : ch));
        var root = Path.Combine(_paths.Temp, $"export-{safeId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private async Task<IReadOnlyList<string>> CopyComponentsAsync(IEnumerable<string> componentIds, string destinationRoot, CancellationToken cancellationToken)
    {
        var actionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var componentId in componentIds)
        {
            var source = Path.Combine(_paths.Components, componentId);
            if (!File.Exists(Path.Combine(source, "onedesk.component.json")))
            {
                continue;
            }

            var destination = Path.Combine(destinationRoot, componentId);
            CopyDirectory(source, destination);
            var component = await ReadManifestAsync<ComponentDefinition>(Path.Combine(source, "onedesk.component.json"), cancellationToken);
            foreach (var actionId in component?.ActionIds ?? [])
            {
                actionIds.Add(actionId);
            }
        }

        return actionIds.ToArray();
    }

    private void CopyActions(IEnumerable<string> actionIds, string destinationRoot)
    {
        foreach (var actionId in actionIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var source = Path.Combine(_paths.Actions, $"{actionId}.json");
            if (!File.Exists(source))
            {
                continue;
            }

            Directory.CreateDirectory(destinationRoot);
            File.Copy(source, Path.Combine(destinationRoot, $"{actionId}.json"), overwrite: true);
        }
    }

    private async Task<PackageExportResult> ExportResultAsync(string destination, string kind, CancellationToken cancellationToken)
    {
        return new PackageExportResult(true, kind, destination, await ComputeHashAsync(destination, cancellationToken), new FileInfo(destination).Length);
    }

    private static async Task<T?> ReadManifestAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
    }

    private static async Task WriteDependencyReportAsync(string root, IReadOnlyList<DependencyDefinition> dependencies, CancellationToken cancellationToken)
    {
        var reportPath = Path.Combine(root, "onedesk.dependencies.json");
        await using var stream = File.Create(reportPath);
        await JsonSerializer.SerializeAsync(stream, new { pluginDependencies = dependencies }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }, cancellationToken);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? destination);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

public sealed record PackageImportResult(bool Ready, string DestinationDirectory, IReadOnlyList<string> MissingPluginIds);

public sealed record PackageExportResult(bool Ready, string Kind, string PackagePath, string Sha256, long SizeBytes);
