using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using ClaudeBatchServer.Core.DTOs;
using ClaudeBatchServer.Core.Models;
using Fasterlimit.Yescrypt;

namespace ClaudeBatchServer.Core.Services;

public class ShadowFileAuthenticationService : IAuthenticationService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ShadowFileAuthenticationService> _logger;
    private readonly SymmetricSecurityKey _signingKey;
    private readonly int _jwtExpiryHours;
    private readonly string _shadowFilePath;
    private readonly string _passwdFilePath;
    private readonly HashSet<string> _revokedTokens = new();

    public ShadowFileAuthenticationService(IConfiguration configuration, ILogger<ShadowFileAuthenticationService> logger, SymmetricSecurityKey signingKey)
    {
        _configuration = configuration;
        _logger = logger;
        _signingKey = signingKey;
        _jwtExpiryHours = int.Parse(_configuration["Jwt:ExpiryHours"] ?? "24");
        _shadowFilePath = ExpandPath(_configuration["Auth:ShadowFilePath"] ?? "/etc/shadow");
        _passwdFilePath = ExpandPath(_configuration["Auth:PasswdFilePath"] ?? "/etc/passwd");
    }

    public async Task<LoginResponse?> AuthenticateAsync(LoginRequest request)
    {
        try
        {
            var user = await ValidateUserCredentialsAsync(request.Username, request.Password);
            if (user == null) return null;

            var token = GenerateJwtToken(user);
            var expiry = DateTime.UtcNow.AddHours(_jwtExpiryHours);

            return new LoginResponse
            {
                Token = token,
                Username = user.Username,
                Expires = expiry
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication failed for user request");
            return null;
        }
    }

    public Task<bool> ValidateTokenAsync(string token)
    {
        try
        {
            if (_revokedTokens.Contains(token))
                return Task.FromResult(false);

            var tokenHandler = new JwtSecurityTokenHandler();
            
            tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = _signingKey,
                ValidateIssuer = false,
                ValidateAudience = false,
                ClockSkew = TimeSpan.Zero
            }, out SecurityToken validatedToken);

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token validation failed");
            return Task.FromResult(false);
        }
    }

    public Task<User?> GetUserFromTokenAsync(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var jwtToken = tokenHandler.ReadJwtToken(token);
            
            var username = jwtToken.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;
            if (string.IsNullOrEmpty(username)) return Task.FromResult<User?>(null);

            return Task.FromResult<User?>(new User { Username = username });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user from token");
            return Task.FromResult<User?>(null);
        }
    }

    public Task<bool> LogoutAsync(string token)
    {
        _revokedTokens.Add(token);
        return Task.FromResult(true);
    }

    private async Task<User?> ValidateUserCredentialsAsync(string username, string password)
    {
        try
        {
            if (!await UserExistsAsync(username)) return null;
            
            var shadowEntry = await ReadShadowEntryAsync(username);
            if (shadowEntry == null) return null;

            bool isValid;
            
            // Check if password is already a hash (hybrid authentication)
            if (IsPrecomputedHash(password))
            {
                // Direct hash comparison for pre-computed hashes
                isValid = VerifyPrecomputedHash(password, shadowEntry);
            }
            else
            {
                // Traditional password verification for plaintext
                isValid = VerifyPassword(password, shadowEntry);
            }
            
            if (!isValid) return null;

            return new User
            {
                Username = username,
                IsActive = true,
                LastLogin = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "User credential validation failed");
            return null;
        }
    }

    private async Task<bool> UserExistsAsync(string username)
    {
        try
        {
            var passwdContent = await File.ReadAllTextAsync(_passwdFilePath);
            return passwdContent.Split('\n')
                .Any(line => line.StartsWith($"{username}:"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check if user exists in passwd file");
            return false;
        }
    }

    private async Task<string?> ReadShadowEntryAsync(string username)
    {
        try
        {
            var shadowContent = await File.ReadAllTextAsync(_shadowFilePath);
            var shadowLine = shadowContent.Split('\n')
                .FirstOrDefault(line => line.StartsWith($"{username}:"));
            
            return shadowLine?.Split(':')[1];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read shadow file entry for user {Username}", username);
            return null;
        }
    }

    private bool VerifyPassword(string password, string hash)
    {
        if (string.IsNullOrEmpty(hash) || hash == "*" || hash == "!")
            return false;

        var parts = hash.Split('$');
        if (parts.Length < 4) return false;

        var algorithm = parts[1];

        return algorithm switch
        {
            "1" => VerifyMD5Hash(password, parts[2], parts[3]),
            "5" => VerifySHA256Hash(password, parts[2], parts[3]),
            "6" => VerifySHA512HashWithRounds(password, hash),
            "y" => VerifyYescryptHash(password, hash),
            _ => HandleUnsupportedAlgorithm(algorithm)
        };
    }

    private bool HandleUnsupportedAlgorithm(string algorithm)
    {
        _logger.LogWarning("Unsupported password hashing algorithm: '{Algorithm}'. Supported algorithms: MD5 ($1$), SHA-256 ($5$), SHA-512 ($6$), yescrypt ($y$)", algorithm);
        return false;
    }

    private bool VerifyMD5Hash(string password, string salt, string expectedHash)
    {
        using var md5 = MD5.Create();
        var input = Encoding.UTF8.GetBytes(password + salt);
        var hash = md5.ComputeHash(input);
        var computedHash = Convert.ToBase64String(hash).TrimEnd('=');
        return computedHash == expectedHash;
    }

    private bool VerifySHA256Hash(string password, string salt, string expectedHash)
    {
        using var sha256 = SHA256.Create();
        var input = Encoding.UTF8.GetBytes(password + salt);
        var hash = sha256.ComputeHash(input);
        var computedHash = Convert.ToBase64String(hash).TrimEnd('=');
        return computedHash == expectedHash;
    }

    private bool VerifySHA512HashWithRounds(string password, string completeHash)
    {
        // Use system crypt via Python with complete hash for verification
        // This handles all SHA-512 variants including rounds= parameter
        try
        {
            _logger.LogDebug("SHA-512 verification: password='{Password}', hash='{Hash}'", password, completeHash);
            
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "python3",
                    Arguments = $"-c \"import crypt; print(crypt.crypt('{password}', '{completeHash}') == '{completeHash}', end='')\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            
            process.Start();
            var result = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            
            _logger.LogDebug("SHA-512 verification: exitCode={ExitCode}, result='{Result}', error='{Error}'", 
                process.ExitCode, result, error);
            
            if (process.ExitCode == 0 && !string.IsNullOrEmpty(result))
            {
                var isMatch = result.Trim().ToLower() == "true";
                _logger.LogDebug("SHA-512 verification final result: {IsMatch}", isMatch);
                return isMatch;
            }
            
            _logger.LogWarning("SHA-512 verification failed: exitCode={ExitCode}, result='{Result}', error='{Error}'",
                process.ExitCode, result, error);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SHA-512 password verification failed");
            return false;
        }
    }

    private bool VerifySHA512Hash(string password, string salt, string expectedHash)
    {
        // Legacy method for simple SHA-512 hashes without rounds
        // Use system crypt via Python with complete hash for verification
        try
        {
            // Reconstruct the complete hash from parts
            var completeHash = $"$6${salt}${expectedHash}";
            
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "python3",
                    Arguments = $"-c \"import crypt; print(crypt.crypt('{password}', '{completeHash}') == '{completeHash}', end='')\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            
            process.Start();
            var result = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            
            if (process.ExitCode == 0 && !string.IsNullOrEmpty(result))
            {
                return result.Trim().ToLower() == "true";
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SHA-512 password verification failed");
            return false;
        }
    }

    private bool VerifyYescryptHash(string password, string hash)
    {
        // Use the yescrypt library to verify the password
        try
        {
            byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
            return Yescrypt.CheckPasswd(passwordBytes, hash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Yescrypt password verification failed");
            return false;
        }
    }

    /// <summary>
    /// Detects if the provided password is already a pre-computed hash in shadow file format
    /// </summary>
    private bool IsPrecomputedHash(string password)
    {
        // Shadow file hash format: $algorithm$salt$hash
        // Valid algorithms: $1$ (MD5), $5$ (SHA-256), $6$ (SHA-512), $y$ (yescrypt)
        if (!password.StartsWith("$")) return false;
        
        var parts = password.Split('$');
        if (parts.Length < 4) return false;
        
        // Check if algorithm is supported
        var algorithm = parts[1];
        return algorithm is "1" or "5" or "6" or "y";
    }

    /// <summary>
    /// Verifies a pre-computed hash directly against the shadow file entry
    /// </summary>
    private bool VerifyPrecomputedHash(string providedHash, string shadowHash)
    {
        // For pre-computed hashes, we compare them directly
        // This allows clients to authenticate using the exact hash from shadow file
        return string.Equals(providedHash, shadowHash, StringComparison.Ordinal);
    }

    private string GenerateJwtToken(User user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("unique_name", user.Username), // Use unique_name to match validation config
                new Claim(ClaimTypes.NameIdentifier, user.Username)
            }),
            Expires = DateTime.UtcNow.AddHours(_jwtExpiryHours),
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256Signature)
        };
        
        var token = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(token);
        
        // Log JWT details for debugging
        System.Console.WriteLine($"[DEBUG] Generated JWT with signing key KeyId: {_signingKey.KeyId}");
        System.Console.WriteLine($"[DEBUG] Token: {tokenString}");
        System.Console.WriteLine($"[DEBUG] Key for validation: {Convert.ToBase64String(_signingKey.Key)}");
        
        return tokenString;
    }

    /// <summary>
    /// Expand ~ to the user's home directory if the path starts with ~/
    /// </summary>
    private static string ExpandPath(string path)
    {
        if (path.StartsWith("~/"))
        {
            var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(homeDirectory, path[2..]);
        }
        return path;
    }
}