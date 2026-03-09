using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TransportBidPortal.Data;
using TransportBidPortal.Domain;

namespace TransportBidPortal.Services;

public interface IFileStorageService
{
    Task<string> SaveAsync(IFormFile file, CancellationToken ct);
}

public class FileStorageService(IWebHostEnvironment env) : IFileStorageService
{
    public async Task<string> SaveAsync(IFormFile file, CancellationToken ct)
    {
        var root = Path.Combine(env.ContentRootPath, "Storage", "Documents");
        Directory.CreateDirectory(root);
        var safe = $"{Guid.NewGuid():N}_{Path.GetFileName(file.FileName)}";
        var path = Path.Combine(root, safe);
        await using var stream = File.Create(path);
        await file.CopyToAsync(stream, ct);
        return path;
    }
}

public interface INotificationService
{
    Task NotifyAsync(Guid userId, string title, string message, CancellationToken ct);
}

public class NotificationService(AppDbContext db, ILogger<NotificationService> logger) : INotificationService
{
    public async Task NotifyAsync(Guid userId, string title, string message, CancellationToken ct)
    {
        db.Notifications.Add(new Notification
        {
            UserId = userId,
            Title = title,
            Message = message
        });
        logger.LogInformation("Email simulation to {UserId}: {Title} - {Message}", userId, title, message);
        await db.SaveChangesAsync(ct);
    }
}

public interface IAuditService
{
    Task LogAsync(Guid userId, string action, string entityType, string entityId, string details, CancellationToken ct);
}

public class AuditService(AppDbContext db) : IAuditService
{
    public async Task LogAsync(Guid userId, string action, string entityType, string entityId, string details, CancellationToken ct)
    {
        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details
        });
        await db.SaveChangesAsync(ct);
    }
}

public interface IExportService
{
    byte[] BuildExcelExport(dynamic dashboardData);
    byte[] BuildPdfExport(dynamic dashboardData);
}

public class ExportService : IExportService
{
    public byte[] BuildExcelExport(dynamic dashboardData)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Ranking");
        ws.Cell(1, 1).Value = "Carrier";
        ws.Cell(1, 2).Value = "Total Price";
        ws.Cell(1, 3).Value = "Score";

        var row = 2;
        foreach (var item in dashboardData.Ranking)
        {
            ws.Cell(row, 1).Value = item.CarrierName;
            ws.Cell(row, 2).Value = item.TotalPrice;
            ws.Cell(row, 3).Value = item.Score;
            row++;
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public byte[] BuildPdfExport(dynamic dashboardData)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(container =>
            container.Page(page =>
            {
                page.Margin(24);
                page.Header().Text("Transport BID Dashboard Export").FontSize(18).Bold();
                page.Content().Column(col =>
                {
                    col.Spacing(6);
                    col.Item().Text($"Generated UTC: {DateTime.UtcNow:yyyy-MM-dd HH:mm}");
                    foreach (var item in dashboardData.Ranking)
                    {
                        col.Item().Text($"{item.CarrierName} | Price: {item.TotalPrice:C} | Score: {item.Score}");
                    }
                });
                page.Footer().AlignCenter().Text(x => x.Span("Transport BID Portal"));
            })).GeneratePdf();
    }
}
