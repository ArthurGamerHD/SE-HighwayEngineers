using Generated;
using ProtoBuf;

namespace HighwayEngineer.Common.Networking
{
    /// <summary>Client to server: carry out a repair quote.</summary>
    [ProtoContract]
    [NetworkPayload(15)]
    public sealed partial class PacketRepairRequest
    {
        [ProtoMember(1)] public int RequestId;
        [ProtoMember(2)] public long QuoteId;
    }
}
