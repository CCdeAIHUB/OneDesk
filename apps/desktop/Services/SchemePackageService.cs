using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OneDesk.Desktop.Domain;
using OneDesk.Desktop.Storage;

namespace OneDesk.Desktop.Services;

public sealed class SchemePackageService
{
    private readonly OneDeskDataPaths _paths;

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
        if (File.Exists(destinationPackage))
        {
            File.Delete(destinationPackage);
        }

        ZipFile.CreateFromDirectory(sourceDirectory, destinationPackage, CompressionLevel.Optimal, includeBaseDirectory: false);
    }

    private void ImportPackage(string packagePath, string destinationDirectory, string requiredManifest)
    {
        var temp = $"{destinationDirectory}.tmp-{Guid.NewGuid():N}";
        ZipFile.ExtractToDirectory(packagePath, temp);
        EnsureManifest(temp, requiredManifest);

        if (Directory.Exists(destinationDirectory))
        {
            Directory.Delete(destinationDirectory, recursive: true);
        }

        Directory.Move(temp, destinationDirectory);
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
}

public sealed record PackageImportResult(bool Ready, string DestinationDirectory, IReadOnlyList<string> MissingPluginIds);
