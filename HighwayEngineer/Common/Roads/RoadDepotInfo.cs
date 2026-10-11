using ProtoBuf;
using VRageMath;

namespace HighwayEngineer.Common.Roads
{
    /// <summary>A Road Logistic Depot as clients see it on the map.</summary>
    [ProtoContract]
    public sealed class RoadDepotInfo
    {
        [ProtoMember(1)] public long GridId;
        [ProtoMember(2)] public long PlanetId;
        /// <summary>Centre of its delivery box.</summary>
        [ProtoMember(3)] public double X;
        [ProtoMember(4)] public double Y;
        [ProtoMember(5)] public double Z;
        /// <summary>The faction that owns it (no station).</summary>
        [ProtoMember(6)] public RoadServiceProvider Faction;

        public Vector3D Position => new Vector3D(X, Y, Z);
    }
}
