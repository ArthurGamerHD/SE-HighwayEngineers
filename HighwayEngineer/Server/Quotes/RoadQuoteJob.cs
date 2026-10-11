using System;
using System.Collections.Generic;
using HighwayEngineer.Common.Helpers;
using HighwayEngineer.Common.Networking;
using HighwayEngineer.Common.Roads;
using HighwayEngineer.Common.Structures;
using HighwayEngineer.Server.Config;
using HighwayEngineer.Server.Structures;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRageMath;

namespace HighwayEngineer.Server.Quotes
{
    /// <summary>
    /// One quote being computed, for a road whose segments may be bridged. Cheap checks run on the
    /// game thread in <see cref="TryCreate"/>; the terrain work runs on a background thread in
    /// <see cref="Run"/>, step by step (one segment, crossing row or bridge each) so a cancelled job
    /// stops early. Bridged segments skip the road's slope and earthwork steps; each run of them
    /// is planned as one bridge instead.
    /// </summary>
    sealed class RoadQuoteJob
    {
        const int MinStepsPerSegment = 4, MaxStepsPerSegment = 1024;
        // Road edge to depot edge, so the depot is clear of the road's own earthwork.
        const double DepotClearanceMeters = 15.0;
        // Steepest terrain grade under the depot footprint that counts as flat ground.
        const double DepotMaxGradeDegrees = 5.0;
        // Depot sites are tried beside the road, up to this far along it from the first node,
        // and up to DepotOutwardSteps steps further from it, nearest first.
        const double DepotSearchAlongMeters = 300.0;
        // A bridge's depot stays by its start, not out over the gap it crosses.
        const double BridgeDepotSearchAlongMeters = 40.0;
        const double DepotSearchStepMeters = 20.0;
        const int DepotOutwardSteps = 5;
        // Spacing of the centerline samples tested against a depot footprint.
        const double DepotRoadSampleMeters = 5.0;
        // Terrain samples per footprint side.
        const int DepotGradeSamples = 3;

        enum Stage { Heights, Slope, Crossings, Earthwork, Bridges, Depot, Done }

        struct DepotSite
        {
            public Vector3D Up;
            public Vector3D Forward;
            public double Cost;
            public int Order;
        }

        public readonly ulong SteamId;
        public readonly PacketRoadQuoteRequest Request;
        public readonly bool StoneMaterial;
        /// <summary>The bridges' road fill yields stone (always true without bridges).</summary>
        public readonly bool BridgeStoneMaterial;
        public readonly bool Exempt;
        /// <summary>The runs of bridged segments, in order.</summary>
        public readonly List<BridgeRun> BridgeRuns;
        /// <summary>The planned bridge of each run, once the Bridges step is done.</summary>
        public readonly List<BridgePlan> BridgePlans = new List<BridgePlan>();

        readonly MyPlanet _planet;
        readonly Vector3D _center;
        readonly double _radius;
        readonly double _maximumRadius;
        readonly List<RoadCurve> _curves;
        readonly int[] _steps;
        readonly double[] _heights;
        readonly List<int> _segmentNumbers;
        Stage _stage;
        int _index;

        public RoadRejectReason Reason;
        public string Message;
        public int InvalidSegment;
        public RoadEarthworkEstimate Estimate;
        /// <summary>
        /// Where a new Road Logistic Depot would stand: the nearest site beside the road whose
        /// terrain grade is under <see cref="DepotMaxGradeDegrees"/>, else the flattest one tried.
        /// </summary>
        public MatrixD DepotPivot;
        public double DepotGradeDegrees = double.MaxValue;

        List<DepotSite> _depotSites;
        // Roads a depot must keep clear of: this road and the planet's other roads.
        readonly List<RoadCurve> _depotObstacles = new List<RoadCurve>();

        RoadQuoteJob(ulong steamId, PacketRoadQuoteRequest request, MyPlanet planet,
            List<RoadCurve> curves, int[] steps, bool stoneMaterial, bool bridgeStoneMaterial, bool exempt)
        {
            SteamId = steamId;
            Request = request;
            StoneMaterial = stoneMaterial;
            BridgeStoneMaterial = bridgeStoneMaterial;
            Exempt = exempt;
            BridgeRuns = request.Bridge != null ? request.Bridge.Runs(curves.Count) : new List<BridgeRun>();
            _planet = planet;
            _center = planet.PositionComp.GetPosition();
            _radius = planet.AverageRadius;
            _maximumRadius = Math.Max(planet.AverageRadius, planet.MaximumRadius);
            _curves = curves;
            _steps = steps;
            _heights = new double[curves.Count + 1];
            _segmentNumbers = new List<int>(curves.Count);
            for (int i = 0; i < curves.Count; i++)
                _segmentNumbers.Add(i + 1);
            foreach (RoadCurve curve in curves)
            {
                RoadCurve own = curve;
                own.WidthMeters = request.Parameters.WidthMeters;
                _depotObstacles.Add(own);
            }
        }

        /// <summary>
        /// Other roads on the planet (curves with <see cref="RoadCurve.WidthMeters"/> set) that a
        /// new depot must not stand on. Game thread, before the job starts.
        /// </summary>
        public void AvoidRoads(IEnumerable<RoadCurve> roads)
        {
            if (roads != null)
                _depotObstacles.AddRange(roads);
        }

        public bool IsDone => _stage == Stage.Done;
        public bool HasBridges => BridgeRuns.Count > 0;

        /// <summary>Whether a segment (0-based) is part of a bridge rather than built as road.</summary>
        public bool IsBridged(int segment) => Request.Bridge != null && Request.Bridge.IsBridged(segment);

        /// <summary>Whether any segment is built as road (and so needs the voxel API).</summary>
        public bool HasRoad
        {
            get
            {
                for (int i = 0; i < _curves.Count; i++)
                    if (!IsBridged(i)) return true;
                return false;
            }
        }
        public MyPlanet Planet => _planet;
        public double Radius => _radius;
        public int SegmentCount => _curves.Count;

        public double HeightAt(Vector3D direction)
        {
            return Vector3D.Distance(_planet.GetClosestSurfacePointGlobal(
                _center + direction * _maximumRadius), _center) - _radius;
        }

        /// <summary>Validates everything that needs no terrain; null with a reason when rejected.</summary>
        public static RoadQuoteJob TryCreate(ulong steamId, PacketRoadQuoteRequest request,
            RoadServerSettings settings, bool exempt, out RoadRejectReason reason, out string message)
        {
            reason = RoadRejectReason.InvalidGeometry;
            message = null;
            var planet = MyAPIGateway.Entities.GetEntityById(request.PlanetId) as MyPlanet;
            if (planet == null || planet.MarkedForClose)
            {
                message = LocHelper.Message("HighwayEngineer_Quote_PlanetNotFound");
                return null;
            }

            RoadBuildParameters parameters = request.Parameters;
            if (parameters == null ||
                !InRange(parameters.WidthMeters, RoadLimits.MinWidthMeters, RoadLimits.MaxWidthMeters) ||
                !InRange(parameters.DepthMeters, RoadLimits.MinDepthMeters, RoadLimits.MaxDepthMeters) ||
                !InRange(parameters.MaxSlopeDegrees, RoadLimits.MinSlopeDegrees, RoadLimits.MaxSlopeDegrees) ||
                !InRange(parameters.ShoulderAngleDegrees, RoadLimits.MinShoulderAngleDegrees,
                    RoadLimits.MaxShoulderAngleDegrees) ||
                !InRange(parameters.SegmentDistanceMeters, RoadLimits.MinSegmentDistanceMeters,
                    RoadLimits.MaxSegmentDistanceMeters))
            {
                reason = RoadRejectReason.OutOfLimits;
                message = LocHelper.Message("HighwayEngineer_Quote_RoadSettingsRange");
                return null;
            }

            BridgeBuildParameters bridge = request.Bridge;
            if (bridge != null)
            {
                BridgeDesign design = bridge.Design;
                if (design != BridgeDesign.Auto && design != BridgeDesign.Beam && design != BridgeDesign.Arch ||
                    !InRange(bridge.SegmentMeters, RoadLimits.MinSegmentDistanceMeters,
                        RoadLimits.MaxSegmentDistanceMeters))
                {
                    reason = RoadRejectReason.OutOfLimits;
                    message = LocHelper.Message("HighwayEngineer_Quote_BridgeSettingsRange");
                    return null;
                }
                if (bridge.NodeLifts != null)
                    foreach (double lift in bridge.NodeLifts)
                        if (!InRange(lift, 0.0, RoadLimits.MaxBridgeHeightMeters))
                        {
                            reason = RoadRejectReason.OutOfLimits;
                            message = LocHelper.Message("HighwayEngineer_Quote_LiftTooHigh", RoadLimits.MaxBridgeHeightMeters);
                            return null;
                        }
                if (FindMaterial(bridge.StructureMaterial) == null)
                {
                    reason = RoadRejectReason.MaterialNotAllowed;
                    message = LocHelper.Message("HighwayEngineer_Error_UnknownMaterial", VoxelMaterialNames.Message(bridge.StructureMaterial));
                    return null;
                }
                if (!string.IsNullOrWhiteSpace(bridge.PavementMaterial) && FindMaterial(bridge.PavementMaterial) == null)
                {
                    reason = RoadRejectReason.MaterialNotAllowed;
                    message = LocHelper.Message("HighwayEngineer_Error_UnknownMaterial", VoxelMaterialNames.Message(bridge.PavementMaterial));
                    return null;
                }
            }

            MyVoxelMaterialDefinition material = FindMaterial(parameters.Material);
            if (material == null)
            {
                reason = RoadRejectReason.MaterialNotAllowed;
                message = LocHelper.Message("HighwayEngineer_Error_UnknownMaterial", VoxelMaterialNames.Message(parameters.Material));
                return null;
            }
            bool stone = string.Equals(material.MinedOre, "Stone", StringComparison.OrdinalIgnoreCase);

            double[] geometry = request.Geometry;
            int perSegment = Constants.GEOMETRY_VALUES_PER_SEGMENT;
            if (geometry == null || geometry.Length == 0 || geometry.Length % perSegment != 0)
            {
                message = LocHelper.Message("HighwayEngineer_Quote_GeometryMalformed");
                return null;
            }
            int count = geometry.Length / perSegment;
            if (bridge != null && (bridge.Segments != null && bridge.Segments.Length != count ||
                                   bridge.NodeLifts != null && bridge.NodeLifts.Length != count + 1))
            {
                message = LocHelper.Message("HighwayEngineer_Quote_BridgeMismatch");
                return null;
            }
            // Only segments built as road use the road material.
            if (!stone && !exempt && (bridge == null || !bridge.BridgesAll(count)))
            {
                reason = RoadRejectReason.MaterialNotAllowed;
                message = LocHelper.Message("HighwayEngineer_Quote_NotStone", material.Id.SubtypeName);
                return null;
            }
            bool bridgeStone = stone;
            if (bridge != null && !string.IsNullOrWhiteSpace(bridge.PavementMaterial))
            {
                MyVoxelMaterialDefinition fill = FindMaterial(bridge.PavementMaterial);
                bridgeStone = string.Equals(fill.MinedOre, "Stone", StringComparison.OrdinalIgnoreCase);
                if (!bridgeStone && !exempt)
                {
                    reason = RoadRejectReason.MaterialNotAllowed;
                    message = LocHelper.Message("HighwayEngineer_Quote_NotStone", fill.Id.SubtypeName);
                    return null;
                }
            }
            if (count > settings.MaxSegmentsPerRoad)
            {
                reason = RoadRejectReason.OutOfLimits;
                message = LocHelper.Message("HighwayEngineer_Quote_TooManySegments", settings.MaxSegmentsPerRoad);
                return null;
            }

            var curves = new List<RoadCurve>(count);
            var values = new double[perSegment];
            MatrixD world = planet.WorldMatrix;
            double length = 0.0;
            for (int i = 0; i < count; i++)
            {
                Array.Copy(geometry, i * perSegment, values, 0, perSegment);
                RoadCurve curve;
                if (!RoadCurve.TryFromLocalGeometry(world, values, out curve) ||
                    i > 0 && Vector3D.Dot(curves[i - 1].End, curve.Start) < 1.0 - 1e-9)
                {
                    message = LocHelper.Message("HighwayEngineer_Quote_SegmentDegenerate", i + 1);
                    return null;
                }
                curves.Add(curve);
                length += curve.Arc * planet.AverageRadius;
            }
            // Bound the terrain sampling: long roads get coarser cross-sections.
            double stepMeters = Math.Max(20.0, length / settings.MaxQuoteSamples);
            var steps = new int[count];
            for (int i = 0; i < count; i++)
                steps[i] = Math.Max(MinStepsPerSegment, Math.Min(MaxStepsPerSegment,
                    (int)Math.Ceiling(curves[i].Arc * planet.AverageRadius / stepMeters)));

            reason = RoadRejectReason.None;
            return new RoadQuoteJob(steamId, request, planet, curves, steps, stone, bridgeStone, exempt);
        }

        /// <summary>Does one unit of work; true once the job is finished (accepted or rejected).</summary>
        public bool Step()
        {
            if (_planet.MarkedForClose)
                return Reject(RoadRejectReason.InvalidGeometry, LocHelper.Message("HighwayEngineer_Quote_PlanetRemoved"), 0);
            switch (_stage)
            {
                case Stage.Heights:
                    // The server samples its own terrain; node heights are never trusted from the client.
                    _heights[0] = HeightAt(_curves[0].Start);
                    for (int i = 0; i < _curves.Count; i++)
                        _heights[i + 1] = HeightAt(_curves[i].End);
                    _stage = Stage.Slope;
                    _index = 0;
                    break;
                case Stage.Slope:
                    // A bridge does not follow the terrain: its own grade is checked when it is planned.
                    if (!IsBridged(_index) && RoadGeometry.ExceedsSlope(_curves[_index], _radius, _heights[_index],
                            _heights[_index + 1], Request.Parameters))
                        return Reject(RoadRejectReason.SlopeExceeded, LocHelper.Message("HighwayEngineer_Quote_SlopeExceeded",
                            _index + 1, Request.Parameters.MaxSlopeDegrees), _index + 1);
                    if (++_index >= _curves.Count)
                    {
                        _stage = Stage.Crossings;
                        _index = 0;
                    }
                    break;
                case Stage.Crossings:
                    for (int j = _index + 1; j < _curves.Count; j++)
                        if (RoadGeometry.FindRoadCrossings(_curves[_index], _curves[j], null,
                                _segmentNumbers[j] == _segmentNumbers[_index] + 1))
                            return Reject(RoadRejectReason.SelfCrossing, LocHelper.Message("HighwayEngineer_Quote_Crossing",
                                _index + 1, j + 1), _index + 1);
                    if (++_index >= _curves.Count)
                    {
                        _stage = Stage.Earthwork;
                        _index = 0;
                    }
                    break;
                case Stage.Earthwork:
                    if (!IsBridged(_index))
                        RoadEarthwork.Add(ref Estimate, RoadEarthwork.EstimateSegment(_curves[_index], _radius,
                            HeightAt, _heights[_index], _heights[_index + 1], Request.Parameters, _steps[_index]));
                    if (++_index >= _curves.Count)
                    {
                        _stage = HasBridges ? Stage.Bridges : Stage.Depot;
                        _index = 0;
                        if (!HasBridges) _depotSites = DepotSites();
                    }
                    break;
                case Stage.Bridges:
                {
                    BridgeRun run = BridgeRuns[_index];
                    var curves = new List<RoadCurve>();
                    Vector3D start, end;
                    double[] pins;
                    if (!BridgePlanner.TryReadRun(_planet, Request.Geometry, run, Request.Bridge.NodeLifts, curves,
                            out start, out end, out pins))
                        return Reject(RoadRejectReason.InvalidGeometry, LocHelper.Message("HighwayEngineer_Quote_BridgeGeometryMalformed"), run.First + 1);
                    string error;
                    BridgePlan plan = BridgePlanner.Plan(_planet, curves, start, end, pins,
                        BridgeOptionsFor(Request, Request.Bridge.Design), out error);
                    if (plan == null)
                        return Reject(RoadRejectReason.InvalidGeometry, LocHelper.Message("HighwayEngineer_Quote_BridgeProblem", BridgeName(run), error), run.First + 1);
                    BridgePlans.Add(plan);
                    if (++_index >= BridgeRuns.Count)
                    {
                        _stage = Stage.Depot;
                        _index = 0;
                        _depotSites = DepotSites();
                    }
                    break;
                }
                case Stage.Depot:
                    // One site per step; the first clear and flat enough one wins (sites are
                    // nearest first). Sites on a road are never used while a clear one exists.
                    DepotSite site = _depotSites[_index];
                    double grade = double.MaxValue;
                    if (!OnRoad(site))
                    {
                        grade = DepotGrade(site);
                        if (grade < DepotGradeDegrees)
                        {
                            DepotGradeDegrees = grade;
                            DepotPivot = DepotPivotAt(site);
                        }
                    }
                    if (grade < DepotMaxGradeDegrees || ++_index >= _depotSites.Count)
                    {
                        // Every site is on a road: the nearest one beside this road it is.
                        if (DepotGradeDegrees == double.MaxValue)
                            DepotPivot = DepotPivotAt(_depotSites[0]);
                        _stage = Stage.Done;
                    }
                    break;
            }
            return _stage == Stage.Done;
        }

        /// <summary>
        /// Depot sites on both sides of the road, beside points along it from the first node,
        /// the depot's bay parallel to the road. Ordered by distance along the road plus distance
        /// out from it.
        /// </summary>
        List<DepotSite> DepotSites()
        {
            double baseOffset = Request.Parameters.WidthMeters * 0.5 + DepotClearanceMeters +
                                Math.Max(RoadLogisticDepot.FootprintLeft, RoadLogisticDepot.FootprintRight);
            var sites = new List<DepotSite>();
            int curve = 0;
            double curveStart = 0.0;
            double searchAlong = IsBridged(0) ? BridgeDepotSearchAlongMeters : DepotSearchAlongMeters;
            for (double along = 0.0; along <= searchAlong; along += DepotSearchStepMeters)
            {
                while (curve < _curves.Count && along > curveStart + _curves[curve].Arc * _radius)
                    curveStart += _curves[curve++].Arc * _radius;
                if (curve >= _curves.Count) break;
                double length = _curves[curve].Arc * _radius;
                double t = length > 0.0 ? (along - curveStart) / length : 0.0;
                Vector3D point = _curves[curve].Evaluate(t);
                Vector3D tangent = _curves[curve].TangentAt(t);
                Vector3D side = Vector3D.Cross(point, tangent);
                if (side.Normalize() < 1e-8)
                    side = Vector3D.CalculatePerpendicularVector(point);
                for (int outward = 0; outward < DepotOutwardSteps; outward++)
                {
                    double angle = (baseOffset + outward * DepotSearchStepMeters) / _radius;
                    for (int sign = 1; sign >= -1; sign -= 2)
                    {
                        Vector3D up = Vector3D.Normalize(point * Math.Cos(angle) + side * (sign * Math.Sin(angle)));
                        Vector3D forward = tangent - up * Vector3D.Dot(tangent, up);
                        if (forward.Normalize() < 1e-8)
                            forward = Vector3D.CalculatePerpendicularVector(up);
                        sites.Add(new DepotSite
                        {
                            Up = up,
                            Forward = forward,
                            Cost = along + outward * DepotSearchStepMeters,
                            Order = sites.Count
                        });
                    }
                }
            }
            sites.Sort((a, b) => a.Cost != b.Cost ? a.Cost.CompareTo(b.Cost) : a.Order.CompareTo(b.Order));
            return sites;
        }

        /// <summary>
        /// True when a road (or its clearance) reaches into the depot footprint at a site:
        /// road centerline samples are measured against the footprint rectangle in the site's
        /// tangent plane.
        /// </summary>
        bool OnRoad(DepotSite site)
        {
            Vector3D right = Vector3D.Cross(site.Forward, site.Up);
            // Footprint rectangle: x along Right, y along Forward (grid -Z).
            double centerX = (RoadLogisticDepot.FootprintRight - RoadLogisticDepot.FootprintLeft) * 0.5;
            double centerY = (RoadLogisticDepot.FootprintForward - RoadLogisticDepot.FootprintBack) * 0.5;
            double halfX = (RoadLogisticDepot.FootprintLeft + RoadLogisticDepot.FootprintRight) * 0.5;
            double halfY = (RoadLogisticDepot.FootprintForward + RoadLogisticDepot.FootprintBack) * 0.5;
            double reach = Math.Sqrt(halfX * halfX + halfY * halfY) + Math.Abs(centerX) + Math.Abs(centerY);
            foreach (RoadCurve road in _depotObstacles)
            {
                // Half a metre of slack: sites beside this road sit exactly at its clearance.
                double clearance = road.WidthMeters * 0.5 + DepotClearanceMeters - 0.5;
                double length = road.Arc * _radius;
                double cull = length + reach + clearance;
                if (ArcMeters(site.Up, road.Start) > cull && ArcMeters(site.Up, road.End) > cull)
                    continue;
                int steps = Math.Max(1, (int)Math.Ceiling(length / DepotRoadSampleMeters));
                for (int i = 0; i <= steps; i++)
                {
                    Vector3D point = road.Evaluate(i / (double)steps);
                    if (Vector3D.Dot(point, site.Up) <= 0.0) continue;
                    Vector3D offset = (point - site.Up) * _radius;
                    double x = Math.Max(0.0, Math.Abs(Vector3D.Dot(offset, right) - centerX) - halfX);
                    double y = Math.Max(0.0, Math.Abs(Vector3D.Dot(offset, site.Forward) - centerY) - halfY);
                    if (x * x + y * y < clearance * clearance)
                        return true;
                }
            }
            return false;
        }

        double ArcMeters(Vector3D a, Vector3D b)
        {
            return Math.Acos(MathHelper.Clamp(Vector3D.Dot(a, b), -1.0, 1.0)) * _radius;
        }

        /// <summary>The steepest grade, in degrees, between terrain samples across the depot footprint.</summary>
        double DepotGrade(DepotSite site)
        {
            Vector3D right = Vector3D.Cross(site.Forward, site.Up);
            Vector3D origin = site.Up * _radius;
            int count = DepotGradeSamples * DepotGradeSamples;
            var offsets = new Vector2D[count];
            var heights = new double[count];
            for (int i = 0; i < DepotGradeSamples; i++)
            for (int j = 0; j < DepotGradeSamples; j++)
            {
                double u = (double)i / (DepotGradeSamples - 1);
                double v = (double)j / (DepotGradeSamples - 1);
                // Grid local x is Right and local -z is Forward (see RoadLogisticDepot.Footprint).
                double x = -RoadLogisticDepot.FootprintLeft +
                           u * (RoadLogisticDepot.FootprintLeft + RoadLogisticDepot.FootprintRight);
                double z = -RoadLogisticDepot.FootprintForward +
                           v * (RoadLogisticDepot.FootprintForward + RoadLogisticDepot.FootprintBack);
                int k = i * DepotGradeSamples + j;
                offsets[k] = new Vector2D(x, z);
                heights[k] = HeightAt(Vector3D.Normalize(origin + right * x - site.Forward * z));
            }
            double steepest = 0.0;
            for (int a = 0; a < count; a++)
            for (int b = a + 1; b < count; b++)
                steepest = Math.Max(steepest,
                    Math.Abs(heights[a] - heights[b]) / Vector2D.Distance(offsets[a], offsets[b]));
            return Math.Atan(steepest) * 180.0 / Math.PI;
        }

        /// <summary>The depot pivot at a site: the bay floor sits on the terrain under the pivot.</summary>
        MatrixD DepotPivotAt(DepotSite site)
        {
            Vector3D pivot = _center + site.Up * (_radius + HeightAt(site.Up) + RoadLogisticDepot.PivotHeight);
            return MatrixD.CreateWorld(pivot, site.Forward, site.Up);
        }

        /// <summary>How a request's bridges are planned: its road's width, depth, slope and sides.</summary>
        public static BridgeOptions BridgeOptionsFor(PacketRoadQuoteRequest request, BridgeDesign design)
        {
            return BridgeOptionsFor(request.Parameters, request.Bridge, design);
        }

        public static BridgeOptions BridgeOptionsFor(RoadBuildParameters road, BridgeBuildParameters bridge,
            BridgeDesign design)
        {
            return BridgePlanner.OptionsFor(road, bridge, design);
        }

        /// <summary>"The bridge over segments 3-5", for messages.</summary>
        static string BridgeName(BridgeRun run)
        {
            return run.First == run.Last
                ? LocHelper.Message("HighwayEngineer_Quote_BridgeOverSegment", run.First + 1)
                : LocHelper.Message("HighwayEngineer_Quote_BridgeOverSegments", run.First + 1, run.Last + 1);
        }

        volatile bool _cancelled;

        public bool IsCancelled => _cancelled;

        /// <summary>Asks a running job to stop at its next step (a newer request supersedes it).</summary>
        public void Cancel()
        {
            _cancelled = true;
        }

        /// <summary>
        /// Background thread: does every step. Only reads the planet's terrain and this job's
        /// own state; the result is read back on the game thread once the task completes.
        /// </summary>
        public void Run()
        {
            try
            {
                while (!_cancelled && !Step()) { }
            }
            catch (Exception error)
            {
                Reject(RoadRejectReason.InvalidGeometry, LocHelper.Message("HighwayEngineer_Quote_PricingFailed", error.Message), 0);
            }
        }

        /// <summary>Ends the job early with a rejection (e.g. superseded by a newer request).</summary>
        public bool Reject(RoadRejectReason reason, string message, int invalidSegment)
        {
            Reason = reason;
            Message = message;
            InvalidSegment = invalidSegment;
            _stage = Stage.Done;
            return true;
        }

        static bool InRange(double value, double minimum, double maximum)
        {
            return !double.IsNaN(value) && value >= minimum && value <= maximum;
        }

        static MyVoxelMaterialDefinition FindMaterial(string subtype)
        {
            if (string.IsNullOrWhiteSpace(subtype) || MyDefinitionManager.Static == null)
                return null;
            foreach (MyVoxelMaterialDefinition definition in MyDefinitionManager.Static.GetVoxelMaterialDefinitions())
                if (ConcreteMod.IsOffered(definition) &&
                    string.Equals(definition.Id.SubtypeName, subtype.Trim(), StringComparison.OrdinalIgnoreCase))
                    return definition;
            return null;
        }
    }
}
