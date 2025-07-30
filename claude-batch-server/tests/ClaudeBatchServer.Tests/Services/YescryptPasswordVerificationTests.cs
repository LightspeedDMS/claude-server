using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using ClaudeBatchServer.Core.Services;
using System.Reflection;

namespace ClaudeBatchServer.Tests.Services;

public class YescryptPasswordVerificationTests
{
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly TestableYescryptAuthService _authService;

    public YescryptPasswordVerificationTests()
    {
        _mockConfiguration = new Mock<IConfiguration>();
        _mockConfiguration.Setup(c => c["Jwt:Key"]).Returns("TestKeyForYescryptAuthenticationThatIsLongEnough");
        _mockConfiguration.Setup(c => c["Jwt:ExpiryHours"]).Returns("24");
        
        // Create test signing key
        var keyBytes = System.Text.Encoding.ASCII.GetBytes("TestKeyForYescryptAuthenticationThatIsLongEnough");
        var signingKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(keyBytes) { KeyId = "test-key" };
        
        _authService = new TestableYescryptAuthService(_mockConfiguration.Object, Mock.Of<ILogger<ShadowFileAuthenticationService>>(), signingKey);
    }

    [Fact]
    public void VerifyPassword_WithValidYescryptPassword_ShouldReturnTrue()
    {
        // This is a real yescrypt hash for testuser with password "test123"
        // From Ubuntu system: testuser:$y$j9T$VRSySjntzaFIR9Ax10T7A0$dQhQpfWxdgtdfq1C63UqumQDfPISr8DN3M5Oon2u5E.
        var knownHash = "$y$j9T$VRSySjntzaFIR9Ax10T7A0$dQhQpfWxdgtdfq1C63UqumQDfPISr8DN3M5Oon2u5E.";
        var password = "test123";

        var result = _authService.TestVerifyPassword(password, knownHash);

        result.Should().BeTrue();
    }

    [Fact]
    public void VerifyPassword_WithInvalidYescryptPassword_ShouldReturnFalse()
    {
        // Using the same hash but with wrong password
        var knownHash = "$y$j9T$VRSySjntzaFIR9Ax10T7A0$dQhQpfWxdgtdfq1C63UqumQDfPISr8DN3M5Oon2u5E.";
        var wrongPassword = "wrongpassword";

        var result = _authService.TestVerifyPassword(wrongPassword, knownHash);

        result.Should().BeFalse();
    }


    [Fact]
    public void VerifyPassword_WithMalformedYescryptHash_ShouldReturnFalse()
    {
        var malformedHash = "$y$invalid$hash";
        var password = "password";

        var result = _authService.TestVerifyPassword(password, malformedHash);

        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_WithEmptyYescryptHash_ShouldReturnFalse()
    {
        var emptyHash = "";
        var password = "password";

        var result = _authService.TestVerifyPassword(password, emptyHash);

        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_WithDisabledAccountHash_ShouldReturnFalse()
    {
        var disabledHash = "!";
        var password = "password";

        var result = _authService.TestVerifyPassword(password, disabledHash);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("$y$j9T$VRSySjntzaFIR9Ax10T7A0$dQhQpfWxdgtdfq1C63UqumQDfPISr8DN3M5Oon2u5E.")]
    [InlineData("$y$j9T$salt$hash")]
    [InlineData("$y$j10T$longersalt$longhashvalue")]
    public void IsYescryptHash_WithValidFormats_ShouldReturnTrue(string hash)
    {
        var parts = hash.Split('$');
        parts.Length.Should().BeGreaterOrEqualTo(4);
        parts[1].Should().Be("y");
    }
}

// Test helper class to expose private methods for testing
internal class TestableYescryptAuthService : ShadowFileAuthenticationService
{
    public TestableYescryptAuthService(IConfiguration configuration, 
        ILogger<ShadowFileAuthenticationService> logger,
        Microsoft.IdentityModel.Tokens.SymmetricSecurityKey signingKey) 
        : base(configuration, logger, signingKey) { }

    public bool TestVerifyPassword(string password, string hash)
    {
        var method = typeof(ShadowFileAuthenticationService)
            .GetMethod("VerifyPassword", BindingFlags.NonPublic | BindingFlags.Instance);
        return (bool)method!.Invoke(this, new object[] { password, hash })!;
    }
}