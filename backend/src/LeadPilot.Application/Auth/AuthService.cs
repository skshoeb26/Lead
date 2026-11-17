using FluentValidation;
using LeadPilot.Application.Common;
using LeadPilot.Domain;
using LeadPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeadPilot.Application.Auth;

public sealed record RegisterRequest(string Email, string Password, string DisplayName);
public sealed record LoginRequest(string Email, string Password, string? DeviceName);
public sealed record RefreshRequest(string RefreshToken);
public sealed record LogoutRequest(string RefreshToken);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record UserDto(Guid Id, string Email, string DisplayName, string Role);
public sealed record AuthResponse(string AccessToken, DateTime AccessTokenExpiresAt, string RefreshToken, DateTime RefreshTokenExpiresAt, UserDto User);

public sealed class RegisterValidator : AbstractValidator<RegisterRequest>
{
    public RegisterValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(10).MaximumLength(128)
            .Matches("[A-Z]").WithMessage("Password must contain an upper-case letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lower-case letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");
    }
}

public sealed class LoginValidator : AbstractValidator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(254);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}

public sealed class AuthService(
    IAppDbContext db,
    IPasswordHasherService hasher,
    ITokenService tokens,
    IOptions<JwtOptions> jwt,
    IOptions<AuthOptions> authOptions,
    IClock clock,
    IAuditService audit,
    ILogger<AuthService> logger)
{
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, string? ip, CancellationToken ct)
    {
        var anyUser = await db.Users.AnyAsync(ct);
        if (anyUser && !authOptions.Value.AllowRegistration)
            throw new ForbiddenException("Registration is disabled. The owner account already exists.", "registration_disabled");

        var normalized = request.Email.Trim().ToUpperInvariant();
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized, ct))
            throw new ConflictException("An account with this email already exists.", "email_taken");

        var user = new User
        {
            Email = request.Email.Trim(),
            NormalizedEmail = normalized,
            DisplayName = request.DisplayName.Trim(),
            PasswordHash = hasher.Hash(request.Password),
            Role = anyUser ? Roles.User : Roles.Owner
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("auth.register", nameof(User), user.Id.ToString(), $"role={user.Role}", ct);
        return await IssueAsync(user, null, ip, null, ct);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, string? ip, CancellationToken ct)
    {
        var normalized = request.Email.Trim().ToUpperInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
        var now = clock.UtcNow;

        if (user is null)
        {
            // Hash anyway to keep timing similar for unknown accounts.
            hasher.Verify(hasher.Hash("timing-equaliser"), request.Password, out _);
            throw new UnauthorizedException("Invalid email or password.", "invalid_credentials");
        }
        if (!user.IsActive) throw new UnauthorizedException("Account is disabled.", "account_disabled");
        if (user.LockoutUntil is not null && user.LockoutUntil > now)
            throw new UnauthorizedException("Too many failed attempts. Try again later.", "locked_out");

        if (!hasher.Verify(user.PasswordHash, request.Password, out var rehash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= authOptions.Value.MaxFailedLogins)
            {
                user.LockoutUntil = now.AddMinutes(authOptions.Value.LockoutMinutes);
                user.FailedLoginCount = 0;
                logger.LogWarning("User {UserId} locked out after repeated failed logins", user.Id);
            }
            await db.SaveChangesAsync(ct);
            await audit.LogAsync("auth.login_failed", nameof(User), user.Id.ToString(), null, ct);
            throw new UnauthorizedException("Invalid email or password.", "invalid_credentials");
        }

        if (rehash) user.PasswordHash = hasher.Hash(request.Password);
        user.FailedLoginCount = 0;
        user.LockoutUntil = null;
        user.LastLoginAt = now;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("auth.login", nameof(User), user.Id.ToString(), null, ct);
        return await IssueAsync(user, null, ip, request.DeviceName, ct);
    }

    /// <summary>
    /// Rotates the refresh token. Presenting an already-rotated token is treated as theft:
    /// the whole token family is revoked.
    /// </summary>
    public async Task<AuthResponse> RefreshAsync(string refreshToken, string? ip, CancellationToken ct)
    {
        var hash = tokens.HashToken(refreshToken);
        var existing = await db.RefreshTokens.Include(t => t.User).FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
                       ?? throw new UnauthorizedException("Invalid refresh token.", "invalid_refresh_token");
        var now = clock.UtcNow;

        if (existing.RevokedAt is not null)
        {
            if (existing.ReplacedByTokenHash is not null)
            {
                var family = await db.RefreshTokens.Where(t => t.FamilyId == existing.FamilyId && t.RevokedAt == null).ToListAsync(ct);
                foreach (var t in family) { t.RevokedAt = now; t.RevokedReason = "reuse_detected"; }
                await db.SaveChangesAsync(ct);
                await audit.LogAsync("auth.refresh_reuse_detected", nameof(User), existing.UserId.ToString(), null, ct);
                logger.LogWarning("Refresh token reuse detected for user {UserId}; family revoked", existing.UserId);
            }
            throw new UnauthorizedException("Refresh token has been revoked.", "refresh_token_revoked");
        }
        if (existing.ExpiresAt <= now) throw new UnauthorizedException("Refresh token expired.", "refresh_token_expired");
        if (existing.User is null || !existing.User.IsActive) throw new UnauthorizedException("Account is disabled.", "account_disabled");

        return await IssueAsync(existing.User, existing, ip, existing.DeviceName, ct);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct)
    {
        var hash = tokens.HashToken(refreshToken);
        var existing = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (existing is null || existing.RevokedAt is not null) return;
        existing.RevokedAt = clock.UtcNow;
        existing.RevokedReason = "logout";
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("auth.logout", nameof(User), existing.UserId.ToString(), null, ct);
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException("User", userId);
        if (!hasher.Verify(user.PasswordHash, request.CurrentPassword, out _))
            throw new ValidationFailedException("Current password is incorrect.");
        var validation = new RegisterValidator().Validate(new RegisterRequest(user.Email, request.NewPassword, user.DisplayName));
        if (!validation.IsValid) throw new ValidationFailedException(string.Join(" ", validation.Errors.Select(e => e.ErrorMessage)));
        user.PasswordHash = hasher.Hash(request.NewPassword);
        var active = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in active) { t.RevokedAt = clock.UtcNow; t.RevokedReason = "password_changed"; }
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("auth.password_changed", nameof(User), userId.ToString(), null, ct);
    }

    public async Task<UserDto> GetAsync(Guid userId, CancellationToken ct)
    {
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ct) ?? throw new NotFoundException("User", userId);
        return new UserDto(u.Id, u.Email, u.DisplayName, u.Role);
    }

    /// <summary>Creates the owner from configuration on first start if no users exist.</summary>
    public async Task EnsureBootstrapOwnerAsync(CancellationToken ct)
    {
        var o = authOptions.Value;
        if (string.IsNullOrWhiteSpace(o.BootstrapOwnerEmail) || string.IsNullOrWhiteSpace(o.BootstrapOwnerPassword)) return;
        if (await db.Users.AnyAsync(ct)) return;
        db.Users.Add(new User
        {
            Email = o.BootstrapOwnerEmail.Trim(),
            NormalizedEmail = o.BootstrapOwnerEmail.Trim().ToUpperInvariant(),
            DisplayName = "Owner",
            PasswordHash = hasher.Hash(o.BootstrapOwnerPassword),
            Role = Roles.Owner
        });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Bootstrap owner account created");
    }

    private async Task<AuthResponse> IssueAsync(User user, RefreshToken? rotating, string? ip, string? device, CancellationToken ct)
    {
        var (access, accessExpires) = tokens.CreateAccessToken(user);
        var refreshValue = tokens.CreateRefreshTokenValue();
        var now = clock.UtcNow;
        var refresh = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokens.HashToken(refreshValue),
            FamilyId = rotating?.FamilyId ?? Guid.NewGuid(),
            ExpiresAt = now.AddDays(jwt.Value.RefreshTokenDays),
            CreatedByIp = ip,
            DeviceName = device
        };
        if (rotating is not null)
        {
            rotating.RevokedAt = now;
            rotating.RevokedReason = "rotated";
            rotating.ReplacedByTokenHash = refresh.TokenHash;
        }
        db.RefreshTokens.Add(refresh);
        await db.SaveChangesAsync(ct);
        return new AuthResponse(access, accessExpires, refreshValue, refresh.ExpiresAt, new UserDto(user.Id, user.Email, user.DisplayName, user.Role));
    }
}
