using System.ComponentModel.DataAnnotations;

namespace TransportBidPortal.Domain;

public class FreightRate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(80)] public string VehicleType { get; set; } = string.Empty;
    public decimal RatePerKm { get; set; }
    [MaxLength(200)] public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
