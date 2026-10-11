using Generated;
using ProtoBuf;
using HighwayEngineer.Common.Roads;

namespace HighwayEngineer.Common.Networking
{
    /// <summary>Client to server: cancel a contract that has not started building.</summary>
    [ProtoContract]
    [NetworkPayload(8)]
    public sealed partial class PacketRoadContractCancel
    {
        [ProtoMember(1)] public int RequestId;
        [ProtoMember(2)] public long ContractId;
    }
}
