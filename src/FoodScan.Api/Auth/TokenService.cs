using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FoodScan.Api.Domain;
using Microsoft.IdentityModel.Tokens;

namespace FoodScan.Api.Auth;

public record TokenPair(string AccessToken, string RefreshToken, int ExpiresIn = 900, string TokenType = "Bearer");

public sealed class TokenService(IConfiguration config)
{
    public TokenPair Issue(User user, string refreshToken)
    {
        var token = new JwtSecurityToken(
            issuer: "FoodScan", audience: "FoodScan.Client",
            claims: [new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())],
            notBefore: DateTime.UtcNow, expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(config["Jwt:Key"]!)), SecurityAlgorithms.HmacSha256));
        return new(new JwtSecurityTokenHandler().WriteToken(token), refreshToken);
    }
    public static string NewRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
