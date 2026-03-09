using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportBidPortal.Contracts;
using TransportBidPortal.Data;
using TransportBidPortal.Domain;
using TransportBidPortal.Services;

namespace TransportBidPortal.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(AppDbContext db, IJwtTokenService tokenService) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(x => x.Email == request.Email, ct);
        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized("Invalid credentials.");
        }

        var token = tokenService.Generate(user);
        return Ok(new LoginResponse(token, user.Name, user.Role.ToString()));
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(x => x.Email == request.Email, ct))
        {
            return Conflict("Email already registered.");
        }

        if (!Enum.TryParse<UserRole>(request.Role, true, out var role))
        {
            return BadRequest("Role must be Shipper, Carrier or Admin.");
        }

        db.Users.Add(new AppUser
        {
            Name = request.Name,
            Email = request.Email,
            Role = role,
            Company = request.Company,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password)
        });
        await db.SaveChangesAsync(ct);
        return Ok();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<object>> Me(CancellationToken ct)
    {
        var id = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var user = await db.Users.Where(x => x.Id == id).Select(x => new { x.Name, x.Email, Role = x.Role.ToString(), x.Company }).FirstOrDefaultAsync(ct);
        return Ok(user);
    }
}
