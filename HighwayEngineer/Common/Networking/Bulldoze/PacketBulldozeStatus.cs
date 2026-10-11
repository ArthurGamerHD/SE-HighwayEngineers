using Generated;
using ProtoBuf;
using HighwayEngineer.Common.Roads;

namespace HighwayEngineer.Common.Networking
{
    /// <summary>Server to requester: progress of a bulldoze job.</summary>
    [ProtoContract]
    [NetworkPayload(11)]
    public sealed partial class PacketBulldozeStatus
    {
        [ProtoMember(1)] public int RequestId;
        [ProtoMember(2)] public RoadJobState State;
        [ProtoMember(3)] public RoadRejectReason Reason;
        [ProtoMember(4)] public string Message;
        [ProtoMember(5)] public long CreditDelta;
    }
}
