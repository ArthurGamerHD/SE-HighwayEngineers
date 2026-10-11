using Acrux.Cartography.Maps;
using Acrux.Cartography;
using System;
using System.Collections.Generic;
using HighwayEngineer.Common.Helpers;
using VRageMath;

namespace HighwayEngineer.Client.Maps
{
    /// <summary>
    /// Trade-off between the three things an automatic road route pays for.
    /// Every term is converted to "cost units"; only the ratios between the
    /// weights matter. With the defaults one metre of road costs 1, a stretch
    /// driven exactly at <see cref="MaxGradeDegrees"/> costs 1 more per metre,
    /// and every cubic metre of cut/fill costs <see cref="Earthwork"/>.
    /// </summary>
    public sealed class RoadRouteWeights
    {
        /// <summary>Cost per metre of road length.</summary>
        public double Distance = 1.0;
        /// <summary>Cost per metre of road driven at the maximum grade. Grows with the
        /// square of the grade, so gentle slopes are cheap and steep ones expensive;
        /// beyond the maximum grade the earthwork term takes over.</summary>
        public double Inclination = 1.0;
        /// <summary>Cost per cubic metre of embankment (cut or fill).</summary>
        public double Earthwork = 0.002;

        /// <summary>Steepest grade a segment may have. Steeper terrain is levelled to this
        /// grade and the extra volume is charged as earthwork; routes with a segment whose
        /// end nodes cannot be joined within it are rejected.</summary>
        public double MaxGradeDegrees = 8.0;
        /// <summary>The editor's "Segment" setting (the API's smoothing window); it sets a
        /// lower bound on the grid cell size.</summary>
        public double SegmentDistanceMeters = 100.0;
        public double RoadWidthMeters = 50.0;
        /// <summary>Embankment side angle.</summary>
        public double FillAngleDegrees = 35.0;

        /// <summary>Grid resolution; zero or less picks one from the road width and length.</summary>
        public double CellSizeMeters;
        /// <summary>Half-width of the search corridor as a fraction of the A-B distance.</summary>
        public double CorridorWidthRatio = 0.35;
        /// <summary>Upper bound on grid cells; the cell size grows to respect it.</summary>
        public int MaxCells = 120000;
        /// <summary>Values above 1 make A* greedier (faster, possibly costlier routes).</summary>
        public double HeuristicWeight = 1.0;
        /// <summary>Cost added per intermediate node, favouring fewer and longer segments.
        /// Negative picks the cost of two flat grid cells.</summary>
        public double NodePenalty = -1.0;
        /// <summary>Longest gap between output nodes; zero or less means no limit, so a
        /// long ramp can climb a cliff the shorter segments cannot.</summary>
        public double MaxNodeSpacingMeters;
        /// <summary>When no valid route is found, A* reruns with the inclination weight
        /// multiplied by 4 up to this many extra times.</summary>
        public int Retries = 2;

        public RoadRouteWeights Clone()
        {
            return new RoadRouteWeights
            {
                Distance = Distance,
                Inclination = Inclination,
                Earthwork = Earthwork,
                MaxGradeDegrees = MaxGradeDegrees,
                SegmentDistanceMeters = SegmentDistanceMeters,
                RoadWidthMeters = RoadWidthMeters,
                FillAngleDegrees = FillAngleDegrees,
                CellSizeMeters = CellSizeMeters,
                CorridorWidthRatio = CorridorWidthRatio,
                MaxCells = MaxCells,
                HeuristicWeight = HeuristicWeight,
                NodePenalty = NodePenalty,
                MaxNodeSpacingMeters = MaxNodeSpacingMeters,
                Retries = Retries
            };
        }
    }

    public sealed class RoadRouteResult
    {
        public bool Success;
        public string Error;
        /// <summary>Surface positions in world space, starting at A and ending at B.</summary>
        public List<Vector3D> Nodes = new List<Vector3D>();
        public double Cost;
        public double LengthMeters;
        /// <summary>Sum of absolute height changes along the terrain under the route.</summary>
        public double ClimbMeters;
        public double EarthworkCubicMeters;
        public int ExpandedCells;
        /// <summary>How many A* searches ran; more than one means the inclination
        /// weight was raised to find a valid route.</summary>
        public int Attempts;
    }

    /// <summary>
    /// A* road routing over the planet surface. The search runs on a grid laid
    /// along the A-B great circle (a rotated latitude/longitude frame whose
    /// equator is the straight route), so distortion is lowest where the road
    /// is most likely to go.
    /// </summary>
    public static class RoadAutoRouter
    {
        // 16-connected moves; the knight moves give 26.6/63.4 degree headings
        // so routes are not forced into 45 degree zig-zags.
        static readonly int[] MoveI = { 1, 1, 0, -1, -1, -1, 0, 1, 2, 1, -1, -2, -2, -1, 1, 2 };
        static readonly int[] MoveJ = { 0, 1, 1, 1, 0, -1, -1, -1, 1, 2, 2, 1, -1, -2, -2, -1 };

        public static RoadRouteResult CreateRoad(PlanetSnapshot planet, Vector3D pointA,
            Vector3D pointB, RoadRouteWeights weights)
        {
            if (planet == null || planet.Planet == null || planet.Planet.MarkedForClose)
                return Fail(LocHelper.GetLoc("HighwayEngineer_Route_NoPlanet"));
            Vector3D center = planet.Position;
            double radius = planet.Radius;
            double maximumRadius = planet.MaximumRadius;
            Func<Vector3D, double> heightAt = direction =>
                Vector3D.Distance(planet.Planet.GetClosestSurfacePointGlobal(
                    center + direction * maximumRadius), center) - radius;
            return CreateRoad(center, radius, heightAt, pointA, pointB, weights);
        }

        /// <summary>
        /// Routes from <paramref name="pointA"/> to <paramref name="pointB"/>.
        /// <paramref name="heightAt"/> maps a unit direction from the planet
        /// centre to the terrain height above <paramref name="radius"/>.
        /// </summary>
        public static RoadRouteResult CreateRoad(Vector3D center, double radius,
            Func<Vector3D, double> heightAt, Vector3D pointA, Vector3D pointB,
            RoadRouteWeights weights)
        {
            if (heightAt == null || radius <= 0.0) return Fail(LocHelper.GetLoc("HighwayEngineer_Route_InvalidPlanet"));
            if (weights == null) weights = new RoadRouteWeights();
            if (weights.MaxGradeDegrees <= 0.0 || weights.MaxGradeDegrees >= 90.0)
                return Fail(LocHelper.GetLoc("HighwayEngineer_Route_SlopeRange"));
            if (weights.RoadWidthMeters <= 0.0 || weights.FillAngleDegrees <= 0.0)
                return Fail(LocHelper.GetLoc("HighwayEngineer_Route_WidthPositive"));
            if (weights.Distance < 0.0 || weights.Inclination < 0.0 || weights.Earthwork < 0.0)
                return Fail(LocHelper.GetLoc("HighwayEngineer_Route_NegativeWeights"));

            Vector3D a = Vector3D.Normalize(pointA - center);
            Vector3D b = Vector3D.Normalize(pointB - center);
            double arc = Math.Acos(MathHelper.Clamp(Vector3D.Dot(a, b), -1.0, 1.0));
            double distance = arc * radius;
            if (distance < 1.0) return Fail(LocHelper.GetLoc("HighwayEngineer_Route_SamePoints"));
            if (arc > Math.PI * 0.9) return Fail(LocHelper.GetLoc("HighwayEngineer_Route_TooFar"));
            double heightA = Vector3D.Distance(pointA, center) - radius;
            double heightB = Vector3D.Distance(pointB, center) - radius;
            var limits = new SegmentLimits(weights);

            weights = weights.Clone();
            int attempts = 0;
            string error = null;
            while (true)
            {
                attempts++;
                RoadRouteResult result = TryRoute(center, radius, heightAt, pointA, pointB,
                    a, b, heightA, heightB, distance, weights, limits, out error);
                if (result != null)
                {
                    result.Attempts = attempts;
                    return result;
                }
                if (attempts > Math.Max(0, weights.Retries)) break;
                weights.Inclination = Math.Max(Math.Max(weights.Inclination, weights.Distance), 1e-3) * 4.0;
            }
            return Fail(error);
        }

        static RoadRouteResult TryRoute(Vector3D center, double radius,
            Func<Vector3D, double> heightAt, Vector3D pointA, Vector3D pointB,
            Vector3D a, Vector3D b, double heightA, double heightB, double distance,
            RoadRouteWeights weights, SegmentLimits limits, out string error)
        {
            var grid = new RouteGrid(radius, heightAt, a, b, distance, weights);
            var model = new RouteCostModel(weights);
            List<int> path = grid.Search(model);
            if (path == null)
            {
                error = LocHelper.GetLoc("HighwayEngineer_Route_NotFound");
                return null;
            }

            var directions = new List<Vector3D>(path.Count);
            var heights = new double[path.Count];
            for (int i = 0; i < path.Count; i++)
            {
                directions.Add(grid.Direction(path[i]));
                heights[i] = grid.Terrain(path[i]);
            }
            directions[0] = a;
            directions[directions.Count - 1] = b;
            heights[0] = heightA;
            heights[heights.Length - 1] = heightB;

            var sampler = new SegmentSampler(radius, heightAt, model, grid.CellSize);
            double maxSpacing = weights.MaxNodeSpacingMeters > 0.0
                ? weights.MaxNodeSpacingMeters : double.PositiveInfinity;
            double nodePenalty = weights.NodePenalty >= 0.0
                ? weights.NodePenalty
                : model.Evaluate(grid.CellSize * 2.0, 0.0, 0.0).Cost;
            List<int> chosen = ChooseNodes(grid, path, directions, heights, model, limits,
                radius, maxSpacing, nodePenalty);
            if (chosen == null)
            {
                error = LocHelper.Format("HighwayEngineer_Route_SlopeUnreachable", weights.MaxGradeDegrees);
                return null;
            }

            var result = new RoadRouteResult { Success = true, ExpandedCells = grid.Expanded };
            for (int k = 0; k < chosen.Count; k++)
            {
                int index = chosen[k];
                if (k > 0)
                {
                    RouteCost piece = sampler.Measure(directions[chosen[k - 1]], directions[index]);
                    result.Cost += piece.Cost;
                    result.LengthMeters += piece.Length;
                    result.ClimbMeters += piece.Climb;
                    result.EarthworkCubicMeters += piece.Volume;
                }
                if (k == 0) result.Nodes.Add(pointA);
                else if (k == chosen.Count - 1) result.Nodes.Add(pointB);
                else result.Nodes.Add(center + directions[index] * (radius + heights[index]));
            }
            error = null;
            return result;
        }

        static RoadRouteResult Fail(string error)
        {
            return new RoadRouteResult { Success = false, Error = error };
        }

        /// <summary>
        /// Picks output nodes from the grid path: a shortest path over the DAG of
        /// "straight segment from path node i to path node j" edges, keeping only
        /// segments whose end heights satisfy the slope limit. Null if none do.
        /// </summary>
        static List<int> ChooseNodes(RouteGrid grid, List<int> cells, List<Vector3D> path,
            double[] heights, RouteCostModel model, SegmentLimits limits, double radius,
            double maxSpacing, double nodePenalty)
        {
            const int Window = 768;
            int count = path.Count;
            var best = new double[count];
            var previous = new int[count];
            for (int i = 0; i < count; i++)
            {
                best[i] = i == 0 ? 0.0 : double.PositiveInfinity;
                previous[i] = -1;
            }
            for (int j = 1; j < count; j++)
            {
                double penalty = j < count - 1 ? nodePenalty : 0.0;
                for (int i = j - 1; i >= Math.Max(0, j - Window); i--)
                {
                    if (double.IsPositiveInfinity(best[i])) continue;
                    double meters = radius * Math.Acos(MathHelper.Clamp(
                        Vector3D.Dot(path[i], path[j]), -1.0, 1.0));
                    if (meters > maxSpacing || !limits.Allows(meters, heights[j] - heights[i]))
                        continue;
                    double candidate = best[i] + penalty;
                    if (candidate >= best[j]) continue;
                    candidate += grid.LineCost(cells[i], cells[j], model);
                    if (candidate < best[j])
                    {
                        best[j] = candidate;
                        previous[j] = i;
                    }
                }
            }
            if (double.IsPositiveInfinity(best[count - 1])) return null;
            var chosen = new List<int>();
            for (int i = count - 1; i >= 0; i = previous[i])
                chosen.Add(i);
            chosen.Reverse();
            return chosen;
        }

        /// <summary>
        /// The editor's validity rule for one segment: the height change between two
        /// nodes must fit under the maximum grade.
        /// </summary>
        public sealed class SegmentLimits
        {
            readonly double _maxGrade;

            public SegmentLimits(RoadRouteWeights weights)
            {
                _maxGrade = Math.Tan(weights.MaxGradeDegrees * Math.PI / 180.0);
            }

            /// <summary>Largest height change a segment of this length may have.</summary>
            public double PermissibleClimb(double meters)
            {
                return meters <= 0.0 ? 0.0 : _maxGrade * meters;
            }

            public bool Allows(double meters, double climb)
            {
                return meters > 0.0 && Math.Abs(climb) <= PermissibleClimb(meters);
            }
        }

        struct RouteCost
        {
            public double Cost;
            public double Length;
            public double Climb;
            public double Volume;
        }

        /// <summary>
        /// Cost of a straight piece of road over terrain. Shared by the grid
        /// search and the simplifier so both optimise the same thing.
        /// </summary>
        sealed class RouteCostModel
        {
            readonly double _distance;
            readonly double _inclination;
            readonly double _earthwork;
            readonly double _maxGrade;
            readonly double _width;
            readonly double _fillSlope;

            public RouteCostModel(RoadRouteWeights weights)
            {
                _distance = weights.Distance;
                _inclination = weights.Inclination;
                _earthwork = weights.Earthwork;
                _maxGrade = Math.Tan(weights.MaxGradeDegrees * Math.PI / 180.0);
                _width = weights.RoadWidthMeters;
                _fillSlope = Math.Tan(weights.FillAngleDegrees * Math.PI / 180.0);
            }

            public double Width => _width;

            /// <param name="length">Horizontal length in metres.</param>
            /// <param name="climb">Terrain height change over the piece.</param>
            /// <param name="crossSlope">Terrain grade perpendicular to the road.</param>
            public RouteCost Evaluate(double length, double climb, double crossSlope)
            {
                var cost = new RouteCost { Length = length, Climb = Math.Abs(climb) };
                if (length <= 0.0) return cost;
                double grade = cost.Climb / length;
                double relative = grade / _maxGrade;

                // Longitudinal: the road is held to the maximum grade; the excess
                // height is cut or filled, ramping from zero to the full excess
                // across the piece. A trapezoid section of depth d has area
                // W d + d^2 / tan(fill); averaging over the ramp gives the terms below.
                double excess = Math.Max(0.0, cost.Climb - _maxGrade * length);
                double longitudinal = length * (_width * excess * 0.5 +
                    excess * excess / (3.0 * _fillSlope));

                // Transverse: levelling a sloped cross-section cuts one half and
                // fills the other, two triangles of (W/2) * (s W/2) / 2.
                double transverse = length * _width * _width * Math.Abs(crossSlope) * 0.25;

                cost.Volume = longitudinal + transverse;
                cost.Cost = _distance * length +
                            _inclination * length * Math.Min(relative, 1.0) * Math.Min(relative, 1.0) +
                            _earthwork * cost.Volume;
                return cost;
            }

            /// <summary>Admissible A* estimate: no route is shorter than the arc.</summary>
            public double Heuristic(double meters) => _distance * meters;
        }

        /// <summary>Measures arbitrary straight segments by sampling the terrain.</summary>
        sealed class SegmentSampler
        {
            readonly double _radius;
            readonly Func<Vector3D, double> _heightAt;
            readonly RouteCostModel _model;
            readonly double _step;
            readonly double _edgeCos;
            readonly double _edgeSin;

            public SegmentSampler(double radius, Func<Vector3D, double> heightAt,
                RouteCostModel model, double step)
            {
                _radius = radius;
                _heightAt = heightAt;
                _model = model;
                _step = step;
                double edgeAngle = Math.Min(Math.PI * 0.5, model.Width * 0.5 / radius);
                _edgeCos = Math.Cos(edgeAngle);
                _edgeSin = Math.Sin(edgeAngle);
            }

            public RouteCost Measure(Vector3D from, Vector3D to)
            {
                double angle = Math.Acos(MathHelper.Clamp(Vector3D.Dot(from, to), -1.0, 1.0));
                double meters = angle * _radius;
                var total = new RouteCost();
                if (meters <= 0.0) return total;
                Vector3D side = Vector3D.Cross(from, to);
                if (side.Normalize() < 1e-12) return total;
                int steps = Math.Max(1, (int)Math.Ceiling(meters / _step));
                double sinAngle = Math.Sin(angle);
                double piece = meters / steps;
                double previousHeight = _heightAt(from);
                double previousCross = CrossSlope(from, side);
                for (int i = 1; i <= steps; i++)
                {
                    Vector3D current = i == steps ? to : Slerp(from, to, angle, sinAngle, i / (double)steps);
                    double height = _heightAt(current);
                    double cross = CrossSlope(current, side);
                    RouteCost cost = _model.Evaluate(piece, height - previousHeight,
                        (cross + previousCross) * 0.5);
                    total.Cost += cost.Cost;
                    total.Length += cost.Length;
                    total.Climb += cost.Climb;
                    total.Volume += cost.Volume;
                    previousHeight = height;
                    previousCross = cross;
                }
                return total;
            }

            double CrossSlope(Vector3D direction, Vector3D side)
            {
                side -= direction * Vector3D.Dot(side, direction);
                if (side.Normalize() < 1e-12) return 0.0;
                double left = _heightAt(direction * _edgeCos + side * _edgeSin);
                double right = _heightAt(direction * _edgeCos - side * _edgeSin);
                return Math.Abs(left - right) / _model.Width;
            }

            static Vector3D Slerp(Vector3D from, Vector3D to, double angle, double sinAngle, double t)
            {
                if (sinAngle < 1e-12) return from;
                return from * (Math.Sin((1.0 - t) * angle) / sinAngle) +
                       to * (Math.Sin(t * angle) / sinAngle);
            }
        }

        /// <summary>
        /// Lazily sampled grid in a frame whose x axis follows the A-B great
        /// circle (along, s) and whose y axis is perpendicular to it (across, t).
        /// </summary>
        sealed class RouteGrid
        {
            readonly double _radius;
            readonly Func<Vector3D, double> _heightAt;
            readonly Vector3D _e1;
            readonly Vector3D _e2;
            readonly Vector3D _e3;
            readonly int _width;
            readonly int _height;
            readonly int _start;
            readonly int _goal;
            readonly double[] _cosAlong;
            readonly double[] _sinAlong;
            readonly double[] _cosAcross;
            readonly double[] _sinAcross;
            readonly double[] _terrain;
            readonly double[] _gradientS;
            readonly double[] _gradientT;
            readonly double _heuristicWeight;

            public double CellSize { get; private set; }
            public int Expanded { get; private set; }

            public RouteGrid(double radius, Func<Vector3D, double> heightAt, Vector3D a,
                Vector3D b, double distance, RoadRouteWeights weights)
            {
                _radius = radius;
                _heightAt = heightAt;
                _heuristicWeight = Math.Max(1.0, weights.HeuristicWeight);
                _e1 = a;
                _e2 = b - a * Vector3D.Dot(a, b);
                _e2.Normalize();
                _e3 = Vector3D.Cross(_e1, _e2);

                double corridor = Math.Max(0.05, weights.CorridorWidthRatio) * distance;
                double cell = weights.CellSizeMeters > 0.0
                    ? weights.CellSizeMeters
                    : Math.Max(Math.Max(weights.RoadWidthMeters * 0.5, weights.SegmentDistanceMeters * 0.25),
                        distance / 160.0);
                // Along: the A-B span plus a margin behind each end. Across: the corridor.
                double cells = (distance + corridor) / cell * (2.0 * corridor / cell);
                if (cells > Math.Max(1000, weights.MaxCells))
                    cell *= Math.Sqrt(cells / Math.Max(1000, weights.MaxCells));
                int segments = Math.Max(2, (int)Math.Ceiling(distance / cell));
                cell = distance / segments;
                CellSize = cell;
                int marginAlong = Math.Max(2, (int)Math.Ceiling(corridor * 0.5 / cell));
                int halfAcross = Math.Max(2, (int)Math.Ceiling(corridor / cell));
                _width = segments + 1 + 2 * marginAlong;
                _height = 2 * halfAcross + 1;

                _cosAlong = new double[_width];
                _sinAlong = new double[_width];
                for (int i = 0; i < _width; i++)
                {
                    double phi = (i - marginAlong) * cell / radius;
                    _cosAlong[i] = Math.Cos(phi);
                    _sinAlong[i] = Math.Sin(phi);
                }
                _cosAcross = new double[_height];
                _sinAcross = new double[_height];
                for (int j = 0; j < _height; j++)
                {
                    double psi = Math.Max(-Math.PI * 0.45,
                        Math.Min(Math.PI * 0.45, (j - halfAcross) * cell / radius));
                    _cosAcross[j] = Math.Cos(psi);
                    _sinAcross[j] = Math.Sin(psi);
                }

                int count = _width * _height;
                _terrain = new double[count];
                _gradientS = new double[count];
                _gradientT = new double[count];
                for (int i = 0; i < count; i++)
                {
                    _terrain[i] = double.NaN;
                    _gradientS[i] = double.NaN;
                }
                _start = Index(marginAlong, halfAcross);
                _goal = Index(marginAlong + segments, halfAcross);
            }

            int Index(int i, int j) => j * _width + i;

            public double Terrain(int cell) => Terrain(cell % _width, cell / _width);

            public Vector3D Direction(int cell)
            {
                int i = cell % _width;
                int j = cell / _width;
                return (_e1 * _cosAlong[i] + _e2 * _sinAlong[i]) * _cosAcross[j] + _e3 * _sinAcross[j];
            }

            double Terrain(int i, int j)
            {
                int cell = Index(i, j);
                double value = _terrain[cell];
                if (double.IsNaN(value))
                {
                    value = _heightAt(Direction(cell));
                    if (double.IsNaN(value) || double.IsInfinity(value)) value = 0.0;
                    _terrain[cell] = value;
                }
                return value;
            }

            void Gradient(int cell, out double alongSlope, out double acrossSlope)
            {
                if (double.IsNaN(_gradientS[cell]))
                {
                    int i = cell % _width;
                    int j = cell / _width;
                    int i0 = Math.Max(0, i - 1), i1 = Math.Min(_width - 1, i + 1);
                    int j0 = Math.Max(0, j - 1), j1 = Math.Min(_height - 1, j + 1);
                    double along = (i1 - i0) * CellSize * _cosAcross[j];
                    double across = (j1 - j0) * CellSize;
                    _gradientS[cell] = (Terrain(i1, j) - Terrain(i0, j)) / along;
                    _gradientT[cell] = (Terrain(i, j1) - Terrain(i, j0)) / across;
                }
                alongSlope = _gradientS[cell];
                acrossSlope = _gradientT[cell];
            }

            double ArcMeters(int from, int to)
            {
                return _radius * Math.Acos(MathHelper.Clamp(
                    Vector3D.Dot(Direction(from), Direction(to)), -1.0, 1.0));
            }

            public List<int> Search(RouteCostModel model)
            {
                int count = _width * _height;
                var cost = new double[count];
                var parent = new int[count];
                var closed = new bool[count];
                for (int i = 0; i < count; i++)
                {
                    cost[i] = double.PositiveInfinity;
                    parent[i] = -1;
                }
                var open = new MinHeap(1024);
                cost[_start] = 0.0;
                open.Push(_start, _heuristicWeight * model.Heuristic(ArcMeters(_start, _goal)));
                Expanded = 0;
                while (open.Count > 0)
                {
                    int current = open.Pop();
                    if (closed[current]) continue;
                    if (current == _goal) return Reconstruct(parent, current);
                    closed[current] = true;
                    Expanded++;

                    int ci = current % _width;
                    int cj = current / _width;
                    for (int move = 0; move < MoveI.Length; move++)
                    {
                        int ni = ci + MoveI[move];
                        int nj = cj + MoveJ[move];
                        if (ni < 0 || nj < 0 || ni >= _width || nj >= _height) continue;
                        int next = Index(ni, nj);
                        if (closed[next]) continue;

                        double step = StepCost(model, ci, cj, ni, nj, MoveI[move], MoveJ[move]);

                        double candidate = cost[current] + step;
                        if (candidate >= cost[next]) continue;
                        cost[next] = candidate;
                        parent[next] = current;
                        open.Push(next, candidate +
                            _heuristicWeight * model.Heuristic(ArcMeters(next, _goal)));
                    }
                }
                return null;
            }

            /// <summary>Cost of moving between two cells (di, dj) apart, in fractional cells.</summary>
            double StepCost(RouteCostModel model, int fromI, int fromJ, int toI, int toJ,
                double di, double dj)
            {
                // Local metric of the rotated frame: along-steps shrink by cos(psi).
                double ds = di * CellSize * (_cosAcross[fromJ] + _cosAcross[toJ]) * 0.5;
                double dt = dj * CellSize;
                double length = Math.Sqrt(ds * ds + dt * dt);
                if (length <= 0.0) return 0.0;
                double fromS, fromT, toS, toT;
                Gradient(Index(fromI, fromJ), out fromS, out fromT);
                Gradient(Index(toI, toJ), out toS, out toT);
                // Terrain slope perpendicular to the step, from the averaged gradient.
                double cross = ((fromT + toT) * ds - (fromS + toS) * dt) * 0.5 / length;
                return model.Evaluate(length, Terrain(toI, toJ) - Terrain(fromI, fromJ), cross).Cost;
            }

            /// <summary>
            /// Cost of a straight line between two cells, marched one cell at a time over
            /// the cached terrain, so comparing many candidate segments stays cheap.
            /// </summary>
            public double LineCost(int from, int to, RouteCostModel model)
            {
                int fi = from % _width, fj = from / _width;
                int ti = to % _width, tj = to / _width;
                int steps = Math.Max(1, Math.Max(Math.Abs(ti - fi), Math.Abs(tj - fj)));
                double di = (ti - fi) / (double)steps;
                double dj = (tj - fj) / (double)steps;
                double total = 0.0;
                int pi = fi, pj = fj;
                for (int k = 1; k <= steps; k++)
                {
                    int ni = k == steps ? ti : (int)Math.Round(fi + di * k);
                    int nj = k == steps ? tj : (int)Math.Round(fj + dj * k);
                    total += StepCost(model, pi, pj, ni, nj, di, dj);
                    pi = ni;
                    pj = nj;
                }
                return total;
            }

            static List<int> Reconstruct(int[] parent, int goal)
            {
                var path = new List<int>();
                for (int cell = goal; cell >= 0; cell = parent[cell])
                    path.Add(cell);
                path.Reverse();
                return path;
            }
        }

        /// <summary>Binary min-heap of cell indices; stale entries are skipped on pop.</summary>
        sealed class MinHeap
        {
            int[] _items;
            double[] _keys;

            public int Count { get; private set; }

            public MinHeap(int capacity)
            {
                _items = new int[capacity];
                _keys = new double[capacity];
            }

            public void Push(int item, double key)
            {
                if (Count == _items.Length)
                {
                    Array.Resize(ref _items, Count * 2);
                    Array.Resize(ref _keys, Count * 2);
                }
                int index = Count++;
                while (index > 0)
                {
                    int up = (index - 1) / 2;
                    if (_keys[up] <= key) break;
                    _items[index] = _items[up];
                    _keys[index] = _keys[up];
                    index = up;
                }
                _items[index] = item;
                _keys[index] = key;
            }

            public int Pop()
            {
                int top = _items[0];
                int lastItem = _items[--Count];
                double lastKey = _keys[Count];
                int index = 0;
                while (true)
                {
                    int child = index * 2 + 1;
                    if (child >= Count) break;
                    if (child + 1 < Count && _keys[child + 1] < _keys[child]) child++;
                    if (_keys[child] >= lastKey) break;
                    _items[index] = _items[child];
                    _keys[index] = _keys[child];
                    index = child;
                }
                _items[index] = lastItem;
                _keys[index] = lastKey;
                return top;
            }
        }
    }
}
