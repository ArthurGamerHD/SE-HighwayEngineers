namespace HighwayEngineer.Common.Roads
{
    public enum RoadContractState
    {
        /// <summary>Accepted; waiting for the item in its delivery zone.</summary>
        Pending = 0,
        /// <summary>Delivered, but the remaining credits could not be charged yet.</summary>
        AwaitingPayment = 1,
        /// <summary>Paid and delivered; the server is pushing the road.</summary>
        Building = 2,
        Completed = 3,
        Failed = 4,
        Cancelled = 5
    }
}
