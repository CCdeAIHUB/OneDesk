using System.Security.Cryptography;
using System.Text;
using OneDesk.Desktop.Storage;

namespace OneDesk.Desktop.Services;

/// <summary>
/// 跨平台桌面凭据存储。密文使用每次写入独立 nonce 的 AES-GCM；主密钥只保存在当前用户数据目录。
/// </summary>
public sealed class DesktopCredentialVault
{
    private readonly string _root;
    private readonly byte[] _key;

    public DesktopCredentialVault(OneDeskDataPaths paths)
    {
        _root = Path.Combine(paths.Root, "credentials");
        Directory.CreateDirectory(_root);
        var keyPath = Path.Combine(_root, "vault.key");
        _key = LoadOrCreateKey(keyPath);
    }

    public async Task WriteAsync(string sourceKey, string key, string value, CancellationToken cancellationToken)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintext = Encoding.UTF8.GetBytes(value);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(_key, tag.Length))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes(sourceKey));
        }
        var payload = new byte[1 + nonce.Length + tag.Length + ciphertext.Length];
        payload[0] = 1;
        nonce.CopyTo(payload, 1);
        tag.CopyTo(payload, 1 + nonce.Length);
        ciphertext.CopyTo(payload, 1 + nonce.Length + tag.Length);
        var path = ResolvePath(sourceKey, key);
        var temporary = $"{path}.tmp-{Guid.NewGuid():N}";
        await File.WriteAllBytesAsync(temporary, payload, cancellationToken);
        File.Move(temporary, path, overwrite: true);
        RestrictFile(path);
    }

    public async Task<string?> ReadAsync(string sourceKey, string key, CancellationToken cancellationToken)
    {
        var path = ResolvePath(sourceKey, key);
        if (!File.Exists(path)) return null;
        var payload = await File.ReadAllBytesAsync(path, cancellationToken);
        if (payload.Length < 29 || payload[0] != 1) throw new InvalidDataException("CredentialPayloadInvalid");
        var nonce = payload.AsSpan(1, 12);
        var tag = payload.AsSpan(13, 16);
        var ciphertext = payload.AsSpan(29);
        var plaintext = new byte[ciphertext.Length];
        using (var aes = new AesGcm(_key, tag.Length))
        {
            aes.Decrypt(nonce, ciphertext, tag, plaintext, Encoding.UTF8.GetBytes(sourceKey));
        }
        return Encoding.UTF8.GetString(plaintext);
    }

    public bool Delete(string sourceKey, string key)
    {
        var path = ResolvePath(sourceKey, key);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    private string ResolvePath(string sourceKey, string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidDataException("CredentialKeyMissing");
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{sourceKey}\0{key}"))).ToLowerInvariant();
        return Path.Combine(_root, $"{identity}.credential");
    }

    private static byte[] LoadOrCreateKey(string path)
    {
        if (File.Exists(path))
        {
            var existing = File.ReadAllBytes(path);
            if (existing.Length != 32) throw new InvalidDataException("CredentialVaultKeyInvalid");
            return existing;
        }
        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(path, key);
        RestrictFile(path);
        return key;
    }

    private static void RestrictFile(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
