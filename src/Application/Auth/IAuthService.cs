namespace AutonomousResearchAgent.Application.Auth;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(LoginCommand command, CancellationToken cancellationToken);
    Task<AuthResult> RegisterAsync(RegisterCommand command, CancellationToken cancellationToken);
    Task<AuthResult> RefreshTokenAsync(TokenRefreshCommand command, CancellationToken cancellationToken);
}
