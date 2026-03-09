using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportBidPortal.Data;

namespace TransportBidPortal.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class LogController(AppDbContext db) : ControllerBase
{
    /// <summary>
    /// Search logs with filters.
    /// GET /api/log?from=2026-02-01&to=2026-02-12&level=Error&service=API&text=timeout&correlationId=abc123&userId=...&page=1&pageSize=100
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<object>> Search(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? level,
        [FromQuery] string? service,
        [FromQuery] string? text,
        [FromQuery] string? correlationId,
        [FromQuery] Guid? userId,
        [FromQuery] string? userEmail,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken ct = default)
    {
        var query = db.AppLogs.AsQueryable();

        if (from.HasValue)
            query = query.Where(l => l.TimestampUtc >= from.Value.ToUniversalTime());
        if (to.HasValue)
            query = query.Where(l => l.TimestampUtc <= to.Value.ToUniversalTime());
        if (!string.IsNullOrWhiteSpace(level))
            query = query.Where(l => l.Level == level);
        if (!string.IsNullOrWhiteSpace(service))
            query = query.Where(l => l.Service == service);
        if (!string.IsNullOrWhiteSpace(text))
            query = query.Where(l => l.Message.Contains(text) || (l.StackTrace != null && l.StackTrace.Contains(text)));
        if (!string.IsNullOrWhiteSpace(correlationId))
            query = query.Where(l => l.CorrelationId == correlationId);
        if (userId.HasValue)
            query = query.Where(l => l.UserId == userId.Value);
        if (!string.IsNullOrWhiteSpace(userEmail))
            query = query.Where(l => l.UserEmail != null && l.UserEmail.Contains(userEmail));

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(l => l.TimestampUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new
            {
                l.Id,
                timestamp = l.TimestampUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                l.Level,
                l.Service,
                l.CorrelationId,
                l.Message,
                l.StackTrace,
                l.UserEmail,
                l.UserId,
                l.IpAddress,
                l.RequestPath,
                l.HttpMethod,
                l.HttpStatus,
                l.ElapsedMs
            })
            .ToListAsync(ct);

        return Ok(new
        {
            totalCount,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
            items
        });
    }

    /// <summary>
    /// Get distinct service names (for filter dropdown).
    /// </summary>
    [HttpGet("services")]
    public async Task<ActionResult<List<string>>> Services(CancellationToken ct)
    {
        var services = await db.AppLogs
            .Select(l => l.Service)
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync(ct);
        return Ok(services);
    }

    /// <summary>
    /// Get log stats (counts per level) for a given date range.
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<object>> Stats(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct = default)
    {
        var query = db.AppLogs.AsQueryable();
        if (from.HasValue)
            query = query.Where(l => l.TimestampUtc >= from.Value.ToUniversalTime());
        if (to.HasValue)
            query = query.Where(l => l.TimestampUtc <= to.Value.ToUniversalTime());

        var stats = await query
            .GroupBy(l => l.Level)
            .Select(g => new { Level = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return Ok(stats);
    }
}
