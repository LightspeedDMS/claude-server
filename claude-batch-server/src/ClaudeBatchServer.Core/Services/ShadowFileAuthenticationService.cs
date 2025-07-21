using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using ClaudeBatchServer.Core.DTOs;
using ClaudeBatchServer.Core.Models;

namespace ClaudeBatchServer.Core.Services;

public class ShadowFileAuthenticationService : IAuthenticationService
{
    private readonly IConfiguration _configuration;
    private readonly string _jwtKey;
    private readonly int _jwtExpiryHours;
    private readonly HashSet<string> _revokedTokens = new();

    public ShadowFileAuthenticationService(IConfiguration configuration)
    {
        _configuration = configuration;
        _jwtKey = _configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT key not configured");
        _jwtExpiryHours = int.Parse(_configuration["Jwt:ExpiryHours"] ?? "24");
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
                User = user.Username,
                Expires = expiry
            };
        }
        catch
        {
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
            var key = Encoding.ASCII.GetBytes(_jwtKey);
            
            tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = false,
                ValidateAudience = false,
                ClockSkew = TimeSpan.Zero
            }, out SecurityToken validatedToken);

            return Task.FromResult(true);
        }
        catch
        {
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
        catch
        {
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
        catch
        {
            return null;
        }
    }

    private async Task<bool> UserExistsAsync(string username)
    {
        try
        {
            var passwdContent = await File.ReadAllTextAsync("/etc/passwd");
            return passwdContent.Split('\n')
                .Any(line => line.StartsWith($"{username}:"));
        }
        catch
        {
            return false;
        }
    }

    private async Task<string?> ReadShadowEntryAsync(string username)
    {
        try
        {
            var shadowContent = await File.ReadAllTextAsync("/etc/shadow");
            var shadowLine = shadowContent.Split('\n')
                .FirstOrDefault(line => line.StartsWith($"{username}:"));
            
            return shadowLine?.Split(':')[1];
        }
        catch
        {
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
        var salt = parts[2];
        var expectedHash = parts[3];

        var computedHash = algorithm switch
        {
            "1" => ComputeMD5Hash(password, salt),
            "5" => ComputeSHA256Hash(password, salt),
            "6" => ComputeSHA512Hash(password, salt),
            _ => null
        };

        return computedHash == expectedHash;
    }

    private string ComputeMD5Hash(string password, string salt)
    {
        using var md5 = MD5.Create();
        var input = Encoding.UTF8.GetBytes(password + salt);
        var hash = md5.ComputeHash(input);
        return Convert.ToBase64String(hash).TrimEnd('=');
    }

    private string ComputeSHA256Hash(string password, string salt)
    {
        using var sha256 = SHA256.Create();
        var input = Encoding.UTF8.GetBytes(password + salt);
        var hash = sha256.ComputeHash(input);
        return Convert.ToBase64String(hash).TrimEnd('=');
    }

    private string ComputeSHA512Hash(string password, string salt)
    {
        using var sha512 = SHA512.Create();
        var input = Encoding.UTF8.GetBytes(password + salt);
        var hash = sha512.ComputeHash(input);
        return Convert.ToBase64String(hash).TrimEnd('=');
    }

    /// <summary>
    /// Detects if the provided password is already a pre-computed hash in shadow file format
    /// </summary>
    private bool IsPrecomputedHash(string password)
    {
        // Shadow file hash format: $algorithm$salt$hash
        // Valid algorithms: $1$ (MD5), $5$ (SHA-256), $6$ (SHA-512)
        if (!password.StartsWith("$")) return false;
        
        var parts = password.Split('$');
        if (parts.Length < 4) return false;
        
        // Check if algorithm is supported
        var algorithm = parts[1];
        return algorithm is "1" or "5" or "6";
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
        var key = Encoding.ASCII.GetBytes(_jwtKey);
        
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.NameIdentifier, user.Username)
            }),
            Expires = DateTime.UtcNow.AddHours(_jwtExpiryHours),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}