namespace TransportBidPortal.Tests.Helpers;

/// <summary>IDs fixos do <c>AppDbContext</c> HasData (hex válidos) para integração/E2E.</summary>
public static class TestSeedIds
{
    public static readonly Guid DemoShipperId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid DemoCarrierAId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid DeliveryPointCuritibaId = Guid.Parse("0e111111-1111-1111-1111-111111111111");
    public static readonly Guid DeliveryPointJoinvilleId = Guid.Parse("0e222222-2222-2222-2222-222222222222");
}
