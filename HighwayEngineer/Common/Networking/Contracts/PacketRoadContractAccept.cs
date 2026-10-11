using Generated;
using ProtoBuf;
using HighwayEngineer.Common.Roads;

namespace HighwayEngineer.Common.Networking
{
    /// <summary>Client to server: accept a quote, creating a contract (charges the upfront share).</summary>
    [ProtoContract]
    [NetworkPayload(3)]
    public sealed partial class PacketRoadContractAccept
    {
        [ProtoMember(1)] public int RequestId;
        [ProtoMember(2)] public long QuoteId;
    }
}
