using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using ClaudeBatchServer.Core.DTOs;
using ClaudeServerCLI.Models;

namespace ClaudeServerCLI.Services;

public class AuthService : IAuthService
{
    private readonly IApiClient _apiClient;
    private readonly IConfigService _configService;
    private readonly ILogger<AuthService> _logger;
    private readonly JwtSecurityTokenHandler _jwtHandler;

    public AuthService(IApiClient apiClient, IConfigService configService, ILogger<AuthService> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jwtHandler = new JwtSecurityTokenHandler();
    }

    public async Task<bool> LoginAsync(string username, string password, bool isHashedPassword = false, string profile = "default", CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Attempting login for user: {Username} with profile: {Profile}", username, profile);
            
            var loginRequest = new LoginRequest
            {
                Username = username,
                Password = password
            };

            var response = await _apiClient.LoginAsync(loginRequest, cancellationToken);
            
            if (response != null && !string.IsNullOrEmpty(response.Token))
            {
                // Store the encrypted token in the profile
                await StoreTokenAsync(response.Token, profile, cancellationToken);
                
                _logger.LogInformation("Login successful for user: {Username}", username);
                return true;
            }
            
            _logger.LogWarning("Login failed for user: {Username} - No token received", username);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed for user: {Username}", username);
            return false;
        }
    }

    public async Task<bool> LogoutAsync(string profile = "default", CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Attempting logout for profile: {Profile}", profile);
            
            // Try to notify the server about logout
            try
            {
                await _apiClient.LogoutAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Server logout notification failed, continuing with local logout");
            }
            
            // Clear local token regardless of server response
            await ClearTokenAsync(profile, cancellationToken);
            
            _logger.LogInformation("Logout successful for profile: {Profile}", profile);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Logout failed for profile: {Profile}", profile);
            return false;
        }
    }

    public async Task<string?> GetCurrentUserAsync(string profile = "default", CancellationToken cancellationToken = default)
    {
        var token = await GetTokenAsync(profile, cancellationToken);
        if (string.IsNullOrEmpty(token))
            return null;

        try
        {
            var jwtToken = _jwtHandler.ReadJwtToken(token);
            var usernameClaim = jwtToken.Claims.FirstOrDefault(x => x.Type == ClaimTypes.Name || x.Type == "sub");
            return usernameClaim?.Value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract username from JWT token");
            return null;
        }
    }

    public async Task<string?> GetTokenAsync(string profile = "default", CancellationToken cancellationToken = default)
    {
        // 1. Check environment variable first (highest priority - great for automation)
        var envToken = Environment.GetEnvironmentVariable("CLAUDE_SERVER_TOKEN");
        if (!string.IsNullOrEmpty(envToken))
        {
            if (IsTokenValid(envToken))
            {
                _apiClient.SetAuthToken(envToken);
                return envToken;
            }
            _logger.LogWarning("Environment token is invalid or expired");
        }

        // 2. Check encrypted config file (persistent storage)
        try
        {
            var profileConfig = await _configService.GetProfileAsync(profile, cancellationToken);
            if (!string.IsNullOrEmpty(profileConfig.EncryptedToken))
            {
                var decryptedToken = DecryptToken(profileConfig.EncryptedToken);
                if (IsTokenValid(decryptedToken))
                {
                    _apiClient.SetAuthToken(decryptedToken);
                    return decryptedToken;
                }
                
                _logger.LogWarning("Stored token is invalid or expired, clearing it");
                await ClearTokenAsync(profile, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve stored token for profile: {Profile}", profile);
        }

        _apiClient.ClearAuthToken();
        return null;
    }

    public async Task<bool> IsAuthenticatedAsync(string profile = "default", CancellationToken cancellationToken = default)
    {
        var token = await GetTokenAsync(profile, cancellationToken);
        return !string.IsNullOrEmpty(token);
    }

    public async Task<bool> RefreshTokenAsync(string profile = "default", CancellationToken cancellationToken = default)
    {
        // Note: The server doesn't appear to have a refresh token endpoint
        // so we just check if the current token is still valid
        var token = await GetTokenAsync(profile, cancellationToken);
        return !string.IsNullOrEmpty(token);
    }

    private async Task StoreTokenAsync(string token, string profile, CancellationToken cancellationToken)
    {
        try
        {
            var encryptedToken = EncryptToken(token);
            var profileConfig = await _configService.GetProfileAsync(profile, cancellationToken);
            profileConfig.EncryptedToken = encryptedToken;
            await _configService.SetProfileAsync(profile, profileConfig, cancellationToken);
            
            _logger.LogDebug("Token stored securely for profile: {Profile}", profile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store token for profile: {Profile}", profile);
            throw;
        }
    }

    private async Task ClearTokenAsync(string profile, CancellationToken cancellationToken)
    {
        try
        {
            var profileConfig = await _configService.GetProfileAsync(profile, cancellationToken);
            profileConfig.EncryptedToken = null;
            await _configService.SetProfileAsync(profile, profileConfig, cancellationToken);
            
            _apiClient.ClearAuthToken();
            
            _logger.LogDebug("Token cleared for profile: {Profile}", profile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear token for profile: {Profile}", profile);
            throw;
        }
    }

    private bool IsTokenValid(string token)
    {
        try
        {
            var jwtToken = _jwtHandler.ReadJwtToken(token);
            return jwtToken.ValidTo > DateTime.UtcNow.AddMinutes(1); // 1 minute buffer
        }
        catch
        {
            return false;
        }
    }

    private string EncryptToken(string token)
    {
        var key = GetOrCreateEncryptionKey();
        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();
        
        using var encryptor = aes.CreateEncryptor();
        var tokenBytes = Encoding.UTF8.GetBytes(token);
        var encryptedBytes = encryptor.TransformFinalBlock(tokenBytes, 0, tokenBytes.Length);
        
        // Combine IV + encrypted data
        var result = new byte[aes.IV.Length + encryptedBytes.Length];
        aes.IV.CopyTo(result, 0);
        encryptedBytes.CopyTo(result, aes.IV.Length);
        
        return Convert.ToBase64String(result);
    }

    private string DecryptToken(string encryptedToken)
    {
        var key = GetOrCreateEncryptionKey();
        var data = Convert.FromBase64String(encryptedToken);
        
        using var aes = Aes.Create();
        aes.Key = key;
        
        // Extract IV
        var iv = new byte[aes.IV.Length];
        Array.Copy(data, 0, iv, 0, iv.Length);
        aes.IV = iv;
        
        // Extract encrypted data
        var encryptedBytes = new byte[data.Length - iv.Length];
        Array.Copy(data, iv.Length, encryptedBytes, 0, encryptedBytes.Length);
        
        using var decryptor = aes.CreateDecryptor();
        var decryptedBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);
        
        return Encoding.UTF8.GetString(decryptedBytes);
    }

    private byte[] GetOrCreateEncryptionKey()
    {
        // Create a machine + user specific encryption key
        var machineId = Environment.MachineName;
        var userId = Environment.UserName;
        var appId = "ClaudeServerCLI";
        
        var keyString = $"{machineId}-{userId}-{appId}";
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(Encoding.UTF8.GetBytes(keyString));
    }
}