using Generated;
using ProtoBuf;
using HighwayEngineer.Common.Roads;

namespace HighwayEngineer.Common.Networking
{
    /// <summary>Server to requester: the bulldoze quote.</summary>
    [ProtoContract]
    [NetworkPayload(6)]
    public sealed partial class PacketBulldozeQuoteResponse
    {
        [ProtoMember(1)] public int RequestId;
        [ProtoMember(2)] public RoadRejectReason Reason;
        [ProtoMember(3)] public string Message;
        /// <summary>Positive: refunded to the player; negative: charged.</summary>
        [ProtoMember(4)] public long CreditDelta;
        [ProtoMember(5)] public long QuoteId;
        [ProtoMember(6)] public float ExpiresSeconds;
        [ProtoMember(7)] public float RetryAfterSeconds;
        [ProtoMember(8)] public bool Exempt;
    }
}
