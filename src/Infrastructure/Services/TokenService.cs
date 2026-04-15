using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AutonomousResearchAgent.Application.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AutonomousResearchAgent.Infrastructure.Services;

public interface ITokenService
{
    (string Token, DateTimeOffset ExpiresAt) GenerateAccessToken(Guid userId, string email, IEnumerable<string> roles);
    (string Token, DateTimeOffset ExpiresAt) GenerateRefreshToken();
    ClaimsPrincipal? ValidateToken(string token);
}

public sealed class TokenService(IOptions<JwtOptions> jwtOptions) : ITokenService
{
    private readonly JwtOptions _jwtOptions = jwtOptions.Value;

    public (string Token, DateTimeOffset ExpiresAt) GenerateAccessToken(Guid userId, string email, IEnumerable<string> roles)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_jwtOptions.AccessTokenExpirationMinutes);
        var token = GenerateToken(
            claims =>
            {
                claims.Append(new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()));
                claims.Append(new Claim(JwtRegisteredClaimNames.Email, email));
                claims.Append(new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()));
                foreach (var role in roles)
                {
                    claims.Append(new Claim(ClaimTypes.Role, role));
                }
            },
            expiresAt);

        return (token, expiresAt);
    }

    public (string Token, DateTimeOffset ExpiresAt) GenerateRefreshToken()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddDays(_jwtOptions.RefreshTokenExpirationDays);
        var token = GenerateToken(
            claims =>
            {
                claims.Append(new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()));
            },
            expiresAt);

        return (token, expiresAt);
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_jwtOptions.SigningKey);

        try
        {
            var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = !string.IsNullOrWhiteSpace(_jwtOptions.Issuer),
                ValidIssuer = _jwtOptions.Issuer,
                ValidateAudience = !string.IsNullOrWhiteSpace(_jwtOptions.Audience),
                ValidAudience = _jwtOptions.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2)
            }, out _);

            return principal;
        }
        catch
        {
            return null;
        }
    }

    private string GenerateToken(Action<List<Claim>> configureClaims, DateTimeOffset expiresAt)
    {
        var claims = new List<Claim>();
        configureClaims(claims);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = credentials
        };

        if (!string.IsNullOrWhiteSpace(_jwtOptions.Issuer))
        {
            tokenDescriptor.Issuer = _jwtOptions.Issuer;
        }

        if (!string.IsNullOrWhiteSpace(_jwtOptions.Audience))
        {
            tokenDescriptor.Audience = _jwtOptions.Audience;
        }

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}
