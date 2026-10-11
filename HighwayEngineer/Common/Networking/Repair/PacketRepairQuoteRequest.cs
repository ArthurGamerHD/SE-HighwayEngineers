using System.Collections.Generic;
using Generated;
using ProtoBuf;
using HighwayEngineer.Common.Roads;

namespace HighwayEngineer.Common.Networking
{
    /// <summary>Client to server: check repairing built segments (new material, holes filled).</summary>
    [ProtoContract]
    [NetworkPayload(13)]
    public sealed partial class PacketRepairQuoteRequest
    {
        [ProtoMember(1)] public int RequestId;
        [ProtoMember(2)] public long PlanetId;
        /// <summary>Road library segments.</summary>
        [ProtoMember(3)] public long[] SegmentIds;
        /// <summary>Road lines of the voxel API that are not in the library.</summary>
        [ProtoMember(4)] public List<RoadLineRef> Lines = new List<RoadLineRef>();
        /// <summary>Voxel material to pave the roads (and bridge decks) with; null keeps theirs.</summary>
        [ProtoMember(5)] public string Material;
        /// <summary>Fill holes dug into the roads and rebuild their bridges.</summary>
        [ProtoMember(6)] public bool RepairHoles;
        /// <summary>Paint the guide lines on the library segments.</summary>
        [ProtoMember(7)] public bool RepaintGuideLines;
    }
}
