using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using TransportBidPortal.Domain;
using TransportBidPortal.Services;

namespace TransportBidPortal.Tests.Unit;

public class JwtTokenServiceTests
{
    private readonly IJwtTokenService _sut;

    public JwtTokenServiceTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "SuperSecretKeyForTestingPurposesOnly_MustBeAtLeast32Chars!",
                ["Jwt:Issuer"] = "TransportBidPortal",
                ["Jwt:Audience"] = "TransportBidPortalUsers"
            })
            .Build();

        _sut = new JwtTokenService(config);
    }

    [Fact]
    public void Generate_ValidUser_ReturnsNonEmptyToken()
    {
        var user = CreateUser(UserRole.Shipper);
        var token = _sut.Generate(user);
        token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Generate_Token_ContainsCorrectClaims()
    {
        var user = CreateUser(UserRole.Carrier);
        var token = _sut.Generate(user);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Sub && c.Value == user.Id.ToString());
        jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.Role && c.Value == "Carrier");
        jwt.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Email && c.Value == user.Email);
    }

    [Fact]
    public void Generate_Token_HasCorrectIssuerAndAudience()
    {
        var user = CreateUser(UserRole.Admin);
        var token = _sut.Generate(user);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Issuer.Should().Be("TransportBidPortal");
        jwt.Audiences.Should().Contain("TransportBidPortalUsers");
    }

    [Fact]
    public void Generate_Token_ExpiresInFuture()
    {
        var user = CreateUser(UserRole.Shipper);
        var token = _sut.Generate(user);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.ValidTo.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public void Generate_MissingKey_ThrowsInvalidOperation()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var svc = new JwtTokenService(config);

        var act = () => svc.Generate(CreateUser(UserRole.Shipper));
        act.Should().Throw<InvalidOperationException>().WithMessage("*JWT key*");
    }

    private static AppUser CreateUser(UserRole role) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test User",
        Email = "test@test.com",
        Role = role,
        Company = "Test Co"
    };
}
