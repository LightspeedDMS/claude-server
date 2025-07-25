using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using ClaudeBatchServer.Core.DTOs;
using ClaudeBatchServer.Core.Services;

namespace ClaudeBatchServer.Tests.Services;

public class ShadowFileAuthenticationServiceTests
{
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly ShadowFileAuthenticationService _authService;

    public ShadowFileAuthenticationServiceTests()
    {
        _mockConfiguration = new Mock<IConfiguration>();
        _mockConfiguration.Setup(c => c["Jwt:Key"]).Returns("ThisIsATestKeyThatIsLongEnoughForTesting");
        _mockConfiguration.Setup(c => c["Jwt:ExpiryHours"]).Returns("24");
        
        // Create test signing key
        var keyBytes = System.Text.Encoding.ASCII.GetBytes("ThisIsATestKeyThatIsLongEnoughForTesting");
        var signingKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(keyBytes) { KeyId = "test-key" };
        
        _authService = new ShadowFileAuthenticationService(_mockConfiguration.Object, signingKey);
    }

    [Fact]
    public async Task AuthenticateAsync_WithInvalidCredentials_ShouldReturnNull()
    {
        var request = new LoginRequest
        {
            Username = "invaliduser",
            Password = "invalidpassword"
        };

        var result = await _authService.AuthenticateAsync(request);

        result.Should().BeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_WithEmptyCredentials_ShouldReturnNull()
    {
        var request = new LoginRequest
        {
            Username = "",
            Password = ""
        };

        var result = await _authService.AuthenticateAsync(request);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ValidateTokenAsync_WithInvalidToken_ShouldReturnFalse()
    {
        var invalidToken = "invalid.token.here";

        var result = await _authService.ValidateTokenAsync(invalidToken);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateTokenAsync_WithEmptyToken_ShouldReturnFalse()
    {
        var result = await _authService.ValidateTokenAsync("");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task LogoutAsync_WithValidToken_ShouldReturnTrue()
    {
        var token = "some.valid.token";

        var result = await _authService.LogoutAsync(token);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateTokenAsync_WithRevokedToken_ShouldReturnFalse()
    {
        var token = "revoked.token.test";
        
        await _authService.LogoutAsync(token);
        var result = await _authService.ValidateTokenAsync(token);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetUserFromTokenAsync_WithInvalidToken_ShouldReturnNull()
    {
        var invalidToken = "invalid.token.here";

        var result = await _authService.GetUserFromTokenAsync(invalidToken);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    public async Task AuthenticateAsync_WithInvalidUsername_ShouldReturnNull(string username)
    {
        var request = new LoginRequest
        {
            Username = username,
            Password = "password"
        };

        var result = await _authService.AuthenticateAsync(request);

        result.Should().BeNull();
    }
}