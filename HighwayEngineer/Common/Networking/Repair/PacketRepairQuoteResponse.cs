using Generated;
using ProtoBuf;
using HighwayEngineer.Common.Roads;

namespace HighwayEngineer.Common.Networking
{
    /// <summary>Server to requester: the repair, checked and ready to confirm.</summary>
    [ProtoContract]
    [NetworkPayload(14)]
    public sealed partial class PacketRepairQuoteResponse
    {
        [ProtoMember(1)] public int RequestId;
        [ProtoMember(2)] public RoadRejectReason Reason;
        /// <summary>What will be repaired, for the confirmation (or why it cannot be).</summary>
        [ProtoMember(3)] public string Message;
        [ProtoMember(4)] public long QuoteId;
        [ProtoMember(5)] public float ExpiresSeconds;
        [ProtoMember(6)] public float RetryAfterSeconds;
    }
}
