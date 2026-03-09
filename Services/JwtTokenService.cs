using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using TransportBidPortal.Domain;

namespace TransportBidPortal.Services;

public interface IJwtTokenService
{
    string Generate(AppUser user);
}

public class JwtTokenService(IConfiguration configuration) : IJwtTokenService
{
    public string Generate(AppUser user)
    {
        var key = configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT key not configured.");
        var issuer = configuration["Jwt:Issuer"] ?? "TransportBidPortal";
        var audience = configuration["Jwt:Audience"] ?? "TransportBidPortalUsers";

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Role, user.Role.ToString())
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
