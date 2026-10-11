using ProtoBuf;
using HighwayEngineer.Common.Helpers;

namespace HighwayEngineer.Common.Roads
{
    /// <summary>A built road segment, as the server's road library stores it.</summary>
    [ProtoContract]
    public sealed class RoadSegmentRecord
    {
        [ProtoMember(1)] public long SegmentId;
        [ProtoMember(2)] public long PlanetId;
        /// <summary>Planet-local geometry (<see cref="Constants.GEOMETRY_VALUES_PER_SEGMENT"/> values).</summary>
        [ProtoMember(3)] public double[] Geometry;
        [ProtoMember(4)] public double WidthMeters;
        /// <summary>The voxel API modification and line index; null until the API lists it.</summary>
        [ProtoMember(5)] public string ModificationId;
        [ProtoMember(6)] public int LineIndex;
        /// <summary>Session play time when built; drives the bulldoze refund window.</summary>
        [ProtoMember(7)] public double BuiltAtPlaySeconds;
        /// <summary>Credits actually paid for this segment (its share of the contract).</summary>
        [ProtoMember(8)] public long CreditsPaid;
        [ProtoMember(9)] public long OwnerIdentityId;
        /// <summary>The contract that built it.</summary>
        [ProtoMember(10)] public long ContractId;
        /// <summary>The bridge it is part of; 0 for a road segment. A bridge is bulldozed whole.</summary>
        [ProtoMember(11)] public long BridgeId;
        /// <summary>Bridges: the design built.</summary>
        [ProtoMember(12)] public BridgeDesign Design;
        /// <summary>Bridges: deck-top height above the planet's average radius at the segment's start and end.</summary>
        [ProtoMember(13)] public double StartHeight;
        [ProtoMember(14)] public double EndHeight;
        /// <summary>Guide lines (road markings) are painted on it: chosen when it was built, or by a repair.</summary>
        [ProtoMember(15)] public bool GuideLines;

        public bool IsBridge => BridgeId != 0;
    }
}
