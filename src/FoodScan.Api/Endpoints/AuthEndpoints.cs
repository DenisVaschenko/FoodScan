using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using FoodScan.Api.Auth;
using FoodScan.Api.Data;
using FoodScan.Api.Domain;
using FoodScan.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FoodScan.Api.Endpoints;

public record RegisterRequest(string Email, string Password, string DisplayName);
public record LoginRequest(string Email, string Password);
public record RefreshRequest(string RefreshToken);
public record ProfileRequest(string DisplayName);

public static class AuthEndpoints
{
    public static Guid UserId(this ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static void MapAuth(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Authentication").RequireRateLimiting("auth");
        auth.MapPost("/register", async (RegisterRequest r, AppDbContext db, IPasswordHasher<User> hasher, CancellationToken ct) =>
        {
            ValidateEmail(r.Email);
            if (r.Password is null || r.Password.Length < 10 || r.Password.Length > 128)
                throw new ApiException(400, "password_length", "Пароль повинен містити 10–128 символів.");
            ValidateName(r.DisplayName);
            var email = r.Email.Trim();
            var normalized = email.ToUpperInvariant();
            if (await db.Users.AnyAsync(x => x.NormalizedEmail == normalized, ct))
                throw new ApiException(409, "email_exists", "Користувач із таким email уже існує.");
            var user = new User { Email = email, NormalizedEmail = normalized, DisplayName = r.DisplayName.Trim() };
            user.PasswordHash = hasher.HashPassword(user, r.Password);
            db.Users.Add(user);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                // Resolve a simultaneous registration without masking unrelated database failures.
                db.ChangeTracker.Clear();
                if (await db.Users.AnyAsync(x => x.NormalizedEmail == normalized, ct))
                    throw new ApiException(409, "email_exists", "Користувач із таким email уже існує.");
                throw;
            }
            return Results.Created("/api/me", new { user.Id, user.Email, user.DisplayName });
        });

        auth.MapPost("/login", async (LoginRequest r, AppDbContext db, IPasswordHasher<User> hasher, TokenService tokens, CancellationToken ct) =>
        {
            ValidateEmail(r.Email);
            if (r.Password is null || r.Password.Length > 128) throw InvalidLogin();
            var email = r.Email.Trim().ToUpperInvariant();
            var user = await db.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == email, ct);
            if (user is null) throw InvalidLogin();
            var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, r.Password);
            if (verification == PasswordVerificationResult.Failed) throw InvalidLogin();
            if (verification == PasswordVerificationResult.SuccessRehashNeeded)
                user.PasswordHash = hasher.HashPassword(user, r.Password);
            var refresh = TokenService.NewRefreshToken();
            db.RefreshSessions.Add(new() { UserId = user.Id, TokenHash = TokenService.Hash(refresh), ExpiresAt = DateTimeOffset.UtcNow.AddDays(14) });
            await db.SaveChangesAsync(ct);
            return Results.Ok(tokens.Issue(user, refresh));
        });

        auth.MapPost("/refresh", async (RefreshRequest r, AppDbContext db, TokenService tokens, CancellationToken ct) =>
        {
            ValidateRefresh(r.RefreshToken);
            var hash = TokenService.Hash(r.RefreshToken);
            var session = await db.RefreshSessions.SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
            if (session is null || session.ExpiresAt <= DateTimeOffset.UtcNow)
                throw new ApiException(401, "invalid_refresh", "Refresh token недійсний або прострочений.");
            var user = await db.Users.SingleAsync(x => x.Id == session.UserId, ct);
            var next = TokenService.NewRefreshToken();
            session.TokenHash = TokenService.Hash(next);
            session.Version++;
            // Keep the original 14-day session deadline; refresh cannot extend it indefinitely.
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException)
            { throw new ApiException(401, "refresh_reused", "Цей refresh token уже використано."); }
            return Results.Ok(tokens.Issue(user, next));
        });

        auth.MapPost("/logout", async (RefreshRequest r, AppDbContext db, CancellationToken ct) =>
        {
            ValidateRefresh(r.RefreshToken);
            var hash = TokenService.Hash(r.RefreshToken);
            await db.RefreshSessions.Where(x => x.TokenHash == hash).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        });

        var me = app.MapGroup("/api/me").WithTags("Profile").RequireAuthorization();
        me.MapGet("", async (ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        {
            var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == principal.UserId(), ct);
            return Results.Ok(new { user.Id, user.Email, user.DisplayName, user.CreatedAt });
        });
        me.MapPut("", async (ProfileRequest r, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        {
            ValidateName(r.DisplayName);
            var user = await db.Users.SingleAsync(x => x.Id == principal.UserId(), ct);
            user.DisplayName = r.DisplayName.Trim();
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { user.Id, user.Email, user.DisplayName, user.CreatedAt });
        });
    }
    private static ApiException InvalidLogin() => new(401, "invalid_credentials", "Неправильний email або пароль.");
    private static void ValidateRefresh(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256)
            throw new ApiException(400, "invalid_token", "Передайте refreshToken.");
    }
    private static void ValidateEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254 || !new EmailAddressAttribute().IsValid(email.Trim()))
            throw new ApiException(400, "invalid_email", "Вкажіть коректний email.");
    }
    private static void ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80)
            throw new ApiException(400, "invalid_name", "Ім'я повинно містити 1–80 символів.");
    }
}
