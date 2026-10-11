using System;
using System.Collections.Generic;
using ProceduralCubemapApi.Api;
using HighwayEngineer.Client.Infrastructure;
using HighwayEngineer.Common.Helpers;
using HighwayEngineer.Common.Roads;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Utils;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

namespace HighwayEngineer.Client.Roads
{
    /// <summary>
    /// Paints a double yellow centre line and a white edge line on each side of the built roads of
    /// the planet nearest the camera that have guide lines (<see cref="RoadSegmentRecord.GuideLines"/>,
    /// chosen when built or by a repair), as screen-space decals bound to the planet: the voxels are
    /// never touched. Each road is cut into short straight pieces, one stretched decal per line
    /// each, placed while the camera is near. The decals are exempt from the game's decal queue,
    /// so this class removes them itself.
    /// <para>
    /// At intersections each line stops where it reaches another road's edge line: the centre
    /// line is gone across the other road, and the edge lines meet at the corners. A road's own
    /// next segment (joined end to end, going on the same way) and roads on another level (a
    /// bridge over a road) do not cut its lines.
    /// </para>
    /// </summary>
    internal sealed class RoadMarkingDecals
    {
        // A piece is at most this long, and shorter where the curve strays from its chord.
        const double PieceMaxMeters = 8.0;
        const double PieceMinMeters = 1.0;
        const double MaxSagMeters = 0.03;
        const int MaxSplitDepth = 16;
        // Decals are placed within this ground distance of the camera and removed past the
        // second (the definition draws them up to 400 m).
        const double PlaceRadiusMeters = 600.0;
        const double RemoveRadiusMeters = 800.0;
        // The cubemap API listing is re-read this long after the road library changes, so a road
        // the server just pushed is in it.
        const int ListingRefreshDelayFrames = 300;
        const int UpdateIntervalFrames = 15;
        const int MaxPlacementsPerUpdate = 96;
        // Each texture spans its decal's width: the centre line's two 0.12 m stripes 0.15 m apart,
        // the edge line's one 0.15 m stripe. The box reaches this far above and below the road so
        // coarse terrain LODs still catch the paint.
        const float CenterLineWidthMeters = 0.5f;
        const float EdgeLineWidthMeters = 0.25f;
        const float DecalDepthMeters = 1f;
        const double PieceOverlapMeters = 0.02;
        // Edge lines run this far inside the road's edges, on roads at least this wide.
        const double EdgeInsetMeters = 0.75;
        const double MinEdgeLineRoadWidthMeters = 4.0;
        // Lines are tested against other roads this often along a piece, and their ends then
        // found to within a few centimetres.
        const double ClipStepMeters = 0.25;
        const int ClipRefineSteps = 4;
        // A road cuts lines this far past its ends, so a road ending at another's side covers
        // the seam between them.
        const double ClipEndMeters = 0.3;
        // Lines shorter than this after cutting are left out.
        const double MinLineMeters = 0.3;
        // Two segments sharing an end are one road going on when their directions out of that
        // end are this close to opposite.
        const double ContinuationCos = -0.9;

        static readonly MyStringHash CenterLineSource = MyStringHash.GetOrCompute("HighwayEngineer_CenterLine");
        static readonly MyStringHash EdgeLineSource = MyStringHash.GetOrCompute("HighwayEngineer_EdgeLine");
        static readonly MyStringHash DefaultMaterial = MyStringHash.GetOrCompute("Default");

        /// <summary>A road to paint: on the terrain, or on a bridge deck at the library's deck heights.</summary>
        struct MarkedRoad
        {
            public RoadCurve Curve;
            // Its guide lines are painted; roads without still cut the lines that cross them.
            public bool Painted;
            public bool Bridge;
            // Bridges: deck-top height above the planet's average radius at the start and end.
            public double StartHeight;
            public double EndHeight;
        }

        /// <summary>A road with its pieces, and the cap around it (to find the roads near a piece).</summary>
        sealed class RoadEntry
        {
            public MarkedRoad Road;
            public List<Piece> Pieces;
            public Vector3D CapCenter;
            public double CapAngle;
            // How far from its centre it cuts other roads' lines: its edge lines' offset, or half
            // its width when it has none; 0 when its width is unknown.
            public double ClipHalfWidth;
            // Its ends, and the directions leaving them into the road.
            public Vector3D Start, End, StartInto, EndInto;
        }

        sealed class Piece
        {
            public RoadEntry Owner;
            public Vector3D StartDirection;
            public Vector3D EndDirection;
            public Vector3D MidDirection;
            // Unit directions across the road (to its right) at the piece's ends, so neighbouring
            // pieces' edge lines meet.
            public Vector3D StartRight;
            public Vector3D EndRight;
            public double StartT;
            public double EndT;
            public bool Bridge;
            public double StartHeight;
            public double EndHeight;
            // Edge lines' distance from the centre line; 0 when the road has none.
            public double EdgeOffset;
            public readonly List<uint> DecalIds = new List<uint>(3);
            public bool Placed;
        }

        // Each road, keyed by its geometry, so roads that did not change keep their decals.
        readonly Dictionary<string, RoadEntry> _roads = new Dictionary<string, RoadEntry>();
        // The pieces of other roads near the piece being placed.
        readonly List<Piece> _clippers = new List<Piece>();
        readonly List<uint> _newIds = new List<uint>(1);
        readonly List<MyDecalPositionUpdate> _updates = new List<MyDecalPositionUpdate>();
        // The cubemap API's road lines on the planet, as last read: roads built outside contracts.
        List<RoadCurve> _listed = new List<RoadCurve>();
        MyPlanet _planet;
        int _frame;
        int _refreshAtFrame = -1;
        int _listingGeneration;
        bool _listingRunning;

        /// <summary>The road library changed: re-read the roads shortly.</summary>
        public void OnRoadLibraryChanged(long planetId)
        {
            if (_planet != null && planetId != 0 && planetId != _planet.EntityId) return;
            if (_refreshAtFrame < 0) _refreshAtFrame = _frame + ListingRefreshDelayFrames;
            // The library's own segments show right away; the listing follows.
            if (_planet != null) Refresh(null);
        }

        public void Update()
        {
            _frame++;
            if (!MinimapLocalConfig.Current.RoadMarkings)
            {
                if (_planet != null) Clear();
                return;
            }
            if (_frame % UpdateIntervalFrames != 0) return;
            if (MyAPIGateway.Session == null || MyAPIGateway.Session.Camera == null) return;
            Vector3D camera = MyAPIGateway.Session.Camera.WorldMatrix.Translation;
            MyPlanet planet = MyGamePruningStructure.GetClosestPlanet(camera);
            if (planet != null && Vector3D.Distance(camera, planet.PositionComp.GetPosition()) >
                planet.MaximumRadius + RemoveRadiusMeters)
                planet = null;
            if (planet != _planet)
            {
                Clear();
                _planet = planet;
                if (planet == null) return;
                Refresh(null);
                ReadListing();
            }
            else if (_refreshAtFrame >= 0 && _frame >= _refreshAtFrame)
            {
                _refreshAtFrame = -1;
                ReadListing();
            }
            if (_planet == null || _planet.Closed) return;
            Stream(camera);
        }

        /// <summary>Removes every decal (session end, setting turned off, planet left).</summary>
        public void Clear()
        {
            foreach (RoadEntry road in _roads.Values)
                RemoveDecals(road.Pieces);
            _roads.Clear();
            _listed = new List<RoadCurve>();
            _planet = null;
            _listingGeneration++;
            _listingRunning = false;
            _refreshAtFrame = -1;
        }

        /// <summary>
        /// Rebuilds the road set from the road library and <paramref name="listed"/> (the cubemap
        /// API's road lines; null keeps the roads last read from it).
        /// </summary>
        void Refresh(List<RoadCurve> listed)
        {
            if (listed != null) _listed = listed;
            var curves = new Dictionary<string, MarkedRoad>();
            foreach (RoadCurve curve in _listed)
                curves[Key(curve)] = new MarkedRoad { Curve = curve };
            // The library last: it knows which segments are bridges, and their deck heights.
            HighwayEngineerClientComponent client = HighwayEngineerSessionComponent.Client;
            if (client != null)
                foreach (RoadSegmentRecord segment in client.Library.SegmentsOn(_planet.EntityId))
                {
                    RoadCurve curve;
                    if (!RoadCurve.TryFromLocalGeometry(_planet.WorldMatrix, segment.Geometry, out curve)) continue;
                    string key = Key(curve);
                    MarkedRoad listedRoad;
                    // Older library segments have no width; the listing may know it.
                    curve.WidthMeters = segment.WidthMeters > 0.0 || !curves.TryGetValue(key, out listedRoad)
                        ? segment.WidthMeters
                        : listedRoad.Curve.WidthMeters;
                    curves[key] = new MarkedRoad
                    {
                        Curve = curve,
                        Painted = segment.GuideLines,
                        Bridge = segment.IsBridge,
                        StartHeight = segment.StartHeight,
                        EndHeight = segment.EndHeight
                    };
                }

            // Roads gone, new or changed; the roads around them are painted again too, as their
            // lines are cut differently now.
            var changed = new List<RoadEntry>();
            var gone = new List<string>();
            foreach (KeyValuePair<string, RoadEntry> road in _roads)
                if (!curves.ContainsKey(road.Key))
                {
                    RemoveDecals(road.Value.Pieces);
                    changed.Add(road.Value);
                    gone.Add(road.Key);
                }
            foreach (string key in gone)
                _roads.Remove(key);
            foreach (KeyValuePair<string, MarkedRoad> road in curves)
            {
                RoadEntry entry;
                // A road that became a bridge (or stopped being one), or whose width became known,
                // is painted again.
                if (_roads.TryGetValue(road.Key, out entry) &&
                    (entry.Road.Painted != road.Value.Painted ||
                     entry.Road.Bridge != road.Value.Bridge ||
                     entry.Road.Curve.WidthMeters != road.Value.Curve.WidthMeters))
                {
                    RemoveDecals(entry.Pieces);
                    changed.Add(entry);
                    entry = null;
                }
                if (entry != null) continue;
                entry = CreateEntry(road.Value, _planet.AverageRadius);
                _roads[road.Key] = entry;
                changed.Add(entry);
            }
            foreach (RoadEntry road in _roads.Values)
                foreach (RoadEntry other in changed)
                    if (other != road && CapsOverlap(road, other))
                    {
                        RemoveDecals(road.Pieces);
                        break;
                    }
        }

        static bool CapsOverlap(RoadEntry a, RoadEntry b)
        {
            return Vector3D.Dot(a.CapCenter, b.CapCenter) >= Math.Cos(Math.Min(Math.PI, a.CapAngle + b.CapAngle));
        }

        /// <summary>Reads the cubemap API's road lines on a worker thread, then refreshes.</summary>
        void ReadListing()
        {
            if (_listingRunning || _planet == null || MyAPIGateway.Parallel == null) return;
            ApiProvider api;
            string error;
            if (!RoadApi.TryValidate(out api, out error)) return;
            string xml;
            try { xml = api.GetPlanetModificationsXml(_planet.EntityId); }
            catch (Exception failure)
            {
                ErrorHandlerHelper.LogError(failure, typeof(RoadMarkingDecals));
                return;
            }
            if (string.IsNullOrWhiteSpace(xml)) return;
            MyPlanet planet = _planet;
            MatrixD world = planet.WorldMatrix;
            int generation = ++_listingGeneration;
            _listingRunning = true;
            List<RoadCurve> listed = null;
            Exception parseError = null;
            MyAPIGateway.Parallel.Start(() =>
            {
                try
                {
                    var listing = MyAPIGateway.Utilities.SerializeFromXML<PlanetModificationListing>(xml);
                    listed = new List<RoadCurve>();
                    if (listing == null || listing.Modifications == null) return;
                    foreach (PlanetModificationInfo modification in listing.Modifications)
                    {
                        if (modification == null || modification.Lines == null) continue;
                        foreach (RuntimeProceduralLineOperation line in modification.Lines)
                        {
                            RoadCurve curve;
                            if (line == null || line.Geometry == null ||
                                !string.Equals(line.Layer, "material", StringComparison.OrdinalIgnoreCase) ||
                                !RoadCurve.TryFromLocalGeometry(world, line.Geometry, out curve)) continue;
                            curve.WidthMeters = line.WidthMeters;
                            listed.Add(curve);
                        }
                    }
                }
                catch (Exception failure) { parseError = failure; }
            }, () =>
            {
                if (generation != _listingGeneration || _planet != planet) return;
                _listingRunning = false;
                if (parseError != null)
                {
                    ErrorHandlerHelper.LogError(parseError, typeof(RoadMarkingDecals));
                    return;
                }
                if (listed != null && MinimapLocalConfig.Current.RoadMarkings) Refresh(listed);
            });
        }

        void Stream(Vector3D camera)
        {
            Vector3D center = _planet.PositionComp.GetPosition();
            Vector3D cameraDirection = Vector3D.Normalize(camera - center);
            double radius = _planet.AverageRadius;
            // Ground distance as an angle, so terrain height and camera altitude do not matter.
            double placeCos = Math.Cos(PlaceRadiusMeters / radius);
            double removeCos = Math.Cos(RemoveRadiusMeters / radius);
            int budget = MaxPlacementsPerUpdate;
            _updates.Clear();
            foreach (RoadEntry road in _roads.Values)
            {
                if (!road.Road.Painted) continue;
                foreach (Piece piece in road.Pieces)
                {
                    double cos = Vector3D.Dot(piece.MidDirection, cameraDirection);
                    if (!piece.Placed && cos > placeCos && budget > 0)
                    {
                        budget--;
                        Place(piece, center);
                    }
                    else if (piece.Placed && cos < removeCos)
                        RemoveDecal(piece);
                }
            }
            if (_updates.Count > 0) MyDecals.UpdateDecals(_updates);
        }

        void Place(Piece piece, Vector3D center)
        {
            // Placed even when a decal fails, so a bad piece is not retried every update.
            piece.Placed = true;
            FindClippers(piece);
            PlaceLine(piece, 0.0, CenterLineSource, CenterLineWidthMeters, center);
            if (piece.EdgeOffset <= 0.0) return;
            PlaceLine(piece, piece.EdgeOffset, EdgeLineSource, EdgeLineWidthMeters, center);
            PlaceLine(piece, -piece.EdgeOffset, EdgeLineSource, EdgeLineWidthMeters, center);
        }

        /// <summary>
        /// Places one line of a piece, <paramref name="offset"/> metres right of the centre line:
        /// one decal for each run of it that no other road covers.
        /// </summary>
        void PlaceLine(Piece piece, double offset, MyStringHash source, float width, Vector3D center)
        {
            double radius = _planet.AverageRadius;
            if (_clippers.Count == 0)
            {
                PlaceRun(piece, 0.0, 1.0, offset, source, width, center);
                return;
            }
            double length = Vector3D.Distance(piece.StartDirection, piece.EndDirection) * radius;
            int steps = Math.Max(1, (int)Math.Ceiling(length / ClipStepMeters));
            double runStart = -1.0;
            bool previous = false;
            for (int i = 0; i <= steps; i++)
            {
                double f = i / (double)steps;
                bool visible = !Covered(LineDirection(piece, offset, f, radius), piece);
                if (i > 0 && visible != previous)
                {
                    // The line's end lies between the two samples: halve the gap a few times.
                    double inside = visible ? f : (i - 1) / (double)steps;
                    double outside = visible ? (i - 1) / (double)steps : f;
                    for (int k = 0; k < ClipRefineSteps; k++)
                    {
                        double half = (inside + outside) * 0.5;
                        if (Covered(LineDirection(piece, offset, half, radius), piece)) outside = half;
                        else inside = half;
                    }
                    if (visible) runStart = inside;
                    else
                    {
                        PlaceRun(piece, runStart, inside, offset, source, width, center);
                        runStart = -1.0;
                    }
                }
                else if (i == 0 && visible)
                    runStart = 0.0;
                previous = visible;
            }
            if (runStart >= 0.0)
                PlaceRun(piece, runStart, 1.0, offset, source, width, center);
        }

        /// <summary>The direction of a line <paramref name="offset"/> metres right of the centre, at fraction <paramref name="f"/> along a piece.</summary>
        static Vector3D LineDirection(Piece piece, double offset, double f, double radius)
        {
            Vector3D direction = Vector3D.Normalize(Vector3D.Lerp(piece.StartDirection, piece.EndDirection, f));
            if (offset == 0.0) return direction;
            Vector3D right = Vector3D.Normalize(Vector3D.Lerp(piece.StartRight, piece.EndRight, f));
            return Offset(direction, right, offset, radius);
        }

        /// <summary>
        /// Collects the pieces of other roads that may cover the piece's lines: not its own
        /// road, on the same level, of known width, not the next segment of the same road.
        /// </summary>
        void FindClippers(Piece piece)
        {
            _clippers.Clear();
            RoadEntry owner = piece.Owner;
            double radius = _planet.AverageRadius;
            double reach = (Vector3D.Distance(piece.StartDirection, piece.EndDirection) * radius * 0.5 +
                            piece.EdgeOffset + ClipEndMeters) / radius;
            foreach (RoadEntry road in _roads.Values)
            {
                if (road == owner || road.ClipHalfWidth <= 0.0 || road.Road.Bridge != owner.Road.Bridge ||
                    Vector3D.Dot(piece.MidDirection, road.CapCenter) < Math.Cos(Math.Min(Math.PI, road.CapAngle + reach)) ||
                    Continues(owner, road))
                    continue;
                double near = reach + road.ClipHalfWidth / radius;
                foreach (Piece other in road.Pieces)
                {
                    double half = Vector3D.Distance(other.StartDirection, other.EndDirection) * 0.5;
                    if (Vector3D.Dot(piece.MidDirection, other.MidDirection) >= Math.Cos(Math.Min(Math.PI, near + half)))
                        _clippers.Add(other);
                }
            }
        }

        /// <summary>Whether <paramref name="b"/> is the next segment of road <paramref name="a"/>: joined end to end, going on the same way.</summary>
        static bool Continues(RoadEntry a, RoadEntry b)
        {
            return Continues(a.Start, a.StartInto, b) || Continues(a.End, a.EndInto, b);
        }

        static bool Continues(Vector3D end, Vector3D into, RoadEntry b)
        {
            const double same = 1e-12;
            return Vector3D.DistanceSquared(end, b.Start) < same && Vector3D.Dot(into, b.StartInto) < ContinuationCos ||
                   Vector3D.DistanceSquared(end, b.End) < same && Vector3D.Dot(into, b.EndInto) < ContinuationCos;
        }

        /// <summary>Whether another road (one of <see cref="_clippers"/>) covers direction <paramref name="n"/>.</summary>
        bool Covered(Vector3D n, Piece piece)
        {
            double radius = _planet.AverageRadius;
            foreach (Piece other in _clippers)
            {
                RoadEntry road = other.Owner;
                Vector3D a = other.StartDirection;
                Vector3D ab = other.EndDirection - a;
                double lengthSquared = ab.LengthSquared();
                if (lengthSquared < 1e-24) continue;
                double u = Vector3D.Dot(n - a, ab) / lengthSquared;
                // Each point belongs to the piece it falls on; past the road's ends only a little.
                double spanT = other.EndT - other.StartT;
                double endU = ClipEndMeters / (road.Road.Curve.Arc * radius * spanT);
                double lower = other.StartT <= 0.0 ? -endU : -0.02;
                double upper = other.EndT >= 1.0 ? 1.0 + endU : 1.02;
                if (u < lower || u > upper) continue;
                if ((n - (a + ab * u)).Length() * radius <= road.ClipHalfWidth)
                    return true;
            }
            return false;
        }

        /// <summary>Places the part of a line from fraction <paramref name="f0"/> to <paramref name="f1"/> along a piece.</summary>
        void PlaceRun(Piece piece, double f0, double f1, double offset, MyStringHash source, float width, Vector3D center)
        {
            double radius = _planet.AverageRadius;
            double fm = (f0 + f1) * 0.5;
            double span = piece.EndT - piece.StartT;
            Vector3D start = PointOn(piece, LineDirection(piece, offset, f0, radius), piece.StartT + span * f0, center);
            Vector3D end = PointOn(piece, LineDirection(piece, offset, f1, radius), piece.StartT + span * f1, center);
            Vector3D middle = PointOn(piece, LineDirection(piece, offset, fm, radius), piece.StartT + span * fm, center);
            Vector3D along = end - start;
            double length = along.Length();
            if (length < 1e-3 || (f1 - f0 < 1.0 && length < MinLineMeters)) return;
            along /= length;
            Vector3D up = Vector3D.Normalize(middle - center);
            Vector3D right = Vector3D.Cross(along, up);
            if (right.LengthSquared() < 1e-9) return;
            right.Normalize();
            // The road's own slope: the box lies along the piece, not level with the planet.
            Vector3D normal = Vector3D.Normalize(Vector3D.Cross(right, along));
            if (Vector3D.Dot(normal, up) < 0) normal = -normal;

            var info = new MyDecalRenderInfo
            {
                Flags = MyDecalFlags.World | MyDecalFlags.IgnoreRenderLimits | MyDecalFlags.IgnoreOffScreenDeletion,
                Position = middle,
                Normal = normal,
                Forward = along,
                Source = source,
                PhysicalMaterial = DefaultMaterial,
                RenderObjectIds = _planet.Render.RenderObjectIDs,
                AliveUntil = int.MaxValue,
                IsTrail = false
            };
            _newIds.Clear();
            MyDecals.AddDecal(ref info, _newIds);
            if (_newIds.Count == 0 || _newIds[0] == 0) return;
            uint id = _newIds[0];
            piece.DecalIds.Add(id);

            // Stretch the decal over the piece: X across the road, Y along it, Z the projection depth.
            Matrix transform = Matrix.CreateFromDir((Vector3)normal, (Vector3)along);
            transform.Right *= width;
            transform.Up *= (float)(length + PieceOverlapMeters);
            transform.Forward *= DecalDepthMeters;
            transform.Translation = Vector3.Zero;
            _updates.Add(new MyDecalPositionUpdate { ID = id, Position = middle, Transform = transform });
        }

        static Vector3D Offset(Vector3D direction, Vector3D right, double offset, double radius)
        {
            return offset == 0.0 ? direction : Vector3D.Normalize(direction + right * (offset / radius));
        }

        /// <summary>Where the edge lines run from the centre line; 0 for roads too narrow or of unknown width.</summary>
        static double EdgeOffset(RoadCurve curve)
        {
            return curve.WidthMeters >= MinEdgeLineRoadWidthMeters ? curve.WidthMeters * 0.5 - EdgeInsetMeters : 0.0;
        }

        /// <summary>
        /// The road surface at <paramref name="direction"/>: the terrain, or a bridge's deck top
        /// (its heights vary linearly along the segment, as the deck was built). Decals paint any
        /// surface inside their box, so deck pieces stay bound to the planet.
        /// </summary>
        Vector3D PointOn(Piece piece, Vector3D direction, double t, Vector3D center)
        {
            if (piece.Bridge)
                return center + direction * (_planet.AverageRadius + piece.StartHeight +
                                             (piece.EndHeight - piece.StartHeight) * t);
            double top = Math.Max(_planet.AverageRadius, _planet.MaximumRadius);
            return _planet.GetClosestSurfacePointGlobal(center + direction * top);
        }

        static void RemoveDecals(List<Piece> pieces)
        {
            foreach (Piece piece in pieces)
                RemoveDecal(piece);
        }

        static void RemoveDecal(Piece piece)
        {
            foreach (uint id in piece.DecalIds)
                MyDecals.RemoveDecal(id, true);
            piece.DecalIds.Clear();
            piece.Placed = false;
        }

        /// <summary>
        /// A road cut into straight pieces that follow its curve within <see cref="MaxSagMeters"/>,
        /// with the cap around it and its ends.
        /// </summary>
        static RoadEntry CreateEntry(MarkedRoad road, double radius)
        {
            RoadCurve curve = road.Curve;
            var entry = new RoadEntry
            {
                Road = road,
                Pieces = new List<Piece>(),
                Start = curve.Evaluate(0.0),
                End = curve.Evaluate(1.0),
                StartInto = curve.TangentAt(0.0),
                EndInto = -curve.TangentAt(1.0)
            };
            double edge = EdgeOffset(curve);
            entry.ClipHalfWidth = edge > 0.0 ? edge : curve.WidthMeters * 0.5;
            int count = Math.Max(1, (int)Math.Ceiling(curve.Arc * radius / PieceMaxMeters));
            for (int i = 0; i < count; i++)
                Cut(entry, radius, i / (double)count, (i + 1) / (double)count, 0);
            entry.CapCenter = curve.Evaluate(0.5);
            double cap = 0.0;
            foreach (Piece piece in entry.Pieces)
                cap = Math.Max(cap, Math.Max(
                    Math.Acos(MathHelper.Clamp(Vector3D.Dot(entry.CapCenter, piece.StartDirection), -1.0, 1.0)),
                    Math.Acos(MathHelper.Clamp(Vector3D.Dot(entry.CapCenter, piece.EndDirection), -1.0, 1.0))));
            entry.CapAngle = cap + (Math.Max(entry.ClipHalfWidth, edge) + ClipEndMeters) / radius;
            return entry;
        }

        static void Cut(RoadEntry entry, double radius, double t0, double t1, int depth)
        {
            MarkedRoad road = entry.Road;
            RoadCurve curve = road.Curve;
            Vector3D a = curve.Evaluate(t0), b = curve.Evaluate(t1);
            double tm = (t0 + t1) * 0.5;
            Vector3D m = curve.Evaluate(tm);
            double length = Vector3D.Distance(a, b) * radius;
            double sag = Vector3D.Distance(m, Vector3D.Normalize(a + b)) * radius;
            if (depth < MaxSplitDepth && length > PieceMinMeters * 2 && sag > MaxSagMeters)
            {
                Cut(entry, radius, t0, tm, depth + 1);
                Cut(entry, radius, tm, t1, depth + 1);
                return;
            }
            entry.Pieces.Add(new Piece
            {
                Owner = entry,
                StartDirection = a,
                EndDirection = b,
                MidDirection = Vector3D.Normalize(a + b),
                StartRight = RightAt(curve, t0, a),
                EndRight = RightAt(curve, t1, b),
                StartT = t0,
                EndT = t1,
                Bridge = road.Bridge,
                StartHeight = road.StartHeight,
                EndHeight = road.EndHeight,
                EdgeOffset = EdgeOffset(curve)
            });
        }

        static Vector3D RightAt(RoadCurve curve, double t, Vector3D direction)
        {
            return Vector3D.Normalize(Vector3D.Cross(curve.TangentAt(t), direction));
        }

        static string Key(RoadCurve curve)
        {
            return Format(curve.Start) + "|" + Format(curve.End) + "|" +
                   Format(curve.StartTangent) + "|" + Format(curve.EndTangent);
        }

        static string Format(Vector3D v)
        {
            // About a millimetre on a 60 km planet: the same road read twice gets the same key.
            return Math.Round(v.X, 8) + "," + Math.Round(v.Y, 8) + "," + Math.Round(v.Z, 8);
        }
    }
}
