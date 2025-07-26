namespace ClaudeServerCLI.Models;

/// <summary>
/// Represents the result of a user management operation
/// </summary>
public class UserOperationResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? ErrorDetails { get; set; }
    public string? BackupFile { get; set; }
    
    public static UserOperationResult SuccessResult(string message, string? backupFile = null)
    {
        return new UserOperationResult
        {
            Success = true,
            Message = message,
            BackupFile = backupFile
        };
    }
    
    public static UserOperationResult ErrorResult(string message, string? errorDetails = null)
    {
        return new UserOperationResult
        {
            Success = false,
            Message = message,
            ErrorDetails = errorDetails
        };
    }
}

/// <summary>
/// Represents a user in the authentication system
/// </summary>
public class UserInfo
{
    public string Username { get; set; } = string.Empty;
    public int Uid { get; set; }
    public int Gid { get; set; }
    public string Gecos { get; set; } = string.Empty;
    public string HomeDirectory { get; set; } = string.Empty;
    public string Shell { get; set; } = string.Empty;
    public DateTime? LastPasswordChange { get; set; }
    public bool HasPassword { get; set; }
    public UserStatus Status { get; set; }
}

/// <summary>
/// Represents the status of a user account
/// </summary>
public enum UserStatus
{
    Active,
    NoPassword,
    NoShadowEntry,
    Locked
}

/// <summary>
/// Represents shadow file entry fields
/// </summary>
public class ShadowEntry
{
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public int LastChange { get; set; }
    public int MinAge { get; set; }
    public int MaxAge { get; set; }
    public int WarnPeriod { get; set; }
    public int InactivePeriod { get; set; }
    public int ExpireDate { get; set; }
    public string Reserved { get; set; } = string.Empty;
}

/// <summary>
/// Represents passwd file entry fields
/// </summary>
public class PasswdEntry
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = "x"; // Always "x" for shadow files
    public int Uid { get; set; }
    public int Gid { get; set; }
    public string Gecos { get; set; } = string.Empty;
    public string HomeDirectory { get; set; } = string.Empty;
    public string Shell { get; set; } = string.Empty;
}