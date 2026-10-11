using Generated;
using ProtoBuf;
using HighwayEngineer.Common.Roads;

namespace HighwayEngineer.Common.Networking
{
    /// <summary>Client to server: list my contracts (on join, or for a progress view).</summary>
    [ProtoContract]
    [NetworkPayload(9)]
    public sealed partial class PacketRoadContractListRequest
    {
        [ProtoMember(1)] public int RequestId;
    }
}
