using System.ComponentModel.DataAnnotations;

namespace TransportBidPortal.Domain;

public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(150)] public string Name { get; set; } = string.Empty;
    [MaxLength(160)] public string Email { get; set; } = string.Empty;
    [MaxLength(200)] public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    [MaxLength(100)] public string Company { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class BidEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CreatedByShipperId { get; set; }
    public AppUser? CreatedByShipper { get; set; }
    [MaxLength(180)] public string Title { get; set; } = string.Empty;
    public AuctionType AuctionType { get; set; }
    public DateTime DeadlineUtc { get; set; }
    public BidStatus Status { get; set; } = BidStatus.Draft;
    public int CurrentRound { get; set; } = 1;
    [MaxLength(300)] public string RequiredDocumentation { get; set; } = string.Empty;
    public decimal BaselineContractValue { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<BidLane> Lanes { get; set; } = new List<BidLane>();
    public ICollection<BidInvitation> Invitations { get; set; } = new List<BidInvitation>();
}

public class BidLane
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BidEventId { get; set; }
    public BidEvent? BidEvent { get; set; }
    [MaxLength(120)] public string Origin { get; set; } = string.Empty;
    [MaxLength(120)] public string Destination { get; set; } = string.Empty;
    [MaxLength(80)] public string FreightType { get; set; } = string.Empty;
    public decimal VolumeForecast { get; set; }
    [MaxLength(140)] public string SlaRequirements { get; set; } = string.Empty;
    [MaxLength(80)] public string VehicleType { get; set; } = string.Empty;
    [MaxLength(120)] public string InsuranceRequirements { get; set; } = string.Empty;
    [MaxLength(120)] public string PaymentTerms { get; set; } = string.Empty;
    [MaxLength(80)] public string Region { get; set; } = string.Empty;
}

public class BidInvitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BidEventId { get; set; }
    public BidEvent? BidEvent { get; set; }
    public Guid CarrierId { get; set; }
    public AppUser? Carrier { get; set; }
    [MaxLength(100)] public string InviteToken { get; set; } = Guid.NewGuid().ToString("N");
    public bool EmailSent { get; set; }
    public DateTime InvitedAtUtc { get; set; } = DateTime.UtcNow;
}

public class CarrierProposal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BidEventId { get; set; }
    public BidEvent? BidEvent { get; set; }
    public Guid CarrierId { get; set; }
    public AppUser? Carrier { get; set; }
    public ProposalStatus Status { get; set; } = ProposalStatus.Draft;
    public int Version { get; set; } = 1;
    public decimal OperationalCapacityTons { get; set; }
    public bool SlaCompliant { get; set; }
    public decimal TotalPrice { get; set; }
    public DateTime SavedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAtUtc { get; set; }
    public ICollection<CarrierProposalLanePrice> LanePrices { get; set; } = new List<CarrierProposalLanePrice>();
    public ICollection<CarrierProposalDocument> Documents { get; set; } = new List<CarrierProposalDocument>();
}

public class CarrierProposalLanePrice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProposalId { get; set; }
    public CarrierProposal? Proposal { get; set; }
    public Guid LaneId { get; set; }
    public BidLane? Lane { get; set; }
    public decimal PricePerLane { get; set; }
}

public class CarrierProposalDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProposalId { get; set; }
    public CarrierProposal? Proposal { get; set; }
    [MaxLength(180)] public string OriginalFileName { get; set; } = string.Empty;
    [MaxLength(300)] public string StoredPath { get; set; } = string.Empty;
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
}

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }
    [MaxLength(200)] public string Title { get; set; } = string.Empty;
    [MaxLength(400)] public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    [MaxLength(100)] public string Action { get; set; } = string.Empty;
    [MaxLength(120)] public string EntityType { get; set; } = string.Empty;
    [MaxLength(120)] public string EntityId { get; set; } = string.Empty;
    [MaxLength(400)] public string Details { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}

public class CarrierQualification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CarrierId { get; set; }
    public AppUser? Carrier { get; set; }
    public decimal QualificationScore { get; set; }
    public decimal HistoricalOnTimeRate { get; set; }
    public decimal ClaimsRatio { get; set; }
}

public class BidTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShipperId { get; set; }
    public AppUser? Shipper { get; set; }
    [MaxLength(180)] public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<BidTemplateColumn> Columns { get; set; } = new List<BidTemplateColumn>();
}

public class BidTemplateColumn
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BidTemplateId { get; set; }
    public BidTemplate? BidTemplate { get; set; }
    [MaxLength(80)] public string Key { get; set; } = string.Empty;
    [MaxLength(120)] public string DisplayName { get; set; } = string.Empty;
    [MaxLength(500)] public string Aliases { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    [MaxLength(40)] public string DataType { get; set; } = "text";
    public int SortOrder { get; set; }
}

public class ShipperFacility
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShipperId { get; set; }
    public AppUser? Shipper { get; set; }
    public FacilityType Type { get; set; } = FacilityType.Filial;
    [MaxLength(180)] public string Name { get; set; } = string.Empty;
    [MaxLength(20)] public string Cnpj { get; set; } = string.Empty;
    [MaxLength(200)] public string Address { get; set; } = string.Empty;
    [MaxLength(120)] public string City { get; set; } = string.Empty;
    [MaxLength(2)] public string State { get; set; } = string.Empty;
    [MaxLength(10)] public string ZipCode { get; set; } = string.Empty;
    [MaxLength(100)] public string Country { get; set; } = "Brasil";
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class ExcelMappingProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShipperId { get; set; }
    public AppUser? Shipper { get; set; }
    [MaxLength(180)] public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<ExcelMappingRule> Rules { get; set; } = new List<ExcelMappingRule>();
}

public class ExcelMappingRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExcelMappingProfileId { get; set; }
    public ExcelMappingProfile? ExcelMappingProfile { get; set; }
    [MaxLength(80)] public string CanonicalField { get; set; } = string.Empty;
    [MaxLength(120)] public string DisplayName { get; set; } = string.Empty;
    [MaxLength(600)] public string Aliases { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    [MaxLength(40)] public string DataType { get; set; } = "text";
    public int SortOrder { get; set; }
}
