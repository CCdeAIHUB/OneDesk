using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OneDesk.Desktop.Services;

public sealed record CodeComponentRuntimeArtifact(string Code, string Style, string Sha256);

public static class CodeComponentArtifactValidator
{
    private const string ManifestPath = "dist/onedesk.runtime.json";

    public static bool TryRead(
        IReadOnlyDictionary<string, string> files,
        out CodeComponentRuntimeArtifact? artifact,
        out string errorCode)
    {
        artifact = null;
        errorCode = string.Empty;
        if (!files.TryGetValue(ManifestPath, out var manifestJson))
        {
            errorCode = "CodeComponentArtifactMissing";
            return false;
        }

        RuntimeManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<RuntimeManifest>(manifestJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            errorCode = "CodeComponentManifestInvalid";
            return false;
        }

        if (manifest is null || manifest.SchemaVersion != 1 ||
            !IsSafeProjectPath(manifest.CodeFile) || !IsSafeProjectPath(manifest.StyleFile) ||
            manifest.Sha256.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit))
        {
            errorCode = "CodeComponentManifestInvalid";
            return false;
        }
        if (!files.TryGetValue(manifest.CodeFile, out var code) || !files.TryGetValue(manifest.StyleFile, out var style))
        {
            errorCode = "CodeComponentArtifactMissing";
            return false;
        }

        var bytes = Encoding.UTF8.GetBytes($"{code}\n/* onedesk-style */\n{style}");
        var actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(actualHash),
                Encoding.ASCII.GetBytes(manifest.Sha256.ToLowerInvariant())))
        {
            errorCode = "CodeComponentArtifactHashMismatch";
            return false;
        }

        artifact = new CodeComponentRuntimeArtifact(code, style, actualHash);
        return true;
    }

    private static bool IsSafeProjectPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return false;
        }
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 && segments.All(segment => segment is not "." and not "..");
    }

    private sealed record RuntimeManifest(
        int SchemaVersion,
        string EntryFile,
        string CodeFile,
        string StyleFile,
        string Sha256);
}
