using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ClaudeServerCLI.Models;
using Microsoft.Extensions.Logging;

namespace ClaudeServerCLI.Services;

/// <summary>
/// Service for managing Claude Server authentication users in shadow files
/// Based on the bash script functionality for add-user.sh, remove-user.sh, list-users.sh, update-user.sh
/// </summary>
public class UserManagementService : IUserManagementService
{
    private readonly ILogger<UserManagementService> _logger;
    private readonly string _serverDirectory;
    private readonly string _passwdFile;
    private readonly string _shadowFile;
    
    // Username validation regex - must start with letter, 3-32 chars, alphanumeric + underscore/dash
    private static readonly Regex UsernameRegex = new(@"^[a-zA-Z][a-zA-Z0-9_-]{2,31}$", RegexOptions.Compiled);
    
    // Default values from bash scripts
    private const int DefaultUid = 1000;
    private const int DefaultGid = 1000;
    private const string DefaultShell = "/bin/bash";
    
    public UserManagementService(ILogger<UserManagementService> logger)
    {
        _logger = logger;
        
        // First try current working directory (for CLI usage and tests)
        var workingDir = Directory.GetCurrentDirectory();
        if (File.Exists(Path.Combine(workingDir, "claude-server-passwd")) || 
            File.Exists(Path.Combine(workingDir, "claude-server-shadow")))
        {
            _serverDirectory = workingDir;
        }
        else
        {
            // Fallback: Determine server directory from app base directory (works with single-file apps)
            var assemblyDir = AppContext.BaseDirectory ?? throw new InvalidOperationException("Cannot determine application directory");
            
            // Navigate up to find the project root (where the auth files should be)
            var currentDir = new DirectoryInfo(assemblyDir);
            while (currentDir != null && !File.Exists(Path.Combine(currentDir.FullName, "claude-server-passwd")) && 
                   !File.Exists(Path.Combine(currentDir.FullName, "claude-server-shadow")))
            {
                currentDir = currentDir.Parent;
            }
            
            // If not found, use current working directory anyway
            _serverDirectory = currentDir?.FullName ?? workingDir;
        }
        
        _passwdFile = Path.Combine(_serverDirectory, "claude-server-passwd");
        _shadowFile = Path.Combine(_serverDirectory, "claude-server-shadow");
        
        _logger.LogDebug("UserManagementService initialized with server directory: {ServerDirectory}", _serverDirectory);
    }

    public async Task<UserOperationResult> AddUserAsync(string username, string password, int? uid = null, int? gid = null, string? homeDir = null, string? shell = null)
    {
        try
        {
            // Validate username
            if (!IsValidUsername(username))
            {
                return UserOperationResult.ErrorResult("Invalid username format. Must start with letter, 3-32 chars, alphanumeric + underscore/dash only");
            }

            // Check if user already exists
            if (await UserExistsAsync(username))
            {
                return UserOperationResult.ErrorResult($"User '{username}' already exists. Use update-user command to modify.");
            }

            // Set defaults
            var actualUid = uid ?? DefaultUid;
            var actualGid = gid ?? DefaultGid;
            var actualHomeDir = homeDir ?? Path.Combine("/home", username);
            var actualShell = shell ?? DefaultShell;

            // Generate password hash
            var hashResult = await GeneratePasswordHashAsync(password);
            if (!hashResult.Success)
            {
                return UserOperationResult.ErrorResult($"Failed to generate password hash: {hashResult.Message}");
            }

            // Create backups
            string? backupSuffix = null;
            if (File.Exists(_passwdFile) || File.Exists(_shadowFile))
            {
                backupSuffix = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                await CreateBackupsAsync(backupSuffix);
            }

            // Add user to passwd file
            var passwdEntry = new PasswdEntry
            {
                Username = username,
                Password = "x",
                Uid = actualUid,
                Gid = actualGid,
                Gecos = $"{username} User",
                HomeDirectory = actualHomeDir,
                Shell = actualShell
            };

            await AppendToFileAsync(_passwdFile, FormatPasswdEntry(passwdEntry));

            // Add user to shadow file
            var daysSinceEpoch = (int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalDays;
            var shadowEntry = new ShadowEntry
            {
                Username = username,
                PasswordHash = hashResult.Message!,
                LastChange = daysSinceEpoch,
                MinAge = 0,
                MaxAge = 99999,
                WarnPeriod = 7,
                InactivePeriod = 0,
                ExpireDate = 0,
                Reserved = ""
            };

            await AppendToFileAsync(_shadowFile, FormatShadowEntry(shadowEntry));

            _logger.LogInformation("User '{Username}' successfully added with UID {Uid}", username, actualUid);
            
            return UserOperationResult.SuccessResult(
                $"User '{username}' successfully added to Claude Server authentication!",
                backupSuffix != null ? $"Backups created with suffix: {backupSuffix}" : null
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add user '{Username}'", username);
            return UserOperationResult.ErrorResult($"Failed to add user: {ex.Message}", ex.ToString());
        }
    }

    public async Task<UserOperationResult> RemoveUserAsync(string username)
    {
        try
        {
            // Check if files exist
            if (!File.Exists(_passwdFile))
            {
                return UserOperationResult.ErrorResult($"Password file not found: {_passwdFile}");
            }

            if (!File.Exists(_shadowFile))
            {
                return UserOperationResult.ErrorResult($"Shadow file not found: {_shadowFile}");
            }

            // Check if user exists
            if (!await UserExistsAsync(username))
            {
                return UserOperationResult.ErrorResult($"User '{username}' does not exist in Claude Server authentication");
            }

            // Create backups
            var backupSuffix = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            await CreateBackupsAsync(backupSuffix);

            // Remove user from passwd file
            await RemoveUserFromFileAsync(_passwdFile, username);

            // Remove user from shadow file
            await RemoveUserFromFileAsync(_shadowFile, username);

            _logger.LogInformation("User '{Username}' successfully removed", username);
            
            return UserOperationResult.SuccessResult(
                $"User '{username}' successfully removed from Claude Server authentication!",
                $"Backups created with suffix: {backupSuffix}"
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove user '{Username}'", username);
            return UserOperationResult.ErrorResult($"Failed to remove user: {ex.Message}", ex.ToString());
        }
    }

    public async Task<UserOperationResult> UpdateUserPasswordAsync(string username, string newPassword)
    {
        try
        {
            // Check if files exist
            if (!File.Exists(_passwdFile))
            {
                return UserOperationResult.ErrorResult($"Password file not found: {_passwdFile}");
            }

            if (!File.Exists(_shadowFile))
            {
                return UserOperationResult.ErrorResult($"Shadow file not found: {_shadowFile}");
            }

            // Check if user exists
            if (!await UserExistsAsync(username))
            {
                return UserOperationResult.ErrorResult($"User '{username}' does not exist in Claude Server authentication. Use add-user command to create the user first");
            }

            // Generate new password hash
            var hashResult = await GeneratePasswordHashAsync(newPassword);
            if (!hashResult.Success)
            {
                return UserOperationResult.ErrorResult($"Failed to generate password hash: {hashResult.Message}");
            }

            // Create backup
            var backupSuffix = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            File.Copy(_shadowFile, $"{_shadowFile}.backup.{backupSuffix}");

            // Update password in shadow file
            await UpdateUserPasswordInShadowFileAsync(username, hashResult.Message!);

            _logger.LogInformation("Password for user '{Username}' successfully updated", username);
            
            return UserOperationResult.SuccessResult(
                $"Password for user '{username}' successfully updated!",
                $"Backup created: claude-server-shadow.backup.{backupSuffix}"
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update password for user '{Username}'", username);
            return UserOperationResult.ErrorResult($"Failed to update password: {ex.Message}", ex.ToString());
        }
    }

    public async Task<IEnumerable<UserInfo>> ListUsersAsync()
    {
        var users = new List<UserInfo>();

        try
        {
            if (!File.Exists(_passwdFile))
            {
                _logger.LogWarning("Password file not found: {PasswdFile}", _passwdFile);
                return users;
            }

            if (!File.Exists(_shadowFile))
            {
                _logger.LogWarning("Shadow file not found: {ShadowFile}", _shadowFile);
                return users;
            }

            // Read shadow entries for password info
            var shadowEntries = new Dictionary<string, ShadowEntry>();
            var shadowLines = await File.ReadAllLinesAsync(_shadowFile);
            foreach (var line in shadowLines.Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                var entry = ParseShadowEntry(line);
                if (entry != null)
                {
                    shadowEntries[entry.Username] = entry;
                }
            }

            // Read passwd entries and combine with shadow info
            var passwdLines = await File.ReadAllLinesAsync(_passwdFile);
            foreach (var line in passwdLines.Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                var passwdEntry = ParsePasswdEntry(line);
                if (passwdEntry != null)
                {
                    var userInfo = new UserInfo
                    {
                        Username = passwdEntry.Username,
                        Uid = passwdEntry.Uid,
                        Gid = passwdEntry.Gid,
                        Gecos = passwdEntry.Gecos,
                        HomeDirectory = passwdEntry.HomeDirectory,
                        Shell = passwdEntry.Shell
                    };

                    // Add shadow information if available
                    if (shadowEntries.TryGetValue(passwdEntry.Username, out var shadowEntry))
                    {
                        userInfo.LastPasswordChange = ConvertDaysToDateTime(shadowEntry.LastChange);
                        userInfo.HasPassword = !string.IsNullOrEmpty(shadowEntry.PasswordHash) && 
                                             shadowEntry.PasswordHash != "*" && 
                                             shadowEntry.PasswordHash != "!";
                        userInfo.Status = userInfo.HasPassword ? UserStatus.Active : UserStatus.NoPassword;
                    }
                    else
                    {
                        userInfo.Status = UserStatus.NoShadowEntry;
                        userInfo.HasPassword = false;
                    }

                    users.Add(userInfo);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list users");
        }

        return users.OrderBy(u => u.Username);
    }

    public async Task<bool> UserExistsAsync(string username)
    {
        try
        {
            if (!File.Exists(_passwdFile))
            {
                return false;
            }

            var lines = await File.ReadAllLinesAsync(_passwdFile);
            return lines.Any(line => line.StartsWith($"{username}:"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check if user '{Username}' exists", username);
            return false;
        }
    }

    public bool IsValidUsername(string username)
    {
        return !string.IsNullOrEmpty(username) && UsernameRegex.IsMatch(username);
    }

    public string GetPasswdFilePath() => _passwdFile;
    public string GetShadowFilePath() => _shadowFile;

    #region Private Methods

    private async Task<UserOperationResult> GeneratePasswordHashAsync(string password)
    {
        try
        {
            // Generate salt
            var salt = GenerateSalt();
            
            // Try different hashing methods in order of preference (same as bash scripts)
            
            // Method 1: Try mkpasswd if available
            var mkpasswdResult = await TryMkpasswdAsync(password, salt);
            if (mkpasswdResult.Success)
            {
                return mkpasswdResult;
            }

            // Method 2: Try Python3 crypt if available  
            var pythonResult = await TryPythonCryptAsync(password, salt);
            if (pythonResult.Success)
            {
                return pythonResult;
            }

            // Method 3: Use C# BCrypt-like implementation (fallback)
            var csharpResult = await TryCSharpHashAsync(password, salt);
            if (csharpResult.Success)
            {
                return csharpResult;
            }

            return UserOperationResult.ErrorResult("Failed to generate password hash with any available method");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating password hash");
            return UserOperationResult.ErrorResult($"Error generating password hash: {ex.Message}");
        }
    }

    private string GenerateSalt()
    {
        // Generate 16-character salt similar to bash script
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[12];
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace("=", "").Replace("+", "").Replace("/", "")[..16];
    }

    private async Task<UserOperationResult> TryMkpasswdAsync(string password, string salt)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "mkpasswd",
                    Arguments = $"-m sha-512 -S {salt}",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.StandardInput.WriteLineAsync(password);
            process.StandardInput.Close();

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                return UserOperationResult.SuccessResult(output.Trim());
            }

            _logger.LogDebug("mkpasswd failed with exit code {ExitCode}: {Error}", process.ExitCode, error);
            return UserOperationResult.ErrorResult("mkpasswd failed");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "mkpasswd not available");
            return UserOperationResult.ErrorResult("mkpasswd not available");
        }
    }

    private async Task<UserOperationResult> TryPythonCryptAsync(string password, string salt)
    {
        try
        {
            var pythonScript = $@"
import crypt, sys
try:
    result = crypt.crypt('{password}', '$6${salt}$')
    print(result)
except:
    sys.exit(1)
";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "python3",
                    Arguments = "-c",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.StandardInput.WriteLineAsync(pythonScript);
            process.StandardInput.Close();

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                return UserOperationResult.SuccessResult(output.Trim());
            }

            _logger.LogDebug("Python3 crypt failed with exit code {ExitCode}: {Error}", process.ExitCode, error);
            return UserOperationResult.ErrorResult("Python3 crypt failed");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Python3 not available");
            return UserOperationResult.ErrorResult("Python3 not available");
        }
    }

    private async Task<UserOperationResult> TryCSharpHashAsync(string password, string salt)
    {
        try
        {
            // Simplified SHA-512 based implementation (similar to bash script fallback)
            using var sha512 = SHA512.Create();
            var combined = Encoding.UTF8.GetBytes(password + salt);
            var hash = sha512.ComputeHash(combined);
            var base64Hash = Convert.ToBase64String(hash).Replace("=", "").Replace("+", "").Replace("/", "");
            
            // Take up to 86 characters, but don't fail if shorter
            var hashPart = base64Hash.Length >= 86 ? base64Hash[..86] : base64Hash;
            var result = $"$6${salt}${hashPart}";
            
            await Task.CompletedTask; // Make it async for consistency
            return UserOperationResult.SuccessResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "C# hash generation failed");
            return UserOperationResult.ErrorResult($"C# hash generation failed: {ex.Message}");
        }
    }

    private async Task CreateBackupsAsync(string suffix)
    {
        try
        {
            if (File.Exists(_passwdFile))
            {
                File.Copy(_passwdFile, $"{_passwdFile}.backup.{suffix}");
                _logger.LogDebug("Created backup: {BackupFile}", $"claude-server-passwd.backup.{suffix}");
            }

            if (File.Exists(_shadowFile))
            {
                File.Copy(_shadowFile, $"{_shadowFile}.backup.{suffix}");
                _logger.LogDebug("Created backup: {BackupFile}", $"claude-server-shadow.backup.{suffix}");
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create backups");
            throw;
        }
    }

    private async Task AppendToFileAsync(string filePath, string content)
    {
        await File.AppendAllTextAsync(filePath, content + Environment.NewLine);
    }

    private async Task RemoveUserFromFileAsync(string filePath, string username)
    {
        var lines = await File.ReadAllLinesAsync(filePath);
        var filteredLines = lines.Where(line => !line.StartsWith($"{username}:")).ToArray();
        await File.WriteAllLinesAsync(filePath, filteredLines);
    }

    private async Task UpdateUserPasswordInShadowFileAsync(string username, string newHash)
    {
        var lines = await File.ReadAllLinesAsync(_shadowFile);
        var daysSinceEpoch = (int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalDays;

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith($"{username}:"))
            {
                var parts = lines[i].Split(':');
                if (parts.Length >= 9)
                {
                    // Update password hash and last change date, preserve other fields
                    parts[1] = newHash;
                    parts[2] = daysSinceEpoch.ToString();
                    lines[i] = string.Join(":", parts);
                }
                else
                {
                    // Create new entry with defaults
                    lines[i] = $"{username}:{newHash}:{daysSinceEpoch}:0:99999:7:::";
                }
                break;
            }
        }

        await File.WriteAllLinesAsync(_shadowFile, lines);
    }

    private string FormatPasswdEntry(PasswdEntry entry)
    {
        return $"{entry.Username}:{entry.Password}:{entry.Uid}:{entry.Gid}:{entry.Gecos}:{entry.HomeDirectory}:{entry.Shell}";
    }

    private string FormatShadowEntry(ShadowEntry entry)
    {
        return $"{entry.Username}:{entry.PasswordHash}:{entry.LastChange}:{entry.MinAge}:{entry.MaxAge}:{entry.WarnPeriod}:{entry.InactivePeriod}:{entry.ExpireDate}:{entry.Reserved}";
    }

    private PasswdEntry? ParsePasswdEntry(string line)
    {
        var parts = line.Split(':');
        if (parts.Length < 7) return null;

        return new PasswdEntry
        {
            Username = parts[0],
            Password = parts[1],
            Uid = int.TryParse(parts[2], out var uid) ? uid : 0,
            Gid = int.TryParse(parts[3], out var gid) ? gid : 0,
            Gecos = parts[4],
            HomeDirectory = parts[5],
            Shell = parts[6]
        };
    }

    private ShadowEntry? ParseShadowEntry(string line)
    {
        var parts = line.Split(':');
        if (parts.Length < 9) return null;

        return new ShadowEntry
        {
            Username = parts[0],
            PasswordHash = parts[1],
            LastChange = int.TryParse(parts[2], out var lastChange) ? lastChange : 0,
            MinAge = int.TryParse(parts[3], out var minAge) ? minAge : 0,
            MaxAge = int.TryParse(parts[4], out var maxAge) ? maxAge : 99999,
            WarnPeriod = int.TryParse(parts[5], out var warn) ? warn : 7,
            InactivePeriod = int.TryParse(parts[6], out var inactive) ? inactive : 0,
            ExpireDate = int.TryParse(parts[7], out var expire) ? expire : 0,
            Reserved = parts[8]
        };
    }

    private DateTime? ConvertDaysToDateTime(int daysSinceEpoch)
    {
        if (daysSinceEpoch <= 0) return null;
        
        try
        {
            return new DateTime(1970, 1, 1).AddDays(daysSinceEpoch);
        }
        catch
        {
            return null;
        }
    }

    #endregion
}