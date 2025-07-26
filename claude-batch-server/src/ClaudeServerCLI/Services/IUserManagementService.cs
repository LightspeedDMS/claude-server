using ClaudeServerCLI.Models;

namespace ClaudeServerCLI.Services;

/// <summary>
/// Service for managing Claude Server authentication users in shadow files
/// </summary>
public interface IUserManagementService
{
    /// <summary>
    /// Adds a new user to the authentication files
    /// </summary>
    /// <param name="username">Username for the new user</param>
    /// <param name="password">Password for the new user</param>
    /// <param name="uid">User ID (defaults to 1000)</param>
    /// <param name="gid">Group ID (defaults to 1000)</param>
    /// <param name="homeDir">Home directory (defaults to /home/{username})</param>
    /// <param name="shell">Shell (defaults to /bin/bash)</param>
    /// <returns>Result of the add operation</returns>
    Task<UserOperationResult> AddUserAsync(string username, string password, int? uid = null, int? gid = null, string? homeDir = null, string? shell = null);

    /// <summary>
    /// Removes a user from the authentication files
    /// </summary>
    /// <param name="username">Username to remove</param>
    /// <returns>Result of the remove operation</returns>
    Task<UserOperationResult> RemoveUserAsync(string username);

    /// <summary>
    /// Updates a user's password
    /// </summary>
    /// <param name="username">Username to update</param>
    /// <param name="newPassword">New password</param>
    /// <returns>Result of the update operation</returns>
    Task<UserOperationResult> UpdateUserPasswordAsync(string username, string newPassword);

    /// <summary>
    /// Lists all users in the authentication files
    /// </summary>
    /// <returns>List of users with their information</returns>
    Task<IEnumerable<UserInfo>> ListUsersAsync();

    /// <summary>
    /// Checks if a user exists in the authentication files
    /// </summary>
    /// <param name="username">Username to check</param>
    /// <returns>True if user exists, false otherwise</returns>
    Task<bool> UserExistsAsync(string username);

    /// <summary>
    /// Validates username format
    /// </summary>
    /// <param name="username">Username to validate</param>
    /// <returns>True if valid, false otherwise</returns>
    bool IsValidUsername(string username);

    /// <summary>
    /// Gets the path to the passwd file
    /// </summary>
    string GetPasswdFilePath();

    /// <summary>
    /// Gets the path to the shadow file
    /// </summary>
    string GetShadowFilePath();
}