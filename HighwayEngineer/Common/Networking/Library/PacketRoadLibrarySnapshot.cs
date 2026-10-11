using System.Collections.Generic;
using Generated;
using ProtoBuf;
using HighwayEngineer.Common.Roads;

namespace HighwayEngineer.Common.Networking
{
    /// <summary>Server to requester: every built segment on a planet.</summary>
    [ProtoContract]
    [NetworkPayload(21)]
    public sealed partial class PacketRoadLibrarySnapshot
    {
        [ProtoMember(1)] public int RequestId;
        [ProtoMember(2)] public long PlanetId;
        /// <summary>Increases with every change; a gap in <see cref="PacketRoadLibraryChanged.Revision"/>
        /// means a missed update, so the client asks for a new snapshot.</summary>
        [ProtoMember(3)] public int Revision;
        [ProtoMember(4)] public List<RoadSegmentRecord> Segments = new List<RoadSegmentRecord>();
        [ProtoMember(5)] public float RetryAfterSeconds;
    }
}
