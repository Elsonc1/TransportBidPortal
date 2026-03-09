using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportBidPortal.Data;

namespace TransportBidPortal.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SystemController(AppDbContext db) : ControllerBase
{
    [HttpGet("notifications")]
    public async Task<ActionResult<object>> Notifications(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
        var items = await db.Notifications
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(50)
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpGet("audit-log")]
    [Authorize(Roles = "Shipper")]
    public async Task<ActionResult<object>> AuditLog(CancellationToken ct)
    {
        var items = await db.AuditLogs
            .OrderByDescending(x => x.TimestampUtc)
            .Take(200)
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpGet("carriers")]
    [Authorize(Roles = "Shipper")]
    public async Task<ActionResult<object>> Carriers(CancellationToken ct)
    {
        var carriers = await db.Users
            .Where(x => x.Role == Domain.UserRole.Carrier)
            .Select(x => new { x.Id, x.Name, x.Company, x.Email })
            .ToListAsync(ct);
        return Ok(carriers);
    }
}
