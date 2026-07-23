using System.Security.Cryptography;
using System.Text.Json;
using OneDesk.Desktop.Storage;

namespace OneDesk.Desktop.Services;

public sealed class PairingService
{
    private readonly Dictionary<string, PairingCodeState> _codes = new();
    private readonly Dictionary<string, TrustedPairingCredential> _trusted = new();
    private readonly OneDeskDataPaths _paths;
    private readonly string _trustedPath;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const int MaxAttempts = 5;

    public PairingService(OneDeskDataPaths paths)
    {
        _paths = paths;
        _paths.EnsureCreated();
        _trustedPath = Path.Combine(_paths.Root, "trusted-devices.json");
        LoadTrustedDevices();
    }

    public string GenerateVerificationCode()
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        _codes[code] = new PairingCodeState(code, DateTimeOffset.UtcNow.AddMinutes(5), 0);
        return code;
    }

    public bool ValidateCode(string code)
    {
        if (!_codes.TryGetValue(code, out var state))
        {
            return false;
        }

        if (state.ExpiresAt <= DateTimeOffset.UtcNow || state.Attempts >= MaxAttempts)
        {
            _codes.Remove(code);
            return false;
        }

        _codes[code] = state with { Attempts = state.Attempts + 1 };
        if (state.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return false;
        }

        _codes.Remove(code);
        return true;
    }

    public TrustedPairingCredential? FindPairingIdentity(string? stableDeviceKey, string displayName)
    {
        var normalizedKey = NormalizeStableDeviceKey(stableDeviceKey);
        if (normalizedKey is null)
        {
            return null;
        }

        var exact = _trusted.Values.FirstOrDefault(item =>
            string.Equals(item.StableDeviceKey, normalizedKey, StringComparison.Ordinal));
        if (exact is not null)
        {
            return exact;
        }

        // 旧版本没有保存稳定键。仅在名称唯一时迁移，避免两台同型号设备被错误合并。
        var legacyMatches = _trusted.Values
            .Where(item => string.IsNullOrWhiteSpace(item.StableDeviceKey))
            .Where(item => string.Equals(item.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return legacyMatches.Length == 1 ? legacyMatches[0] : null;
    }

    public TrustedPairingCredential CreateTrustCredential(
        string deviceId,
        string displayName,
        string? stableDeviceKey = null,
        string? platform = null,
        string? architecture = null)
    {
        _trusted.TryGetValue(deviceId, out var existing);
        var credential = new TrustedPairingCredential(
            deviceId,
            displayName,
            existing?.Remark,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
            existing?.CreatedAt ?? DateTimeOffset.UtcNow,
            NormalizeStableDeviceKey(stableDeviceKey) ?? existing?.StableDeviceKey,
            string.IsNullOrWhiteSpace(platform) ? existing?.Platform : platform.Trim(),
            string.IsNullOrWhiteSpace(architecture) ? existing?.Architecture : architecture.Trim());
        _trusted[deviceId] = credential;
        SaveTrustedDevices();
        return credential;
    }

    public bool BindStableDeviceKey(string deviceId, string? stableDeviceKey, string? platform, string? architecture)
    {
        var normalizedKey = NormalizeStableDeviceKey(stableDeviceKey);
        if (normalizedKey is null || !_trusted.TryGetValue(deviceId, out var credential))
        {
            return false;
        }

        if (_trusted.Values.Any(item =>
                !string.Equals(item.DeviceId, deviceId, StringComparison.Ordinal) &&
                string.Equals(item.StableDeviceKey, normalizedKey, StringComparison.Ordinal)))
        {
            return false;
        }

        // 可信连接已先验证长期凭据，因此此处只为现有身份补充重装可恢复的索引。
        _trusted[deviceId] = credential with
        {
            StableDeviceKey = normalizedKey,
            Platform = string.IsNullOrWhiteSpace(platform) ? credential.Platform : platform.Trim(),
            Architecture = string.IsNullOrWhiteSpace(architecture) ? credential.Architecture : architecture.Trim()
        };
        SaveTrustedDevices();
        return true;
    }

    public TrustedPairingCredential? RenameTrustedDevice(string deviceId, string remark)
    {
        if (!_trusted.TryGetValue(deviceId, out var credential))
        {
            return null;
        }

        var renamed = credential with { Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim() };
        _trusted[deviceId] = renamed;
        SaveTrustedDevices();
        return renamed;
    }

    public bool ValidateTrustCredential(string deviceId, string token)
    {
        if (!_trusted.TryGetValue(deviceId, out var credential))
        {
            return false;
        }

        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(credential.Token),
                Convert.FromBase64String(token));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public IReadOnlyCollection<TrustedPairingCredential> TrustedDevices()
    {
        return _trusted.Values.ToArray();
    }

    public string CreateQrPayload(string ip, int port, string verificationCode)
    {
        return $"onedesk://pair?host={Uri.EscapeDataString(ip)}&port={port}&code={verificationCode}";
    }

    private void LoadTrustedDevices()
    {
        if (!File.Exists(_trustedPath))
        {
            return;
        }

        try
        {
            var trusted = JsonSerializer.Deserialize<IReadOnlyList<TrustedPairingCredential>>(File.ReadAllText(_trustedPath), JsonOptions) ?? [];
            foreach (var credential in trusted.Where(item => !string.IsNullOrWhiteSpace(item.DeviceId) && !string.IsNullOrWhiteSpace(item.Token)))
            {
                _trusted[credential.DeviceId] = credential;
            }
        }
        catch (JsonException)
        {
            _trusted.Clear();
        }
    }

    private void SaveTrustedDevices()
    {
        File.WriteAllText(_trustedPath, JsonSerializer.Serialize(_trusted.Values.OrderBy(item => item.CreatedAt).ToArray(), JsonOptions));
    }

    private static string? NormalizeStableDeviceKey(string? stableDeviceKey)
    {
        var value = stableDeviceKey?.Trim();
        return string.IsNullOrWhiteSpace(value) || value.Length > 128 ? null : value;
    }
}

public sealed record PairingCodeState(string Code, DateTimeOffset ExpiresAt, int Attempts);

public sealed record TrustedPairingCredential(
    string DeviceId,
    string DisplayName,
    string? Remark,
    string Token,
    DateTimeOffset CreatedAt,
    string? StableDeviceKey = null,
    string? Platform = null,
    string? Architecture = null);
