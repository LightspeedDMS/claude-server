namespace ClaudeServerCLI.Services;

public interface IAuthService
{
    /// <summary>
    /// Authenticates with the server and stores the token securely
    /// </summary>
    /// <param name="username">Username</param>
    /// <param name="password">Password (plain text or hashed)</param>
    /// <param name="isHashedPassword">Whether the password is already hashed</param>
    /// <param name="profile">Profile name to store token under</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if authentication was successful</returns>
    Task<bool> LoginAsync(string username, string password, bool isHashedPassword = false, 
        string profile = "default", CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs out and clears stored token for the profile
    /// </summary>
    /// <param name="profile">Profile name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if logout was successful</returns>
    Task<bool> LogoutAsync(string profile = "default", CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current user information for the profile
    /// </summary>
    /// <param name="profile">Profile name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Username if authenticated, null otherwise</returns>
    Task<string?> GetCurrentUserAsync(string profile = "default", CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a valid JWT token for the profile
    /// </summary>
    /// <param name="profile">Profile name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Valid JWT token if available, null otherwise</returns>
    Task<string?> GetTokenAsync(string profile = "default", CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the user is authenticated for the profile
    /// </summary>
    /// <param name="profile">Profile name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if authenticated with valid token</returns>
    Task<bool> IsAuthenticatedAsync(string profile = "default", CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes the token if it's expired or about to expire
    /// </summary>
    /// <param name="profile">Profile name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if token was refreshed successfully</returns>
    Task<bool> RefreshTokenAsync(string profile = "default", CancellationToken cancellationToken = default);
}