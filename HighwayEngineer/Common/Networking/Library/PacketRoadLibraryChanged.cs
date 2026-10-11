using System.Collections.Generic;
using Generated;
using ProtoBuf;
using HighwayEngineer.Common.Roads;

namespace HighwayEngineer.Common.Networking
{
    /// <summary>Server to all clients: segments built or removed on a planet.</summary>
    [ProtoContract]
    [NetworkPayload(22)]
    public sealed partial class PacketRoadLibraryChanged
    {
        [ProtoMember(1)] public long PlanetId;
        [ProtoMember(2)] public int Revision;
        [ProtoMember(3)] public List<RoadSegmentRecord> Added = new List<RoadSegmentRecord>();
        [ProtoMember(4)] public long[] RemovedSegmentIds;
    }
}
