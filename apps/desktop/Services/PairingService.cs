using System.Security.Cryptography;

namespace OneDesk.Desktop.Services;

public sealed class PairingService
{
    private readonly Dictionary<string, PairingCodeState> _codes = new();
    private readonly Dictionary<string, TrustedPairingCredential> _trusted = new();
    private const int MaxAttempts = 5;

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
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
            DateTimeOffset.UtcNow);
        _trusted[deviceId] = credential;
        return credential;
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
}

public sealed record PairingCodeState(string Code, DateTimeOffset ExpiresAt, int Attempts);

public sealed record TrustedPairingCredential(
    string DeviceId,
    string DisplayName,
    string Token,
    DateTimeOffset CreatedAt);
