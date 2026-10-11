using System.Collections.Generic;
using HighwayEngineer.Common.Roads;

namespace HighwayEngineer.Common.Api
{
    /// <summary>
    /// The built roads and bridges: the server's road library, or a client's copy of it. Read on
    /// the game thread; records are shared, so callers must not change them.
    /// </summary>
    public interface IRoadRouteSource
    {
        /// <summary>Increases whenever a segment is built or removed.</summary>
        int Revision { get; }
        /// <summary>False on a client until the first copy of the library arrived.</summary>
        bool IsReady { get; }
        IEnumerable<long> PlanetIds { get; }
        IEnumerable<RoadSegmentRecord> SegmentsOn(long planetId);
        /// <summary>The segment with this id, or null.</summary>
        RoadSegmentRecord FindSegment(long segmentId);
    }
}
