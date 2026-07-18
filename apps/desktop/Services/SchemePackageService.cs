using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
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
            var componentCopy = await CopyComponentsAsync(componentIds, Path.Combine(temp, "components"), cancellationToken);
            CopyActions(componentCopy.ActionIds, Path.Combine(temp, "actions"));
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

            var componentCopy = await CopyComponentsAsync(componentIds, Path.Combine(temp, "components"), cancellationToken);
            CopyActions(componentCopy.ActionIds, Path.Combine(temp, "actions"));
            var pluginDependencies = (scheme?.PluginDependencies ?? [])
                .Concat(componentCopy.PluginDependencies)
                .Where(dependency => string.Equals(dependency.Kind, "plugin", StringComparison.OrdinalIgnoreCase))
                .DistinctBy(dependency => dependency.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            CopyPlugins(pluginDependencies, Path.Combine(temp, "plugins"));
            await WriteDependencyReportAsync(temp, pluginDependencies, cancellationToken);
            CreatePackage(temp, destination);
            return await ExportResultAsync(destination, "scheme", cancellationToken);
        }
        finally
        {
            DeleteDirectory(temp);
        }
    }

    public PackageImportResult ImportComponent(
        string packagePath,
        IReadOnlyDictionary<string, string>? installedPluginVersions = null,
        IReadOnlyDictionary<string, PluginVersionChoice>? pluginChoices = null)
    {
        using var session = BeginImportComponent(packagePath, installedPluginVersions, pluginChoices);
        if (session.Result.Ready) session.Complete();
        return session.Result;
    }

    public WorkspacePackageImportSession BeginImportComponent(
        string packagePath,
        IReadOnlyDictionary<string, string>? installedPluginVersions = null,
        IReadOnlyDictionary<string, PluginVersionChoice>? pluginChoices = null) =>
        BeginImportWorkspaceBundle(packagePath, WorkspacePackageKind.Component, installedPluginVersions, pluginChoices);

    public PackageImportResult ImportPage(
        string packagePath,
        IReadOnlyDictionary<string, string>? installedPluginVersions = null,
        IReadOnlyDictionary<string, PluginVersionChoice>? pluginChoices = null)
    {
        using var session = BeginImportPage(packagePath, installedPluginVersions, pluginChoices);
        if (session.Result.Ready) session.Complete();
        return session.Result;
    }

    public WorkspacePackageImportSession BeginImportPage(
        string packagePath,
        IReadOnlyDictionary<string, string>? installedPluginVersions = null,
        IReadOnlyDictionary<string, PluginVersionChoice>? pluginChoices = null) =>
        BeginImportWorkspaceBundle(packagePath, WorkspacePackageKind.Page, installedPluginVersions, pluginChoices);

    public PackageImportResult ImportScheme(
        string packagePath,
        IReadOnlyDictionary<string, string>? installedPluginVersions = null,
        IReadOnlyDictionary<string, PluginVersionChoice>? pluginChoices = null)
    {
        using var session = BeginImportScheme(packagePath, installedPluginVersions, pluginChoices);
        if (session.Result.Ready) session.Complete();
        return session.Result;
    }

    public WorkspacePackageImportSession BeginImportScheme(
        string packagePath,
        IReadOnlyDictionary<string, string>? installedPluginVersions = null,
        IReadOnlyDictionary<string, PluginVersionChoice>? pluginChoices = null) =>
        BeginImportWorkspaceBundle(packagePath, WorkspacePackageKind.Scheme, installedPluginVersions, pluginChoices);

    private WorkspacePackageImportSession BeginImportWorkspaceBundle(
        string packagePath,
        WorkspacePackageKind kind,
        IReadOnlyDictionary<string, string>? installedPluginVersions,
        IReadOnlyDictionary<string, PluginVersionChoice>? pluginChoices)
    {
        var extractionRoot = Path.Combine(_paths.Temp, $"import-extract-{Guid.NewGuid():N}");
        var entityStage = Path.Combine(_paths.Temp, $"import-entity-{Guid.NewGuid():N}");
        var backupRoot = Path.Combine(_paths.Temp, $"import-backup-{Guid.NewGuid():N}");
        try
        {
            SafeExtractPackage(packagePath, extractionRoot);
            var descriptor = ReadBundleDescriptor(extractionRoot, kind);
            var installedVersions = installedPluginVersions ?? EmptyPluginVersions;
            var choices = pluginChoices ?? EmptyPluginChoices;
            var packagedPlugins = ReadPackagedPlugins(extractionRoot);
            var missingPlugins = descriptor.PluginDependencies
                .Where(dependency =>
                    !installedVersions.ContainsKey(dependency.Id) &&
                    !packagedPlugins.ContainsKey(dependency.Id))
                .Select(dependency => dependency.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (missingPlugins.Length > 0)
            {
                return WorkspacePackageImportSession.Completed(
                    new PackageImportResult(false, descriptor.Destination, missingPlugins, [], []),
                    extractionRoot,
                    entityStage,
                    backupRoot);
            }

            var conflicts = FindPluginConflicts(descriptor.PluginDependencies, installedVersions, packagedPlugins);
            var unresolved = conflicts
                .Where(conflict => !choices.TryGetValue(conflict.Id, out var choice) || !ChoiceCanResolve(conflict, choice))
                .ToArray();
            if (unresolved.Length > 0)
            {
                return WorkspacePackageImportSession.Completed(
                    new PackageImportResult(false, descriptor.Destination, [], unresolved, []),
                    extractionRoot,
                    entityStage,
                    backupRoot);
            }

            var pluginsToInstall = packagedPlugins.Values
                .Where(plugin =>
                    !installedVersions.ContainsKey(plugin.Id) ||
                    (choices.TryGetValue(plugin.Id, out var choice) && choice == PluginVersionChoice.UsePackage))
                .Select(plugin => plugin.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            CopyRootEntity(extractionRoot, entityStage);
            var operations = new List<FileInstallOperation>
            {
                FileInstallOperation.Directory(entityStage, descriptor.Destination),
            };
            AddDirectoryOperations(extractionRoot, "pages", _paths.Pages, "onedesk.page.json", operations);
            AddDirectoryOperations(extractionRoot, "components", _paths.Components, "onedesk.component.json", operations);
            AddActionOperations(extractionRoot, operations);
            AddPluginOperations(extractionRoot, pluginsToInstall, operations);

            var transaction = FileInstallTransaction.Begin(operations, backupRoot);
            return new WorkspacePackageImportSession(
                new PackageImportResult(true, descriptor.Destination, [], [], pluginsToInstall.ToArray()),
                transaction,
                extractionRoot,
                entityStage,
                backupRoot);
        }
        catch
        {
            DeleteDirectory(extractionRoot);
            DeleteDirectory(entityStage);
            DeleteDirectory(backupRoot);
            throw;
        }
    }

    private BundleDescriptor ReadBundleDescriptor(string root, WorkspacePackageKind kind)
    {
        return kind switch
        {
            WorkspacePackageKind.Component => ReadComponentDescriptor(root),
            WorkspacePackageKind.Page => ReadPageDescriptor(root),
            WorkspacePackageKind.Scheme => ReadSchemeDescriptor(root),
            _ => throw new InvalidDataException("WorkspacePackageKindUnsupported"),
        };
    }

    private BundleDescriptor ReadComponentDescriptor(string root)
    {
        var manifest = ReadManifest<ComponentDefinition>(Path.Combine(root, "onedesk.component.json"));
        return new BundleDescriptor(
            Path.Combine(_paths.Components, SafeId(manifest.Id)),
            manifest.PluginDependencies);
    }

    private BundleDescriptor ReadPageDescriptor(string root)
    {
        var manifest = ReadManifest<PageDefinition>(Path.Combine(root, "onedesk.page.json"));
        var pluginDependencies = ReadNestedComponentDependencies(root);
        return new BundleDescriptor(Path.Combine(_paths.Pages, SafeId(manifest.Id)), pluginDependencies);
    }

    private BundleDescriptor ReadSchemeDescriptor(string root)
    {
        var manifest = ReadManifest<SchemeDefinition>(Path.Combine(root, "onedesk.scheme.json"));
        var dependencies = manifest.PluginDependencies
            .Concat(ReadNestedComponentDependencies(root))
            .GroupBy(dependency => dependency.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        return new BundleDescriptor(Path.Combine(_paths.Schemes, SafeId(manifest.Id)), dependencies);
    }

    private static IReadOnlyList<DependencyDefinition> ReadNestedComponentDependencies(string root)
    {
        var componentRoot = Path.Combine(root, "components");
        if (!Directory.Exists(componentRoot)) return [];
        return Directory.EnumerateFiles(componentRoot, "onedesk.component.json", SearchOption.AllDirectories)
            .Select(ReadManifest<ComponentDefinition>)
            .SelectMany(component => component.PluginDependencies)
            .GroupBy(dependency => dependency.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static IReadOnlyDictionary<string, PluginManifest> ReadPackagedPlugins(string root)
    {
        var pluginRoot = Path.Combine(root, "plugins");
        if (!Directory.Exists(pluginRoot)) return new Dictionary<string, PluginManifest>(StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateDirectories(pluginRoot, "*", SearchOption.TopDirectoryOnly)
            .Select(directory => ReadManifest<PluginManifest>(Path.Combine(directory, "onedesk.plugin.json")))
            .ToDictionary(plugin => plugin.Id, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<PluginVersionConflict> FindPluginConflicts(
        IEnumerable<DependencyDefinition> dependencies,
        IReadOnlyDictionary<string, string> installedVersions,
        IReadOnlyDictionary<string, PluginManifest> packagedPlugins)
    {
        var conflicts = new List<PluginVersionConflict>();
        foreach (var dependency in dependencies
                     .Where(dependency => string.Equals(dependency.Kind, "plugin", StringComparison.OrdinalIgnoreCase))
                     .DistinctBy(dependency => dependency.Id, StringComparer.OrdinalIgnoreCase))
        {
            installedVersions.TryGetValue(dependency.Id, out var installedVersion);
            packagedPlugins.TryGetValue(dependency.Id, out var packagedPlugin);
            var packagedVersion = packagedPlugin?.Version;
            var installedMatches = string.Equals(installedVersion, dependency.Version, StringComparison.OrdinalIgnoreCase);
            var packagedMatches = string.Equals(packagedVersion, dependency.Version, StringComparison.OrdinalIgnoreCase);
            if ((installedVersion is not null && !installedMatches) ||
                (installedVersion is null && packagedVersion is not null && !packagedMatches))
            {
                conflicts.Add(new PluginVersionConflict(
                    dependency.Id,
                    dependency.Version,
                    installedVersion,
                    packagedVersion,
                    installedVersion is not null,
                    packagedVersion is not null));
            }
        }
        return conflicts;
    }

    private static bool ChoiceCanResolve(PluginVersionConflict conflict, PluginVersionChoice choice) => choice switch
    {
        PluginVersionChoice.KeepInstalled => conflict.CanKeepInstalled,
        PluginVersionChoice.UsePackage => conflict.CanUsePackage,
        _ => false,
    };

    private static void CopyRootEntity(string source, string destination)
    {
        var excluded = new HashSet<string>(["pages", "components", "actions", "plugins"], StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.TopDirectoryOnly))
        {
            if (excluded.Contains(Path.GetFileName(directory))) continue;
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static void AddDirectoryOperations(
        string extractionRoot,
        string sourceName,
        string destinationRoot,
        string manifestName,
        ICollection<FileInstallOperation> operations)
    {
        var sourceRoot = Path.Combine(extractionRoot, sourceName);
        if (!Directory.Exists(sourceRoot)) return;
        foreach (var source in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.TopDirectoryOnly))
        {
            var manifestPath = Path.Combine(source, manifestName);
            if (!File.Exists(manifestPath)) throw new InvalidDataException($"PackageDependencyManifestMissing:{manifestName}");
            var id = manifestName switch
            {
                "onedesk.page.json" => ReadManifest<PageDefinition>(manifestPath).Id,
                "onedesk.component.json" => ReadManifest<ComponentDefinition>(manifestPath).Id,
                _ => throw new InvalidDataException("PackageDependencyManifestUnsupported"),
            };
            operations.Add(FileInstallOperation.Directory(source, Path.Combine(destinationRoot, SafeId(id))));
        }
    }

    private void AddActionOperations(string extractionRoot, ICollection<FileInstallOperation> operations)
    {
        var sourceRoot = Path.Combine(extractionRoot, "actions");
        if (!Directory.Exists(sourceRoot)) return;
        foreach (var source in Directory.EnumerateFiles(sourceRoot, "*.json", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(source);
            operations.Add(FileInstallOperation.File(source, Path.Combine(_paths.Actions, fileName)));
        }
    }

    private void AddPluginOperations(
        string extractionRoot,
        IReadOnlySet<string> pluginsToInstall,
        ICollection<FileInstallOperation> operations)
    {
        var sourceRoot = Path.Combine(extractionRoot, "plugins");
        if (!Directory.Exists(sourceRoot)) return;
        foreach (var source in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.TopDirectoryOnly))
        {
            var manifest = ReadManifest<PluginManifest>(Path.Combine(source, "onedesk.plugin.json"));
            if (!pluginsToInstall.Contains(manifest.Id)) continue;
            operations.Add(FileInstallOperation.Directory(source, Path.Combine(_paths.Plugins, SafeId(manifest.Id))));
        }
    }

    private static T ReadManifest<T>(string path)
    {
        if (!File.Exists(path)) throw new InvalidDataException($"PackageManifestMissing:{Path.GetFileName(path)}");
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, PackageJsonOptions)
            ?? throw new InvalidDataException($"PackageManifestInvalid:{Path.GetFileName(path)}");
    }

    private static string SafeId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id is "." or ".." || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("PackageIdentityInvalid");
        return id;
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

    public static void SafeExtractPackage(string packagePath, string destinationDirectory)
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
        var scheme = JsonSerializer.Deserialize<SchemeDefinition>(stream, PackageJsonOptions);
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

    private async Task<ComponentCopyResult> CopyComponentsAsync(IEnumerable<string> componentIds, string destinationRoot, CancellationToken cancellationToken)
    {
        var actionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pluginDependencies = new Dictionary<string, DependencyDefinition>(StringComparer.OrdinalIgnoreCase);
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
            foreach (var dependency in component?.PluginDependencies ?? [])
            {
                pluginDependencies.TryAdd(dependency.Id, dependency);
            }
        }

        return new ComponentCopyResult(actionIds.ToArray(), pluginDependencies.Values.ToArray());
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

    private void CopyPlugins(IEnumerable<DependencyDefinition> dependencies, string destinationRoot)
    {
        foreach (var dependency in dependencies
                     .Where(dependency => string.Equals(dependency.Kind, "plugin", StringComparison.OrdinalIgnoreCase))
                     .DistinctBy(dependency => dependency.Id, StringComparer.OrdinalIgnoreCase))
        {
            var source = Path.Combine(_paths.Plugins, dependency.Id);
            if (!File.Exists(Path.Combine(source, "onedesk.plugin.json")))
                throw new InvalidDataException($"RequiredPluginPackageMissing:{dependency.Id}");
            CopyDirectory(source, Path.Combine(destinationRoot, dependency.Id));
        }
    }

    private async Task<PackageExportResult> ExportResultAsync(string destination, string kind, CancellationToken cancellationToken)
    {
        return new PackageExportResult(true, kind, destination, await ComputeHashAsync(destination, cancellationToken), new FileInfo(destination).Length);
    }

    private static async Task<T?> ReadManifestAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, PackageJsonOptions, cancellationToken);
    }

    private static async Task WriteDependencyReportAsync(string root, IReadOnlyList<DependencyDefinition> dependencies, CancellationToken cancellationToken)
    {
        var reportPath = Path.Combine(root, "onedesk.dependencies.json");
        await using var stream = File.Create(reportPath);
        await JsonSerializer.SerializeAsync(stream, new { pluginDependencies = dependencies }, PackageJsonOptions, cancellationToken);
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

    private sealed record BundleDescriptor(string Destination, IReadOnlyList<DependencyDefinition> PluginDependencies);
    private sealed record ComponentCopyResult(
        IReadOnlyList<string> ActionIds,
        IReadOnlyList<DependencyDefinition> PluginDependencies);

    private static readonly JsonSerializerOptions PackageJsonOptions = CreatePackageJsonOptions();
    private static readonly IReadOnlyDictionary<string, string> EmptyPluginVersions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlyDictionary<string, PluginVersionChoice> EmptyPluginChoices =
        new Dictionary<string, PluginVersionChoice>(StringComparer.OrdinalIgnoreCase);

    private static JsonSerializerOptions CreatePackageJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private enum WorkspacePackageKind
    {
        Component,
        Page,
        Scheme,
    }
}

public enum PluginVersionChoice
{
    KeepInstalled,
    UsePackage,
}

public sealed record PluginVersionConflict(
    string Id,
    string RequiredVersion,
    string? InstalledVersion,
    string? PackagedVersion,
    bool CanKeepInstalled,
    bool CanUsePackage);

public sealed record PackageImportResult(
    bool Ready,
    string DestinationDirectory,
    IReadOnlyList<string> MissingPluginIds,
    IReadOnlyList<PluginVersionConflict> UnresolvedPluginConflicts,
    IReadOnlyList<string> InstalledPluginIds);

/// <summary>
/// 工作区包文件已切换但尚未最终提交的会话。调用方可先完成插件握手等外部校验，
/// 未调用 Complete 就释放会话时会恢复导入前的全部文件。
/// </summary>
public sealed class WorkspacePackageImportSession : IDisposable
{
    private readonly FileInstallTransaction.FileInstallSession? _transaction;
    private readonly IReadOnlyList<string> _temporaryDirectories;
    private int _finished;

    internal WorkspacePackageImportSession(
        PackageImportResult result,
        FileInstallTransaction.FileInstallSession? transaction,
        params string[] temporaryDirectories)
    {
        Result = result;
        _transaction = transaction;
        _temporaryDirectories = temporaryDirectories;
    }

    public PackageImportResult Result { get; }

    internal static WorkspacePackageImportSession Completed(PackageImportResult result, params string[] temporaryDirectories) =>
        new(result, null, temporaryDirectories);

    public void Complete()
    {
        if (!Result.Ready) throw new InvalidOperationException("WorkspaceImportNotReady");
        if (Interlocked.CompareExchange(ref _finished, 1, 0) != 0)
        {
            throw new InvalidOperationException("WorkspaceImportAlreadyFinished");
        }
        _transaction?.Complete();
        Cleanup();
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _finished, 2, 0) != 0) return;
        _transaction?.Dispose();
        Cleanup();
    }

    private void Cleanup()
    {
        foreach (var directory in _temporaryDirectories)
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}

public sealed record PackageExportResult(bool Ready, string Kind, string PackagePath, string Sha256, long SizeBytes);
