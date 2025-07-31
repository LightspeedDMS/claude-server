namespace ClaudeBatchServer.Core.DTOs;

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? AuthType { get; set; } // "plaintext" or "hash"
}

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty; // Changed from User to Username for consistency
    public DateTime Expires { get; set; }
}

public class AuthErrorResponse
{
    public string Error { get; set; } = string.Empty;
    public string ErrorType { get; set; } = string.Empty;
    public string? Details { get; set; }
}

public class LogoutRequest
{
}

public class LogoutResponse
{
    public bool Success { get; set; } = true;
}