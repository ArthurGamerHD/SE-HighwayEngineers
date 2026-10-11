namespace HighwayEngineer.Common.Roads
{
    public enum RoadRejectReason
    {
        None = 0,
        InvalidGeometry = 1,
        SlopeExceeded = 2,
        SelfCrossing = 3,
        OutOfLimits = 4,
        MaterialNotAllowed = 5,
        NoPermission = 6,
        InsufficientCredits = 7,
        RateLimited = 8,
        ServerBusy = 9,
        Superseded = 10,
        QuoteExpired = 11,
        UnknownContract = 12,
        ApiUnavailable = 13,
        PushFailed = 14,
        /// <summary>No faction station near the road to take the contract.</summary>
        NoServiceProvider = 15,
        /// <summary>The game's contract system refused the contract.</summary>
        ContractRejected = 16,
        TooManyContracts = 17
    }
}
