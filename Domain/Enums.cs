namespace TransportBidPortal.Domain;

public enum UserRole
{
    Shipper = 1,
    Carrier = 2,
    Admin = 3
}

public enum AuctionType
{
    Open = 1,
    Sealed = 2,
    MultiRound = 3
}

public enum BidStatus
{
    Draft = 1,
    Open = 2,
    Closed = 3
}

public enum ProposalStatus
{
    Draft = 1,
    Submitted = 2
}

public enum FacilityType
{
    Matriz = 1,
    Filial = 2
}
