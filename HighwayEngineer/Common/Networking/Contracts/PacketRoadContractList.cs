using System.Collections.Generic;
using Generated;
using ProtoBuf;
using HighwayEngineer.Common.Roads;

namespace HighwayEngineer.Common.Networking
{
    /// <summary>Server to requester: the player's open contracts, with their delivery zones.</summary>
    [ProtoContract]
    [NetworkPayload(10)]
    public sealed partial class PacketRoadContractList
    {
        [ProtoMember(1)] public int RequestId;
        [ProtoMember(2)] public List<RoadContractInfo> Contracts = new List<RoadContractInfo>();
        [ProtoMember(3)] public float RetryAfterSeconds;
    }
}
