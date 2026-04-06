using TransportBidPortal.Domain;

namespace TransportBidPortal.Contracts;

public record LoginRequest(string Email, string Password);
public record LoginResponse(string Token, string Name, string Role);
public record RegisterRequest(string Name, string Email, string Password, string Role, string Company);

public record CreateBidRequest(
    string Title,
    AuctionType AuctionType,
    DateTime? DeadlineUtc,
    string? RequiredDocumentation,
    decimal? BaselineContractValue,
    Guid? OriginFacilityId,
    Guid? TemplateId,
    List<BidLaneInput> Lanes);

public record BidLaneInput(
    string Origin,
    string Destination,
    string? FreightType,
    decimal VolumeForecast,
    string? SlaRequirements,
    string? VehicleType,
    string? InsuranceRequirements,
    string? PaymentTerms,
    string? Region);

public record InviteCarriersRequest(List<Guid> CarrierIds);

public record ProposalLanePriceInput(Guid LaneId, decimal PricePerLane);

public record SaveProposalRequest(
    Guid BidId,
    List<ProposalLanePriceInput> LanePrices,
    decimal OperationalCapacityTons,
    bool SlaCompliant);

public record DashboardFilter(
    string? Region,
    Guid? CarrierId,
    string? VehicleType);

public record RankingWeights(
    decimal PriceWeight,
    decimal CostBenefitWeight,
    decimal SlaWeight,
    decimal QualificationWeight);

public record ExcelColumnMatch(
    string CanonicalField,
    string MatchedHeader,
    int ColumnIndex,
    decimal Confidence);

public record ExcelAnalysisResult(
    string SheetName,
    int TotalRows,
    int TotalColumns,
    List<ExcelColumnMatch> Matches,
    List<string> UnmappedHeaders);

public record TemplateColumnInput(
    string Key,
    string DisplayName,
    string Aliases,
    bool IsRequired,
    string DataType,
    int SortOrder);

public record CreateBidTemplateRequest(
    string Name,
    bool IsDefault,
    List<TemplateColumnInput> Columns);

public record UpdateBidTemplateRequest(
    string Name,
    bool IsDefault,
    List<TemplateColumnInput> Columns);

public record TemplateHeaderMatch(
    string TemplateKey,
    string DisplayName,
    bool IsRequired,
    string? MatchedHeader,
    decimal Confidence);

public record TemplateFieldDefinition(
    string Key,
    string DisplayName,
    string Aliases,
    bool IsRequired,
    string DataType,
    int SortOrder);

public record MappingRuleInput(
    string CanonicalField,
    string DisplayName,
    string Aliases,
    bool IsRequired,
    string DataType,
    int SortOrder);

public record SaveMappingProfileRequest(
    Guid ShipperId,
    string Name,
    bool IsActive,
    List<MappingRuleInput> Rules);

public record RouteSuggestion(
    Guid OriginFacilityId, string OriginName, string OriginCity, string OriginState, string OriginCep,
    Guid DestFacilityId,   string DestName,   string DestCity,   string DestState,   string DestCep,
    double DistanceKm, double DurationHours, decimal EstimatedCost,
    string SuggestedVehicleType, int Rank);

public record SaveFacilityRequest(
    string Name,
    string Type,
    string Cnpj,
    string Address,
    string City,
    string State,
    string ZipCode,
    string? Country,
    decimal? Latitude,
    decimal? Longitude,
    bool IsActive);

public record SourceHeader(int ColumnIndex, string Name);

public record ExtractHeadersResult(
    string SheetName,
    int DetectedHeaderRow,
    int TotalRows,
    List<SourceHeader> Headers);

public record ColumnMappingInput(int SourceColumnIndex, string TargetFieldKey);

public record TransformRequest(
    int HeaderRow,
    List<ColumnMappingInput> Mappings);

public record TransformPreviewResult(
    int TotalRows,
    List<string> Columns,
    List<Dictionary<string, string>> Rows);

public record RawCellData(int ColumnIndex, string ColumnLetter, string Value);

public record RawRowData(int RowNumber, List<RawCellData> Cells);

public record ExcelRawPreviewResult(
    string SheetName,
    int TotalRows,
    int TotalColumns,
    int DetectedHeaderRow,
    List<string> ColumnLetters,
    List<RawRowData> Rows);
