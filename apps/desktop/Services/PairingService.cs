using System.Security.Cryptography;

namespace OneDesk.Desktop.Services;

public sealed class PairingService
{
    private readonly Dictionary<string, DateTimeOffset> _codes = new();

    public string GenerateVerificationCode()
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        _codes[code] = DateTimeOffset.UtcNow.AddMinutes(5);
        return code;
    }

    public bool ValidateCode(string code)
    {
        if (!_codes.TryGetValue(code, out var expiresAt))
        {
            return false;
        }

        _codes.Remove(code);
        return expiresAt > DateTimeOffset.UtcNow;
    }

    public string CreateTrustCredential()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    }

    public string CreateQrPayload(string ip, int port, string verificationCode)
    {
        return $"onedesk://pair?host={Uri.EscapeDataString(ip)}&port={port}&code={verificationCode}";
    }
}
