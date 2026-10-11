using System;
using System.Collections.Generic;
using HighwayEngineer.Common.Helpers;
using HighwayEngineer.Common.Roads;
using VRageMath;

namespace HighwayEngineer.Server.Contracts
{
    /// <summary>
    /// The roads built (or being built) on one planet, as a graph for distances along the road
    /// network. Graph nodes are road nodes; a node that lies on another road's segment (a road
    /// snapped onto the middle of another, or an intersection node) splits that segment with a
    /// junction, so every road meeting there is connected. Positions are unit directions from
    /// the planet centre; distances are arcs at the planet's average radius.
    /// </summary>
    sealed class RoadNetwork
    {
        /// <summary>Road nodes closer than this to a node or segment of another road join it.</summary>
        public const double JoinMeters = 50.0;
        // Coarse sampling when finding the point of a segment closest to a node.
        const double SearchStepMeters = 10.0;
        const int RefineIterations = 24;
        // Sampling when measuring a segment's length between two junctions.
        const double LengthStepMeters = 20.0;

        /// <summary>One road segment and the graph nodes on it, by curve parameter.</summary>
        sealed class Segment
        {
            public RoadCurve Curve;
            public double Meters;
            public Vector3D Center;
            public double Angle;
            public readonly List<KeyValuePair<double, int>> Stops = new List<KeyValuePair<double, int>>();
        }

        readonly double _radius;
        readonly double _joinAngle;
        readonly double _joinDot;
        readonly List<Vector3D> _nodes = new List<Vector3D>();
        readonly List<Segment> _segments = new List<Segment>();
        List<List<KeyValuePair<int, double>>> _edges;

        public RoadNetwork(double radius)
        {
            _radius = radius;
            _joinAngle = JoinMeters / radius;
            _joinDot = Math.Cos(_joinAngle);
        }

        /// <summary>
        /// Adds a road from its 12-value-per-segment planet-local geometry and returns its graph
        /// nodes. Each road node joins an existing node within <see cref="JoinMeters"/> first,
        /// else the closest existing segment (splitting it with a junction), else it is new.
        /// Existing nodes lying on the new road's segments split them the same way.
        /// </summary>
        public List<int> AddRoad(MatrixD planetWorld, double[] geometry)
        {
            _edges = null;
            var roadNodes = new List<int>();
            if (geometry == null) return roadNodes;
            int existingNodes = _nodes.Count;
            int existingSegments = _segments.Count;
            int per = Constants.GEOMETRY_VALUES_PER_SEGMENT;
            var values = new double[per];
            int previous = -1;
            for (int i = 0; i + per <= geometry.Length; i += per)
            {
                Array.Copy(geometry, i, values, 0, per);
                RoadCurve curve;
                if (!RoadCurve.TryFromLocalGeometry(planetWorld, values, out curve)) continue;
                if (previous < 0)
                {
                    previous = Join(curve.Start, existingSegments);
                    roadNodes.Add(previous);
                }
                int next = Join(curve.End, existingSegments);
                roadNodes.Add(next);
                if (next == previous) continue;
                var segment = new Segment { Curve = curve, Meters = Length(curve, 0.0, 1.0) };
                Bound(segment);
                segment.Stops.Add(new KeyValuePair<double, int>(0.0, previous));
                segment.Stops.Add(new KeyValuePair<double, int>(1.0, next));
                _segments.Add(segment);
                previous = next;
            }
            // Older roads ending on this one's segments.
            for (int node = 0; node < existingNodes; node++)
            {
                if (roadNodes.Contains(node)) continue;
                for (int s = existingSegments; s < _segments.Count; s++)
                {
                    double t;
                    if (TryNearestOnSegment(_segments[s], _nodes[node], out t))
                    {
                        _segments[s].Stops.Add(new KeyValuePair<double, int>(t, node));
                        break;
                    }
                }
            }
            return roadNodes;
        }

        /// <summary>
        /// Shortest distance along the roads from <paramref name="from"/> (joining the network
        /// at the closest node within <see cref="JoinMeters"/>) to any of the graph nodes
        /// <paramref name="goals"/>; infinity when none is reachable within
        /// <paramref name="maximumMeters"/>.
        /// </summary>
        public double DistanceAlongRoads(Vector3D from, ICollection<int> goals, double maximumMeters)
        {
            int start = FindNode(from);
            if (start < 0 || goals.Count == 0) return double.PositiveInfinity;
            List<List<KeyValuePair<int, double>>> edges = Edges();

            // Dijkstra over a binary heap; stale heap entries are skipped.
            var distance = new double[_nodes.Count];
            for (int i = 0; i < distance.Length; i++) distance[i] = double.PositiveInfinity;
            distance[start] = 0.0;
            var heap = new MinHeap();
            heap.Push(0.0, start);
            double meters;
            int current;
            while (heap.TryPop(out meters, out current))
            {
                if (meters > distance[current]) continue;
                if (meters > maximumMeters) break;
                if (goals.Contains(current)) return meters;
                foreach (KeyValuePair<int, double> edge in edges[current])
                {
                    double next = meters + edge.Value;
                    if (next < distance[edge.Key])
                    {
                        distance[edge.Key] = next;
                        heap.Push(next, edge.Key);
                    }
                }
            }
            return double.PositiveInfinity;
        }

        public double ArcMeters(Vector3D a, Vector3D b)
        {
            return Angle(a, b) * _radius;
        }

        /// <summary>The graph node for a road node: an existing node, a junction on an existing segment, or new.</summary>
        int Join(Vector3D direction, int segmentCount)
        {
            int node = FindNode(direction);
            if (node >= 0) return node;
            Segment best = null;
            double bestT = 0.0, bestDot = _joinDot;
            for (int s = 0; s < segmentCount; s++)
            {
                double t;
                if (!TryNearestOnSegment(_segments[s], direction, out t)) continue;
                double dot = Vector3D.Dot(_segments[s].Curve.Evaluate(t), direction);
                if (dot >= bestDot)
                {
                    best = _segments[s];
                    bestT = t;
                    bestDot = dot;
                }
            }
            node = _nodes.Count;
            if (best == null)
            {
                _nodes.Add(direction);
                return node;
            }
            // A junction on the existing segment, where the new road meets it.
            _nodes.Add(best.Curve.Evaluate(bestT));
            best.Stops.Add(new KeyValuePair<double, int>(bestT, node));
            return node;
        }

        /// <summary>The closest graph node within <see cref="JoinMeters"/>; -1 when none.</summary>
        int FindNode(Vector3D direction)
        {
            int best = -1;
            double bestDot = _joinDot;
            for (int i = 0; i < _nodes.Count; i++)
            {
                double dot = Vector3D.Dot(_nodes[i], direction);
                if (dot >= bestDot)
                {
                    best = i;
                    bestDot = dot;
                }
            }
            return best;
        }

        /// <summary>
        /// The parameter of the segment point closest to <paramref name="direction"/>, when it is
        /// within <see cref="JoinMeters"/> and away from the segment's ends (whose own nodes
        /// serve there).
        /// </summary>
        bool TryNearestOnSegment(Segment segment, Vector3D direction, out double t)
        {
            t = 0.0;
            if (Angle(segment.Center, direction) > segment.Angle + _joinAngle) return false;
            int steps = Math.Max(2, (int)Math.Ceiling(segment.Meters / SearchStepMeters));
            int bestStep = 0;
            double bestDot = double.MinValue;
            for (int i = 0; i <= steps; i++)
            {
                double dot = Vector3D.Dot(segment.Curve.Evaluate(i / (double)steps), direction);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    bestStep = i;
                }
            }
            // Golden-section refinement around the best sample.
            double low = Math.Max(0.0, (bestStep - 1) / (double)steps);
            double high = Math.Min(1.0, (bestStep + 1) / (double)steps);
            const double ratio = 0.6180339887498949;
            for (int i = 0; i < RefineIterations; i++)
            {
                double a = high - (high - low) * ratio, b = low + (high - low) * ratio;
                if (Vector3D.Dot(segment.Curve.Evaluate(a), direction) >
                    Vector3D.Dot(segment.Curve.Evaluate(b), direction))
                    high = b;
                else
                    low = a;
            }
            t = (low + high) * 0.5;
            if (Vector3D.Dot(segment.Curve.Evaluate(t), direction) < _joinDot) return false;
            return Math.Min(t, 1.0 - t) * segment.Meters > 1.0;
        }

        /// <summary>The graph edges: each segment's stops in order, joined by the arc between them.</summary>
        List<List<KeyValuePair<int, double>>> Edges()
        {
            if (_edges != null) return _edges;
            _edges = new List<List<KeyValuePair<int, double>>>(_nodes.Count);
            for (int i = 0; i < _nodes.Count; i++)
                _edges.Add(new List<KeyValuePair<int, double>>());
            foreach (Segment segment in _segments)
            {
                segment.Stops.Sort((a, b) => a.Key.CompareTo(b.Key));
                for (int i = 1; i < segment.Stops.Count; i++)
                {
                    int a = segment.Stops[i - 1].Value, b = segment.Stops[i].Value;
                    if (a == b) continue;
                    double meters = Length(segment.Curve, segment.Stops[i - 1].Key, segment.Stops[i].Key);
                    _edges[a].Add(new KeyValuePair<int, double>(b, meters));
                    _edges[b].Add(new KeyValuePair<int, double>(a, meters));
                }
            }
            return _edges;
        }

        /// <summary>The arc length of a curve between two parameters.</summary>
        double Length(RoadCurve curve, double from, double to)
        {
            int steps = Math.Max(1, (int)Math.Ceiling(curve.Arc * _radius * (to - from) / LengthStepMeters));
            double meters = 0.0;
            Vector3D last = curve.Evaluate(from);
            for (int i = 1; i <= steps; i++)
            {
                Vector3D next = curve.Evaluate(from + (to - from) * i / steps);
                meters += ArcMeters(last, next);
                last = next;
            }
            return meters;
        }

        /// <summary>The cap around a segment, to skip far ones cheaply.</summary>
        static void Bound(Segment segment)
        {
            Vector3D center = segment.Curve.Evaluate(0.5);
            double angle = 0.0;
            const int steps = 8;
            for (int i = 0; i <= steps; i++)
                angle = Math.Max(angle, Angle(center, segment.Curve.Evaluate(i / (double)steps)));
            segment.Center = center;
            // The samples may miss the farthest point slightly; pad by a sample spacing.
            segment.Angle = angle + segment.Curve.Arc / steps;
        }

        static double Angle(Vector3D a, Vector3D b)
        {
            return Math.Acos(MathHelper.Clamp(Vector3D.Dot(a, b), -1.0, 1.0));
        }

        /// <summary>A binary min-heap of (distance, node) pairs.</summary>
        sealed class MinHeap
        {
            readonly List<KeyValuePair<double, int>> _items = new List<KeyValuePair<double, int>>();

            public void Push(double key, int value)
            {
                _items.Add(new KeyValuePair<double, int>(key, value));
                int i = _items.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (_items[parent].Key <= _items[i].Key) break;
                    Swap(i, parent);
                    i = parent;
                }
            }

            public bool TryPop(out double key, out int value)
            {
                if (_items.Count == 0)
                {
                    key = 0.0;
                    value = -1;
                    return false;
                }
                key = _items[0].Key;
                value = _items[0].Value;
                int last = _items.Count - 1;
                _items[0] = _items[last];
                _items.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int smallest = i, left = 2 * i + 1, right = left + 1;
                    if (left < _items.Count && _items[left].Key < _items[smallest].Key) smallest = left;
                    if (right < _items.Count && _items[right].Key < _items[smallest].Key) smallest = right;
                    if (smallest == i) break;
                    Swap(i, smallest);
                    i = smallest;
                }
                return true;
            }

            void Swap(int a, int b)
            {
                KeyValuePair<double, int> item = _items[a];
                _items[a] = _items[b];
                _items[b] = item;
            }
        }

        /// <summary>The node directions of a road, from its planet-local geometry.</summary>
        public static List<Vector3D> Nodes(MatrixD planetWorld, double[] geometry)
        {
            var nodes = new List<Vector3D>();
            if (geometry == null) return nodes;
            int per = Constants.GEOMETRY_VALUES_PER_SEGMENT;
            var values = new double[per];
            for (int i = 0; i + per <= geometry.Length; i += per)
            {
                Array.Copy(geometry, i, values, 0, per);
                RoadCurve curve;
                if (!RoadCurve.TryFromLocalGeometry(planetWorld, values, out curve)) continue;
                if (nodes.Count == 0) nodes.Add(curve.Start);
                nodes.Add(curve.End);
            }
            return nodes;
        }
    }
}
