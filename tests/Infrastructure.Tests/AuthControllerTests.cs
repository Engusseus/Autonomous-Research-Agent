using AutonomousResearchAgent.Api.Contracts.Auth;
using AutonomousResearchAgent.Api.Controllers;
using AutonomousResearchAgent.Application.Auth;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Infrastructure.Tests;

public sealed class AuthControllerTests
{
    private readonly Mock<IAuthService> _authServiceMock;
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        _authServiceMock = new Mock<IAuthService>();
        _controller = new AuthController(_authServiceMock.Object);
    }

    [Fact]
    public async Task Login_ReturnsOk_WithAuthResponse()
    {
        // Arrange
        var request = new LoginRequest("test@test.com", "password");
        var userDto = new UserDto(Guid.NewGuid(), "test@test.com", "testuser", Array.Empty<string>());
        var authResult = new AuthResult("access-token", "refresh-token", DateTimeOffset.UtcNow.AddHours(1), userDto);

        _authServiceMock
            .Setup(s => s.LoginAsync(It.IsAny<LoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(authResult);

        // Act
        var result = await _controller.Login(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AuthResponse>(okResult.Value);
        Assert.Equal(authResult.AccessToken, response.AccessToken);
        Assert.Equal(authResult.RefreshToken, response.RefreshToken);
        Assert.Equal(authResult.ExpiresAt, response.ExpiresAt);
        Assert.Equal(userDto.Id, response.User.Id);
        Assert.Equal(userDto.Email, response.User.Email);
        Assert.Equal(userDto.Username, response.User.Username);
    }

    [Fact]
    public async Task Register_ReturnsCreated_WithAuthResponse()
    {
        // Arrange
        var request = new RegisterRequest("test@test.com", "testuser", "password");
        var userDto = new UserDto(Guid.NewGuid(), "test@test.com", "testuser", Array.Empty<string>());
        var authResult = new AuthResult("access-token", "refresh-token", DateTimeOffset.UtcNow.AddHours(1), userDto);

        _authServiceMock
            .Setup(s => s.RegisterAsync(It.IsAny<RegisterCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(authResult);

        // Act
        var result = await _controller.Register(request, CancellationToken.None);

        // Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(AuthController.Login), createdResult.ActionName);
        var response = Assert.IsType<AuthResponse>(createdResult.Value);
        Assert.Equal(authResult.AccessToken, response.AccessToken);
        Assert.Equal(authResult.RefreshToken, response.RefreshToken);
    }

    [Fact]
    public async Task RefreshToken_ReturnsOk_WithAuthResponse()
    {
        // Arrange
        var request = new TokenRefreshRequest("old-refresh-token");
        var userDto = new UserDto(Guid.NewGuid(), "test@test.com", "testuser", Array.Empty<string>());
        var authResult = new AuthResult("new-access-token", "new-refresh-token", DateTimeOffset.UtcNow.AddHours(1), userDto);

        _authServiceMock
            .Setup(s => s.RefreshTokenAsync(It.IsAny<TokenRefreshCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(authResult);

        // Act
        var result = await _controller.RefreshToken(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AuthResponse>(okResult.Value);
        Assert.Equal(authResult.AccessToken, response.AccessToken);
        Assert.Equal(authResult.RefreshToken, response.RefreshToken);
    }
}
