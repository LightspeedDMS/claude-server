using ClaudeBatchServer.Core.DTOs;
using ClaudeBatchServer.Core.Models;

namespace ClaudeBatchServer.Core.Services;

public interface IAuthenticationService
{
    Task<LoginResponse?> AuthenticateAsync(LoginRequest request);
    Task<bool> ValidateTokenAsync(string token);
    Task<User?> GetUserFromTokenAsync(string token);
    Task<bool> LogoutAsync(string token);
}