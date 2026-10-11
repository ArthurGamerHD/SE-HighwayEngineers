using System.Collections.Generic;
using Generated;
using ProtoBuf;
using HighwayEngineer.Common.Roads;

namespace HighwayEngineer.Common.Networking
{
    /// <summary>Client to server: price removing built segments.</summary>
    [ProtoContract]
    [NetworkPayload(5)]
    public sealed partial class PacketBulldozeQuoteRequest
    {
        [ProtoMember(1)] public int RequestId;
        [ProtoMember(2)] public long PlanetId;
        /// <summary>Road library segments.</summary>
        [ProtoMember(3)] public long[] SegmentIds;
        /// <summary>Road lines of the voxel API that are not in the library.</summary>
        [ProtoMember(4)] public List<RoadLineRef> Lines = new List<RoadLineRef>();
    }
}
