using System.Diagnostics;
using System.Security.Claims;
using TransportBidPortal.Data;
using TransportBidPortal.Domain;

namespace TransportBidPortal.Middleware;

public class RequestLoggingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        var correlationId = context.Items["CorrelationId"]?.ToString() ?? "";
        var path = context.Request.Path.ToString();
        var method = context.Request.Method;
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "";

        string? userEmail = null;
        Guid? userId = null;

        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            sw.Stop();
            ExtractUser(context, ref userId, ref userEmail);
            await PersistLog(context, new AppLog
            {
                Level = "Error",
                Service = "API",
                CorrelationId = correlationId,
                Message = $"[{method}] {path} - Unhandled: {ex.Message}",
                StackTrace = ex.StackTrace?[..Math.Min(ex.StackTrace.Length, 4000)],
                UserEmail = userEmail,
                UserId = userId,
                IpAddress = ip,
                RequestPath = path,
                HttpMethod = method,
                HttpStatus = 500,
                ElapsedMs = sw.ElapsedMilliseconds
            });
            throw;
        }

        sw.Stop();
        ExtractUser(context, ref userId, ref userEmail);

        var status = context.Response.StatusCode;
        var level = status >= 500 ? "Error" : status >= 400 ? "Warn" : "Info";

        await PersistLog(context, new AppLog
        {
            Level = level,
            Service = "API",
            CorrelationId = correlationId,
            Message = $"[{method}] {path} -> {status} ({sw.ElapsedMilliseconds}ms)",
            UserEmail = userEmail,
            UserId = userId,
            IpAddress = ip,
            RequestPath = path,
            HttpMethod = method,
            HttpStatus = status,
            ElapsedMs = sw.ElapsedMilliseconds
        });
    }

    private static void ExtractUser(HttpContext context, ref Guid? userId, ref string? userEmail)
    {
        var idClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(idClaim, out var uid))
        {
            userId = uid;
        }

        userEmail = context.User.FindFirstValue(ClaimTypes.Email)
                    ?? context.User.FindFirstValue("email");
    }

    private static async Task PersistLog(HttpContext context, AppLog log)
    {
        try
        {
            var db = context.RequestServices.GetRequiredService<AppDbContext>();
            db.AppLogs.Add(log);
            await db.SaveChangesAsync();
        }
        catch
        {
            // logging must never crash the pipeline
        }
    }
}
