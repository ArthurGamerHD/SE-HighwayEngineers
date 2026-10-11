using Generated;
using ProtoBuf;
using HighwayEngineer.Common.Roads;

namespace HighwayEngineer.Common.Networking
{
    /// <summary>Server to owner: a contract changed (reply to accept/cancel, or unsolicited progress).</summary>
    [ProtoContract]
    [NetworkPayload(4)]
    public sealed partial class PacketRoadContractStatus
    {
        /// <summary>The request this answers; 0 for unsolicited updates.</summary>
        [ProtoMember(1)] public int RequestId;
        [ProtoMember(2)] public RoadContractInfo Contract;
        [ProtoMember(3)] public RoadRejectReason Reason;
        [ProtoMember(4)] public string Message;
        [ProtoMember(5)] public float RetryAfterSeconds;
    }
}
