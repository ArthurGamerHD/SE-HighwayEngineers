using Generated;
using ProtoBuf;
using HighwayEngineer.Common.Roads;

namespace HighwayEngineer.Common.Networking
{
    /// <summary>Client to server: the built roads on a planet.</summary>
    [ProtoContract]
    [NetworkPayload(20)]
    public sealed partial class PacketRoadLibraryRequest
    {
        [ProtoMember(1)] public int RequestId;
        [ProtoMember(2)] public long PlanetId;
    }
}
