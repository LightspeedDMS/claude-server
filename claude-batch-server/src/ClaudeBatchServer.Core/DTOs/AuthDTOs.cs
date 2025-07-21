namespace ClaudeBatchServer.Core.DTOs;

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public DateTime Expires { get; set; }
}

public class LogoutResponse
{
    public bool Success { get; set; } = true;
}