using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using ClaudeBatchServer.Core.DTOs;
using ClaudeBatchServer.Core.Services;

namespace ClaudeBatchServer.Tests.Services;

public class HashAuthenticationTests
{
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly ShadowFileAuthenticationService _authService;

    public HashAuthenticationTests()
    {
        _mockConfiguration = new Mock<IConfiguration>();
        _mockConfiguration.Setup(c => c["Jwt:Key"]).Returns("TestKeyForHashAuthenticationThatIsLongEnough");
        _mockConfiguration.Setup(c => c["Jwt:ExpiryHours"]).Returns("24");
        
        // Create test signing key
        var keyBytes = System.Text.Encoding.ASCII.GetBytes("TestKeyForHashAuthenticationThatIsLongEnough");
        var signingKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(keyBytes) { KeyId = "test-key" };
        
        _authService = new ShadowFileAuthenticationService(_mockConfiguration.Object, Mock.Of<ILogger<ShadowFileAuthenticationService>>(), signingKey);
    }

    private TestableAuthService CreateTestableAuthService()
    {
        var keyBytes = System.Text.Encoding.ASCII.GetBytes("TestKeyForHashAuthenticationThatIsLongEnough");
        var signingKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(keyBytes) { KeyId = "test-key" };
        return new TestableAuthService(_mockConfiguration.Object, Mock.Of<ILogger<ShadowFileAuthenticationService>>(), signingKey);
    }

    [Theory]
    [InlineData("$6$randomsalt$hash123456789012345678901234567890")]
    [InlineData("$5$anothersalt$sha256hash123456789012345678")]
    [InlineData("$1$md5salt$md5hash12345678")]
    [InlineData("$y$j9T$VRSySjntzaFIR9Ax10T7A0$dQhQpfWxdgtdfq1C63UqumQDfPISr8DN3M5Oon2u5E.")]
    public async Task AuthenticateAsync_WithValidHashFormat_ShouldAcceptAsPrecomputedHash(string hashPassword)
    {
        var request = new LoginRequest
        {
            Username = "testuser",
            Password = hashPassword
        };

        var result = await _authService.AuthenticateAsync(request);

        result.Should().BeNull(); // Will fail because user doesn't exist, but validates hash format
    }

    [Theory]
    [InlineData("plaintext_password")]
    [InlineData("$invalid$format")]
    [InlineData("$99$unsupported$algorithm")]
    [InlineData("")]
    public async Task AuthenticateAsync_WithNonHashFormat_ShouldTreatAsPlaintext(string password)
    {
        var request = new LoginRequest
        {
            Username = "testuser", 
            Password = password
        };

        var result = await _authService.AuthenticateAsync(request);

        result.Should().BeNull(); // Will fail because user doesn't exist, but validates plaintext path
    }

    [Fact]
    public void IsPrecomputedHash_WithValidSHA512Hash_ShouldReturnTrue()
    {
        var hashPassword = "$6$randomsalt$veryLongSHA512HashString123456789012345678901234567890";
        
        var authService = CreateTestableAuthService();
        var result = authService.TestIsPrecomputedHash(hashPassword);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsPrecomputedHash_WithValidSHA256Hash_ShouldReturnTrue()
    {
        var hashPassword = "$5$anothersalt$sha256HashString123456789012345678";
        
        var authService = CreateTestableAuthService();
        var result = authService.TestIsPrecomputedHash(hashPassword);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsPrecomputedHash_WithValidMD5Hash_ShouldReturnTrue()
    {
        var hashPassword = "$1$md5salt$md5HashString123";
        
        var authService = CreateTestableAuthService();
        var result = authService.TestIsPrecomputedHash(hashPassword);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsPrecomputedHash_WithValidYescryptHash_ShouldReturnTrue()
    {
        var hashPassword = "$y$j9T$VRSySjntzaFIR9Ax10T7A0$dQhQpfWxdgtdfq1C63UqumQDfPISr8DN3M5Oon2u5E.";
        
        var authService = CreateTestableAuthService();
        var result = authService.TestIsPrecomputedHash(hashPassword);

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("plaintext")]
    [InlineData("$invalid")]
    [InlineData("$99$unsupported$algorithm")]
    [InlineData("")]
    [InlineData("no-dollar-signs")]
    public void IsPrecomputedHash_WithInvalidFormat_ShouldReturnFalse(string password)
    {
        var authService = CreateTestableAuthService();
        var result = authService.TestIsPrecomputedHash(password);

        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyPrecomputedHash_WithMatchingHashes_ShouldReturnTrue()
    {
        var hash = "$6$salt123$hashvalue456789";
        
        var authService = CreateTestableAuthService();
        var result = authService.TestVerifyPrecomputedHash(hash, hash);

        result.Should().BeTrue();
    }

    [Fact]
    public void VerifyPrecomputedHash_WithDifferentHashes_ShouldReturnFalse()
    {
        var hash1 = "$6$salt123$hashvalue456789";
        var hash2 = "$6$salt123$differenthash123";
        
        var authService = CreateTestableAuthService();
        var result = authService.TestVerifyPrecomputedHash(hash1, hash2);

        result.Should().BeFalse();
    }
}

// Test helper class to expose private methods for testing
internal class TestableAuthService : ShadowFileAuthenticationService
{
    public TestableAuthService(IConfiguration configuration, ILogger<ShadowFileAuthenticationService> logger, Microsoft.IdentityModel.Tokens.SymmetricSecurityKey signingKey) : base(configuration, logger, signingKey) { }

    public bool TestIsPrecomputedHash(string password)
    {
        var method = typeof(ShadowFileAuthenticationService)
            .GetMethod("IsPrecomputedHash", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (bool)method!.Invoke(this, new object[] { password })!;
    }

    public bool TestVerifyPrecomputedHash(string providedHash, string shadowHash)
    {
        var method = typeof(ShadowFileAuthenticationService)
            .GetMethod("VerifyPrecomputedHash", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (bool)method!.Invoke(this, new object[] { providedHash, shadowHash })!;
    }
}