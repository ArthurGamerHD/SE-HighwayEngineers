using Generated;
using ProtoBuf;
using HighwayEngineer.Common.Roads;

namespace HighwayEngineer.Common.Networking
{
    /// <summary>Server to requester: the quote, or why the draft was rejected.</summary>
    [ProtoContract]
    [NetworkPayload(2)]
    public sealed partial class PacketRoadQuoteResponse
    {
        [ProtoMember(1)] public int RequestId;
        [ProtoMember(2)] public RoadRejectReason Reason;
        [ProtoMember(3)] public string Message;
        /// <summary>First invalid segment (1-based) for slope/crossing rejections; 0 otherwise.</summary>
        [ProtoMember(4)] public int InvalidSegment;
        [ProtoMember(5)] public RoadCostBreakdown Costs;
        /// <summary>Single-use; accepting it creates the contract.</summary>
        [ProtoMember(6)] public long QuoteId;
        [ProtoMember(7)] public float ExpiresSeconds;
        [ProtoMember(8)] public float RetryAfterSeconds;
        /// <summary>Where the delivery box will be placed.</summary>
        [ProtoMember(9)] public DeliveryZone Zone;
        /// <summary>The faction whose station takes the contract; null when none is needed (creative).</summary>
        [ProtoMember(10)] public RoadServiceProvider Provider;
        /// <summary>The existing Road Logistic Depot the delivery goes to; 0 when a new one is spawned (or none is needed).</summary>
        [ProtoMember(11)] public long DepotGridId;
    }
}
