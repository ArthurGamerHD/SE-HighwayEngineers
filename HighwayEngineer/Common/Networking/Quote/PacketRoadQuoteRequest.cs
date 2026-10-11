using Generated;
using ProtoBuf;
using HighwayEngineer.Common.Helpers;
using HighwayEngineer.Common.Roads;

namespace HighwayEngineer.Common.Networking
{
    /// <summary>Client to server: price and validate a draft road.</summary>
    [ProtoContract]
    [NetworkPayload(1)]
    public sealed partial class PacketRoadQuoteRequest
    {
        [ProtoMember(1)] public int RequestId;
        [ProtoMember(2)] public long PlanetId;
        [ProtoMember(3)] public RoadBuildParameters Parameters;
        /// <summary>All segments, flattened (<see cref="Constants.GEOMETRY_VALUES_PER_SEGMENT"/> each).
        /// Node heights are not sent: the server samples its own terrain.</summary>
        [ProtoMember(4)] public double[] Geometry;
        /// <summary>Set when some segments are bridges (which, and how); null for a plain road.</summary>
        [ProtoMember(5)] public BridgeBuildParameters Bridge;
    }
}
