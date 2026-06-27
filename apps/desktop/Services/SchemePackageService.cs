using System.IO.Compression;
using System.Security.Cryptography;

namespace OneDesk.Desktop.Services;

public sealed class SchemePackageService
{
    public async Task<string> ComputeHashAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public void ExportScheme(string sourceDirectory, string destinationPackage)
    {
        if (File.Exists(destinationPackage))
        {
            File.Delete(destinationPackage);
        }

        ZipFile.CreateFromDirectory(sourceDirectory, destinationPackage, CompressionLevel.Optimal, includeBaseDirectory: false);
    }

    public void ImportScheme(string packagePath, string destinationDirectory)
    {
        var temp = $"{destinationDirectory}.tmp-{Guid.NewGuid():N}";
        ZipFile.ExtractToDirectory(packagePath, temp);
        if (Directory.Exists(destinationDirectory))
        {
            Directory.Delete(destinationDirectory, recursive: true);
        }

        Directory.Move(temp, destinationDirectory);
    }
}
