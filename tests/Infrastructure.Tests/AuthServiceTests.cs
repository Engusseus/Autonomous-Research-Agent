using AutonomousResearchAgent.Application.Auth;
using AutonomousResearchAgent.Application.Common;
using AutonomousResearchAgent.Domain.Entities;
using AutonomousResearchAgent.Infrastructure.Persistence;
using AutonomousResearchAgent.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Infrastructure.Tests;

public sealed class AuthServiceTests
{
    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task LoginAsync_ReturnsAuthResult_WhenCredentialsAreValid()
    {
        // Arrange
        await using var dbContext = CreateDbContext();
        var mockTokenService = new Mock<ITokenService>();

        var rawPassword = "Password123!";
        var hashedPassword = BCrypt.Net.BCrypt.HashPassword(rawPassword);

        var role = new Role { Id = Guid.NewGuid(), Name = "Admin" };
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            Username = "testuser",
            PasswordHash = hashedPassword,
            IsActive = true
        };
        user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id, Role = role, User = user });

        dbContext.Roles.Add(role);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        mockTokenService
            .Setup(x => x.GenerateAccessToken(user.Id, user.Email, It.Is<IEnumerable<string>>(roles => roles.Contains("Admin"))))
            .Returns(("access-token", DateTimeOffset.UtcNow.AddMinutes(15)));

        mockTokenService
            .Setup(x => x.GenerateRefreshToken())
            .Returns(("refresh-token", DateTimeOffset.UtcNow.AddDays(7)));

        var authService = new AuthService(dbContext, mockTokenService.Object, NullLogger<AuthService>.Instance);
        var command = new LoginCommand(user.Email, rawPassword);

        // Act
        var result = await authService.LoginAsync(command, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("access-token", result.AccessToken);
        Assert.Equal("refresh-token", result.RefreshToken);
        Assert.Equal(user.Id, result.User.Id);
        Assert.Equal(user.Email, result.User.Email);
        Assert.Equal(user.Username, result.User.Username);
        Assert.Contains("Admin", result.User.Roles);

        var savedToken = await dbContext.RefreshTokens.FirstOrDefaultAsync(t => t.UserId == user.Id);
        Assert.NotNull(savedToken);
    }

    [Fact]
    public async Task LoginAsync_ThrowsAuthenticationException_WhenUserNotFound()
    {
        // Arrange
        await using var dbContext = CreateDbContext();
        var mockTokenService = new Mock<ITokenService>();

        var authService = new AuthService(dbContext, mockTokenService.Object, NullLogger<AuthService>.Instance);
        var command = new LoginCommand("nonexistent@example.com", "Password123!");

        // Act & Assert
        await Assert.ThrowsAsync<AuthenticationException>(() => authService.LoginAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task LoginAsync_ThrowsAuthenticationException_WhenUserIsInactive()
    {
        // Arrange
        await using var dbContext = CreateDbContext();
        var mockTokenService = new Mock<ITokenService>();

        var rawPassword = "Password123!";
        var hashedPassword = BCrypt.Net.BCrypt.HashPassword(rawPassword);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "inactive@example.com",
            Username = "inactiveuser",
            PasswordHash = hashedPassword,
            IsActive = false // User is inactive
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var authService = new AuthService(dbContext, mockTokenService.Object, NullLogger<AuthService>.Instance);
        var command = new LoginCommand(user.Email, rawPassword);

        // Act & Assert
        await Assert.ThrowsAsync<AuthenticationException>(() => authService.LoginAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task LoginAsync_ThrowsAuthenticationException_WhenPasswordIsInvalid()
    {
        // Arrange
        await using var dbContext = CreateDbContext();
        var mockTokenService = new Mock<ITokenService>();

        var rawPassword = "Password123!";
        var hashedPassword = BCrypt.Net.BCrypt.HashPassword(rawPassword);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            Username = "testuser",
            PasswordHash = hashedPassword,
            IsActive = true
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var authService = new AuthService(dbContext, mockTokenService.Object, NullLogger<AuthService>.Instance);
        var command = new LoginCommand(user.Email, "WrongPassword456!");

        // Act & Assert
        await Assert.ThrowsAsync<AuthenticationException>(() => authService.LoginAsync(command, CancellationToken.None));
    }
}