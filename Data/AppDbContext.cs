using Microsoft.EntityFrameworkCore;
using TransportBidPortal.Domain;

namespace TransportBidPortal.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<BidEvent> BidEvents => Set<BidEvent>();
    public DbSet<BidLane> BidLanes => Set<BidLane>();
    public DbSet<BidInvitation> BidInvitations => Set<BidInvitation>();
    public DbSet<CarrierProposal> CarrierProposals => Set<CarrierProposal>();
    public DbSet<CarrierProposalLanePrice> CarrierProposalLanePrices => Set<CarrierProposalLanePrice>();
    public DbSet<CarrierProposalDocument> CarrierProposalDocuments => Set<CarrierProposalDocument>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<CarrierQualification> CarrierQualifications => Set<CarrierQualification>();
    public DbSet<BidTemplate> BidTemplates => Set<BidTemplate>();
    public DbSet<BidTemplateColumn> BidTemplateColumns => Set<BidTemplateColumn>();
    public DbSet<ExcelMappingProfile> ExcelMappingProfiles => Set<ExcelMappingProfile>();
    public DbSet<AppLog> AppLogs => Set<AppLog>();
    public DbSet<ShipperFacility> ShipperFacilities => Set<ShipperFacility>();
    public DbSet<ExcelMappingRule> ExcelMappingRules => Set<ExcelMappingRule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var shipperId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var carrierA = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var carrierB = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var adminId = Guid.Parse("99999999-9999-9999-9999-999999999999");

        modelBuilder.Entity<AppUser>().HasIndex(x => x.Email).IsUnique();
        modelBuilder.Entity<BidInvitation>()
            .HasIndex(x => new { x.BidEventId, x.CarrierId }).IsUnique();
        modelBuilder.Entity<CarrierProposal>()
            .HasIndex(x => new { x.BidEventId, x.CarrierId, x.Version }).IsUnique();
        modelBuilder.Entity<BidTemplate>()
            .HasIndex(x => new { x.ShipperId, x.Name }).IsUnique();
        modelBuilder.Entity<ExcelMappingProfile>()
            .HasIndex(x => new { x.ShipperId, x.Name }).IsUnique();

        modelBuilder.Entity<BidEvent>()
            .HasOne(x => x.CreatedByShipper)
            .WithMany()
            .HasForeignKey(x => x.CreatedByShipperId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<BidInvitation>()
            .HasOne(x => x.Carrier)
            .WithMany()
            .HasForeignKey(x => x.CarrierId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CarrierProposal>()
            .HasOne(x => x.Carrier)
            .WithMany()
            .HasForeignKey(x => x.CarrierId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CarrierProposalLanePrice>()
            .HasOne(x => x.Lane)
            .WithMany()
            .HasForeignKey(x => x.LaneId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<BidTemplate>()
            .HasOne(x => x.Shipper)
            .WithMany()
            .HasForeignKey(x => x.ShipperId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<BidTemplateColumn>()
            .HasOne(x => x.BidTemplate)
            .WithMany(x => x.Columns)
            .HasForeignKey(x => x.BidTemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ExcelMappingProfile>()
            .HasOne(x => x.Shipper)
            .WithMany()
            .HasForeignKey(x => x.ShipperId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ExcelMappingRule>()
            .HasOne(x => x.ExcelMappingProfile)
            .WithMany(x => x.Rules)
            .HasForeignKey(x => x.ExcelMappingProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CarrierProposal>().Property(x => x.TotalPrice).HasPrecision(18, 2);
        modelBuilder.Entity<CarrierProposal>().Property(x => x.OperationalCapacityTons).HasPrecision(18, 2);
        modelBuilder.Entity<CarrierProposalLanePrice>().Property(x => x.PricePerLane).HasPrecision(18, 2);
        modelBuilder.Entity<BidEvent>().Property(x => x.BaselineContractValue).HasPrecision(18, 2);
        modelBuilder.Entity<BidLane>().Property(x => x.VolumeForecast).HasPrecision(18, 2);
        modelBuilder.Entity<CarrierQualification>().Property(x => x.QualificationScore).HasPrecision(18, 2);
        modelBuilder.Entity<CarrierQualification>().Property(x => x.HistoricalOnTimeRate).HasPrecision(18, 2);
        modelBuilder.Entity<CarrierQualification>().Property(x => x.ClaimsRatio).HasPrecision(18, 2);

        var defaultTemplateId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        modelBuilder.Entity<BidTemplate>().HasData(
            new BidTemplate
            {
                Id = defaultTemplateId,
                ShipperId = shipperId,
                Name = "Template Padrão BID",
                IsDefault = true
            });

        modelBuilder.Entity<BidTemplateColumn>().HasData(
            new BidTemplateColumn { Id = Guid.Parse("77777777-7777-7777-7777-777777777701"), BidTemplateId = defaultTemplateId, Key = "Origin", DisplayName = "Origem", Aliases = "origem,origin,cidade origem", IsRequired = true, DataType = "text", SortOrder = 1 },
            new BidTemplateColumn { Id = Guid.Parse("77777777-7777-7777-7777-777777777702"), BidTemplateId = defaultTemplateId, Key = "Destination", DisplayName = "Destino", Aliases = "destino,destination,cidade destino", IsRequired = true, DataType = "text", SortOrder = 2 },
            new BidTemplateColumn { Id = Guid.Parse("77777777-7777-7777-7777-777777777703"), BidTemplateId = defaultTemplateId, Key = "FreightType", DisplayName = "Tipo Frete", Aliases = "tipo frete,freight type", IsRequired = false, DataType = "text", SortOrder = 3 },
            new BidTemplateColumn { Id = Guid.Parse("77777777-7777-7777-7777-777777777704"), BidTemplateId = defaultTemplateId, Key = "VolumeForecast", DisplayName = "Volume", Aliases = "volume,volume forecast,previsao volume", IsRequired = false, DataType = "number", SortOrder = 4 },
            new BidTemplateColumn { Id = Guid.Parse("77777777-7777-7777-7777-777777777705"), BidTemplateId = defaultTemplateId, Key = "VehicleType", DisplayName = "Tipo Veículo", Aliases = "tipo veiculo,vehicle type", IsRequired = false, DataType = "text", SortOrder = 5 },
            new BidTemplateColumn { Id = Guid.Parse("77777777-7777-7777-7777-777777777706"), BidTemplateId = defaultTemplateId, Key = "SlaRequirements", DisplayName = "SLA", Aliases = "sla,service level", IsRequired = false, DataType = "text", SortOrder = 6 },
            new BidTemplateColumn { Id = Guid.Parse("77777777-7777-7777-7777-777777777707"), BidTemplateId = defaultTemplateId, Key = "Region", DisplayName = "Região", Aliases = "regiao,region", IsRequired = false, DataType = "text", SortOrder = 7 }
        );

        modelBuilder.Entity<AppUser>().HasData(
            new AppUser
            {
                Id = shipperId,
                Name = "Demo Shipper",
                Email = "shipper@demo.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Demo@123"),
                Role = UserRole.Shipper,
                Company = "Global Foods"
            },
            new AppUser
            {
                Id = carrierA,
                Name = "Carrier Prime",
                Email = "carrier1@demo.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Demo@123"),
                Role = UserRole.Carrier,
                Company = "Prime Logistics"
            },
            new AppUser
            {
                Id = carrierB,
                Name = "Carrier Delta",
                Email = "carrier2@demo.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Demo@123"),
                Role = UserRole.Carrier,
                Company = "Delta Transportes"
            },
            new AppUser
            {
                Id = adminId,
                Name = "Portal Admin",
                Email = "admin@demo.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Demo@123"),
                Role = UserRole.Admin,
                Company = "Bid Platform Support"
            });

        modelBuilder.Entity<CarrierQualification>().HasData(
            new CarrierQualification
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                CarrierId = carrierA,
                QualificationScore = 86,
                HistoricalOnTimeRate = 94,
                ClaimsRatio = 2
            },
            new CarrierQualification
            {
                Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                CarrierId = carrierB,
                QualificationScore = 79,
                HistoricalOnTimeRate = 90,
                ClaimsRatio = 3
            });

        var defaultProfileId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        modelBuilder.Entity<ExcelMappingProfile>().HasData(
            new ExcelMappingProfile
            {
                Id = defaultProfileId,
                ShipperId = shipperId,
                Name = "Perfil RFQ Padrão",
                IsActive = true
            });

        modelBuilder.Entity<ExcelMappingRule>().HasData(
            new ExcelMappingRule { Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"), ExcelMappingProfileId = defaultProfileId, CanonicalField = "Origin", DisplayName = "Origem", Aliases = "origem,origin,cidade origem", IsRequired = true, DataType = "text", SortOrder = 1 },
            new ExcelMappingRule { Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2"), ExcelMappingProfileId = defaultProfileId, CanonicalField = "Destination", DisplayName = "Destino", Aliases = "destino,destination,cidade destino", IsRequired = true, DataType = "text", SortOrder = 2 },
            new ExcelMappingRule { Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3"), ExcelMappingProfileId = defaultProfileId, CanonicalField = "FreightType", DisplayName = "Tipo Frete", Aliases = "tipo frete,freight type", IsRequired = false, DataType = "text", SortOrder = 3 },
            new ExcelMappingRule { Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa4"), ExcelMappingProfileId = defaultProfileId, CanonicalField = "VolumeForecast", DisplayName = "Volume", Aliases = "volume,previsao volume", IsRequired = false, DataType = "number", SortOrder = 4 },
            new ExcelMappingRule { Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa5"), ExcelMappingProfileId = defaultProfileId, CanonicalField = "VehicleType", DisplayName = "Tipo Veículo", Aliases = "tipo veiculo,vehicle type", IsRequired = false, DataType = "text", SortOrder = 5 },
            new ExcelMappingRule { Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa6"), ExcelMappingProfileId = defaultProfileId, CanonicalField = "SlaRequirements", DisplayName = "SLA", Aliases = "sla,service level", IsRequired = false, DataType = "text", SortOrder = 6 },
            new ExcelMappingRule { Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa7"), ExcelMappingProfileId = defaultProfileId, CanonicalField = "Region", DisplayName = "Região", Aliases = "regiao,region", IsRequired = false, DataType = "text", SortOrder = 7 }
        );

        modelBuilder.Entity<ShipperFacility>()
            .HasOne(x => x.Shipper)
            .WithMany()
            .HasForeignKey(x => x.ShipperId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ShipperFacility>().Property(x => x.Latitude).HasPrecision(10, 7);
        modelBuilder.Entity<ShipperFacility>().Property(x => x.Longitude).HasPrecision(10, 7);

        modelBuilder.Entity<ShipperFacility>().HasData(
            new ShipperFacility
            {
                Id = Guid.Parse("fac11111-1111-1111-1111-111111111111"),
                ShipperId = shipperId,
                Type = FacilityType.Matriz,
                Name = "CD São Paulo (Matriz)",
                Cnpj = "12.345.678/0001-90",
                Address = "Av. Paulista, 1000",
                City = "São Paulo",
                State = "SP",
                ZipCode = "01310-100",
                IsActive = true
            },
            new ShipperFacility
            {
                Id = Guid.Parse("fac22222-2222-2222-2222-222222222222"),
                ShipperId = shipperId,
                Type = FacilityType.Filial,
                Name = "CD Curitiba",
                Cnpj = "12.345.678/0002-71",
                Address = "Rod. BR-116, km 98",
                City = "Curitiba",
                State = "PR",
                ZipCode = "81630-000",
                IsActive = true
            },
            new ShipperFacility
            {
                Id = Guid.Parse("fac33333-3333-3333-3333-333333333333"),
                ShipperId = shipperId,
                Type = FacilityType.Filial,
                Name = "CD Joinville",
                Cnpj = "12.345.678/0003-52",
                Address = "Rua Industrial, 500",
                City = "Joinville",
                State = "SC",
                ZipCode = "89219-100",
                IsActive = true
            }
        );

        modelBuilder.Entity<AppLog>().HasKey(x => x.Id);
        modelBuilder.Entity<AppLog>().Property(x => x.Id).ValueGeneratedOnAdd();
        modelBuilder.Entity<AppLog>().HasIndex(x => x.TimestampUtc);
        modelBuilder.Entity<AppLog>().HasIndex(x => x.Level);
        modelBuilder.Entity<AppLog>().HasIndex(x => x.CorrelationId);
        modelBuilder.Entity<AppLog>().HasIndex(x => x.UserId);
    }
}
