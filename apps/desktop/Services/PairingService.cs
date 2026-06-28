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

    public TrustedPairingCredential CreateTrustCredential(string deviceId, string displayName)
    {
        var credential = new TrustedPairingCredential(
            deviceId,
            displayName,
            null,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
            DateTimeOffset.UtcNow);
        _trusted[deviceId] = credential;
        SaveTrustedDevices();
        return credential;
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
}

public sealed record PairingCodeState(string Code, DateTimeOffset ExpiresAt, int Attempts);

public sealed record TrustedPairingCredential(
    string DeviceId,
    string DisplayName,
    string? Remark,
    string Token,
    DateTimeOffset CreatedAt);
