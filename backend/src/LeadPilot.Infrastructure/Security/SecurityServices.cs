using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LeadPilot.Application.Common;
using LeadPilot.Domain.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LeadPilot.Infrastructure.Security;

/// <summary>ASP.NET Core Identity's PBKDF2 hasher (HMAC-SHA512, 100k iterations in v3 format).</summary>
public sealed class PasswordHasherService : IPasswordHasherService
{
    private readonly PasswordHasher<User> _hasher = new(Options.Create(new PasswordHasherOptions { IterationCount = 210_000 }));
    private static readonly User Dummy = new();

    public string Hash(string password) => _hasher.HashPassword(Dummy, password);

    public bool Verify(string hash, string password, out bool needsRehash)
    {
        var result = _hasher.VerifyHashedPassword(Dummy, hash, password);
        needsRehash = result == PasswordVerificationResult.SuccessRehashNeeded;
        return result != PasswordVerificationResult.Failed;
    }
}

public sealed class JwtTokenService(IOptions<JwtOptions> options, IClock clock) : ITokenService
{
    public (string Token, DateTime ExpiresAt) CreateAccessToken(User user)
    {
        var o = options.Value;
        var expires = clock.UtcNow.AddMinutes(o.AccessTokenMinutes);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.SigningKey));
        var token = new JwtSecurityToken(
            issuer: o.Issuer,
            audience: o.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("name", user.DisplayName)
            ],
            notBefore: clock.UtcNow,
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public string CreateRefreshTokenValue() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48));

    public string HashToken(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

/// <summary>Encrypts secrets at rest with ASP.NET Core Data Protection (keys persisted outside the database).</summary>
public sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    public string Protect(string purpose, string plaintext) => provider.CreateProtector("LeadPilot." + purpose).Protect(plaintext);
    public string Unprotect(string purpose, string ciphertext) => provider.CreateProtector("LeadPilot." + purpose).Unprotect(ciphertext);
}
