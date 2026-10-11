using ProtoBuf;

namespace HighwayEngineer.Common.Roads
{
    /// <summary>
    /// A road line of the voxel cubemap API that is not in our road library (built some other
    /// way), as the planner lists it. The server checks it against its own listing before use.
    /// </summary>
    [ProtoContract]
    public sealed class RoadLineRef
    {
        [ProtoMember(1)] public string ModificationId;
        [ProtoMember(2)] public int LineIndex;
        /// <summary>The line's planet-local geometry as listed, so a line that moved is not removed by mistake.</summary>
        [ProtoMember(3)] public double[] Geometry;
    }
}
