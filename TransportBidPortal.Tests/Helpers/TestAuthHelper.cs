using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using TransportBidPortal.Domain;

namespace TransportBidPortal.Tests.Helpers;

public static class TestAuthHelper
{
    private const string TestKey = "SuperSecretKeyForTestingPurposesOnly_MustBeAtLeast32Chars!";
    private const string Issuer = "TransportBidPortal";
    private const string Audience = "TransportBidPortalUsers";

    public static string GenerateToken(Guid userId, string name, UserRole role)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, name),
            new(ClaimTypes.Role, role.ToString())
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(Issuer, Audience, claims,
            expires: DateTime.UtcNow.AddHours(8), signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static void SetAuth(this HttpClient client, Guid userId, string name, UserRole role)
    {
        var token = GenerateToken(userId, name, role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}
