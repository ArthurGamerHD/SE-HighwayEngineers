using Generated;
using ProtoBuf;
using HighwayEngineer.Common.Config;
using HighwayEngineer.Common.Roads;

namespace HighwayEngineer.Common.Networking
{
    /// <summary>Server to clients: prices, on join and whenever they change.</summary>
    [ProtoContract]
    [NetworkPayload(40)]
    public sealed partial class PacketRoadCostSettingsSync
    {
        [ProtoMember(1)] public int ProtocolVersion;
        [ProtoMember(2)] public RoadCostSettings Settings;
    }
}
