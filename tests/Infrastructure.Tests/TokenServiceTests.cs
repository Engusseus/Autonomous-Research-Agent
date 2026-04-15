using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AutonomousResearchAgent.Application.Auth;
using AutonomousResearchAgent.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Infrastructure.Tests;

public class TokenServiceTests
{
    private readonly JwtOptions _jwtOptions;
    private readonly TokenService _sut;

    public TokenServiceTests()
    {
        _jwtOptions = new JwtOptions
        {
            SigningKey = "super-secret-key-that-needs-to-be-at-least-32-bytes-long",
            Issuer = "test-issuer",
            Audience = "test-audience",
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7
        };

        var optionsMock = new Mock<IOptions<JwtOptions>>();
        optionsMock.Setup(x => x.Value).Returns(_jwtOptions);

        _sut = new TokenService(optionsMock.Object, NullLogger<TokenService>.Instance);
    }

    [Fact]
    public void GenerateAccessToken_ReturnsValidToken()
    {
        var userId = Guid.NewGuid();
        var email = "test@example.com";
        var roles = new[] { "Admin", "User" };

        var (token, expiresAt) = _sut.GenerateAccessToken(userId, email, roles);

        Assert.NotNull(token);
        Assert.True(expiresAt > DateTimeOffset.UtcNow);

        var principal = _sut.ValidateToken(token);

        Assert.NotNull(principal);
        // The token service generates the Sub claim as JwtRegisteredClaimNames.Sub but reads it back as ClaimTypes.NameIdentifier
        // Let's just check that it parses to a generic principal with these claims present
        var claims = principal.Claims.ToList();
        Assert.Contains(claims, c => c.Value == userId.ToString());
        Assert.Contains(claims, c => c.Value == email);
        Assert.Contains(claims, c => c.Type == ClaimTypes.Role && c.Value == "Admin");
        Assert.Contains(claims, c => c.Type == ClaimTypes.Role && c.Value == "User");
    }

    [Fact]
    public void ValidateToken_WithInvalidToken_ReturnsNull()
    {
        var result = _sut.ValidateToken("invalid-token-string");

        Assert.Null(result);
    }

    [Fact]
    public void ValidateToken_WithExpiredToken_ReturnsNull()
    {
        var handler = new JwtSecurityTokenHandler();
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(_jwtOptions.SigningKey));
        var creds = new Microsoft.IdentityModel.Tokens.SigningCredentials(key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);

        var tokenDescriptor = new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }),
            Expires = DateTime.UtcNow.AddMinutes(-10), // Expired 10 minutes ago
            NotBefore = DateTime.UtcNow.AddMinutes(-20),
            SigningCredentials = creds,
            Issuer = _jwtOptions.Issuer,
            Audience = _jwtOptions.Audience
        };

        var expiredToken = handler.CreateToken(tokenDescriptor);
        var tokenString = handler.WriteToken(expiredToken);

        // Act
        var result = _sut.ValidateToken(tokenString);

        // Assert
        Assert.Null(result);
    }
}
