using ProtoBuf;

namespace HighwayEngineer.Common.Roads
{
    /// <summary>The road settings a push is built and validated with.</summary>
    [ProtoContract]
    public sealed class RoadBuildParameters
    {
        /// <summary>Voxel material subtype the road surface is made of.</summary>
        [ProtoMember(1)] public string Material;
        [ProtoMember(2)] public double WidthMeters;
        [ProtoMember(3)] public double DepthMeters;
        /// <summary>Cut/fill side angle; zero when the sides are not cut or filled.</summary>
        [ProtoMember(4)] public double ShoulderAngleDegrees;
        [ProtoMember(5)] public double MaxSlopeDegrees;
        /// <summary>Smoothing window (half-width, metres) for terrain-following roads; averaged roads run at a constant grade between nodes.</summary>
        [ProtoMember(6)] public double SegmentDistanceMeters;
        /// <summary>Level each segment to its average terrain height instead of following it.</summary>
        [ProtoMember(7)] public bool AverageHeight;
        /// <summary>Paint guide lines (road markings) on the built segments.</summary>
        [ProtoMember(8)] public bool GuideLines;
    }
}
