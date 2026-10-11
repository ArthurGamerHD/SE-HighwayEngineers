using System;
using System.Collections.Generic;
using Generated;
using HighwayEngineer.Common.Roads;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRageMath;

namespace HighwayEngineer.Common.Api
{
    /// <summary>
    /// The routes HighwayEngineer built, for other mods (maps, LCDs): every road and bridge segment
    /// with its curve, width and bridge, straight from our road library, so they need not rebuild
    /// roads from the voxel cubemap API's modifications. It answers on servers and clients alike
    /// (a client has a copy of the server's library). Get it with HighwayEngineerClient.TryGet(yourReplyChannel) from
    /// BeforeStart or later; poll <see cref="GetRevision"/> to know when to read the routes again.
    /// Call it on the game thread.
    /// </summary>
    [ApiProvider(ClientNamespace = "HighwayEngineer.Api", ClientName = "HighwayEngineerApi")]
    internal partial class HighwayEngineerApiServer
    {
        const int ApiVersion = 1;

        readonly Func<IRoadRouteSource> _source;

        public HighwayEngineerApiServer(Func<IRoadRouteSource> source)
        {
            _source = source;
        }

        /// <summary>Version of this API; methods are only ever added, so check it is at least what you use.</summary>
        [ApiMethod]
        public int GetApiVersion()
        {
            return ApiVersion;
        }

        /// <summary>
        /// False until the routes are known: on a client, until the server's library arrived.
        /// The other methods then return nothing.
        /// </summary>
        [ApiMethod]
        public bool IsReady()
        {
            IRoadRouteSource source = _source();
            return source != null && source.IsReady;
        }

        /// <summary>Increases whenever a road or bridge is built or bulldozed; read the routes again when it changes.</summary>
        [ApiMethod]
        public int GetRevision()
        {
            IRoadRouteSource source = _source();
            return source == null ? 0 : source.Revision;
        }

        /// <summary>Entity ids of the planets that have routes.</summary>
        [ApiMethod]
        public long[] GetPlanetIds()
        {
            IRoadRouteSource source = _source();
            return source == null ? new long[0] : new List<long>(source.PlanetIds).ToArray();
        }

        /// <summary>Ids of every road and bridge segment on a planet; empty when it has none.</summary>
        [ApiMethod]
        public long[] GetSegmentIds(long planetId)
        {
            var ids = new List<long>();
            IRoadRouteSource source = _source();
            if (source != null)
                foreach (RoadSegmentRecord segment in source.SegmentsOn(planetId))
                    ids.Add(segment.SegmentId);
            return ids.ToArray();
        }

        /// <summary>
        /// A segment's curve as 12 planet-local values (start, end, start tangent, end tangent: unit
        /// directions in the planet's frame), the voxel cubemap API's line geometry; null when unknown.
        /// </summary>
        [ApiMethod]
        public double[] GetSegmentGeometry(long segmentId)
        {
            RoadSegmentRecord segment = Find(segmentId);
            return segment == null || segment.Geometry == null ? null : (double[])segment.Geometry.Clone();
        }

        /// <summary>The planet a segment is on; 0 when unknown.</summary>
        [ApiMethod]
        public long GetSegmentPlanetId(long segmentId)
        {
            RoadSegmentRecord segment = Find(segmentId);
            return segment == null ? 0 : segment.PlanetId;
        }

        /// <summary>A segment's width in metres; 0 when unknown.</summary>
        [ApiMethod]
        public double GetSegmentWidth(long segmentId)
        {
            RoadSegmentRecord segment = Find(segmentId);
            return segment == null ? 0.0 : segment.WidthMeters;
        }

        /// <summary>The bridge a segment is part of; 0 for a road segment (or an unknown one).</summary>
        [ApiMethod]
        public long GetSegmentBridgeId(long segmentId)
        {
            RoadSegmentRecord segment = Find(segmentId);
            return segment == null ? 0 : segment.BridgeId;
        }

        /// <summary>A bridge's segments, in order along it; empty when unknown.</summary>
        [ApiMethod]
        public long[] GetBridgeSegmentIds(long bridgeId)
        {
            var ids = new List<long>();
            RoadSegmentRecord first = FindBridgeSegment(bridgeId);
            if (first != null)
                foreach (RoadSegmentRecord segment in _source().SegmentsOn(first.PlanetId))
                    if (segment.BridgeId == bridgeId)
                        ids.Add(segment.SegmentId);
            return ids.ToArray();
        }

        /// <summary>A bridge's design: "beam" or "arch"; null when unknown.</summary>
        [ApiMethod]
        public string GetBridgeDesign(long bridgeId)
        {
            RoadSegmentRecord segment = FindBridgeSegment(bridgeId);
            if (segment == null) return null;
            return segment.Design == BridgeDesign.Arch ? "arch" : "beam";
        }

        /// <summary>
        /// <paramref name="count"/> world points evenly along a segment's centreline (2 to 1024),
        /// on the road surface: the terrain under a road, the deck top of a bridge. Null when the
        /// segment or its planet is unknown.
        /// </summary>
        [ApiMethod]
        public Vector3D[] GetSegmentPoints(long segmentId, int count)
        {
            RoadSegmentRecord segment = Find(segmentId);
            var planet = segment == null ? null : MyAPIGateway.Entities.GetEntityById(segment.PlanetId) as MyPlanet;
            RoadCurve curve;
            if (planet == null || !RoadCurve.TryFromLocalGeometry(planet.WorldMatrix, segment.Geometry, out curve))
                return null;
            count = Math.Max(2, Math.Min(1024, count));
            Vector3D center = planet.PositionComp.GetPosition();
            double top = Math.Max(planet.AverageRadius, planet.MaximumRadius);
            var points = new Vector3D[count];
            for (int i = 0; i < count; i++)
            {
                double t = i / (double)(count - 1);
                Vector3D direction = curve.Evaluate(t);
                points[i] = segment.IsBridge
                    ? center + direction * (planet.AverageRadius + segment.StartHeight +
                                            (segment.EndHeight - segment.StartHeight) * t)
                    : planet.GetClosestSurfacePointGlobal(center + direction * top);
            }
            return points;
        }

        RoadSegmentRecord Find(long segmentId)
        {
            IRoadRouteSource source = _source();
            return source == null ? null : source.FindSegment(segmentId);
        }

        RoadSegmentRecord FindBridgeSegment(long bridgeId)
        {
            IRoadRouteSource source = _source();
            if (source == null || bridgeId == 0) return null;
            foreach (long planetId in source.PlanetIds)
                foreach (RoadSegmentRecord segment in source.SegmentsOn(planetId))
                    if (segment.BridgeId == bridgeId)
                        return segment;
            return null;
        }
    }
}
