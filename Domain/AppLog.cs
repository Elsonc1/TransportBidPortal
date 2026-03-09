using System.ComponentModel.DataAnnotations;

namespace TransportBidPortal.Domain;

public class AppLog
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(10)] public string Level { get; set; } = "Info";
    [MaxLength(80)] public string Service { get; set; } = "API";
    [MaxLength(60)] public string CorrelationId { get; set; } = string.Empty;
    [MaxLength(500)] public string Message { get; set; } = string.Empty;
    [MaxLength(4000)] public string? StackTrace { get; set; }
    [MaxLength(160)] public string? UserEmail { get; set; }
    public Guid? UserId { get; set; }
    [MaxLength(50)] public string? IpAddress { get; set; }
    [MaxLength(200)] public string? RequestPath { get; set; }
    [MaxLength(10)] public string? HttpMethod { get; set; }
    public int? HttpStatus { get; set; }
    public long? ElapsedMs { get; set; }
}
