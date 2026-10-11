using System;
using System.Collections.Generic;
using VRageMath;

namespace HighwayEngineer.Common.Structures
{
    /// <summary>
    /// How an approach ramp at one end of a bridge is shaped: road fill that carries the deck on to
    /// the road. Its top leaves the deck's end flush, at the deck's own grade, and eases over
    /// <see cref="BridgeShape.TransitionMeters"/> into the road (or terrain) past it, meeting it flush;
    /// where the terrain falls away faster than <see cref="Grade"/>, it runs on down at that grade,
    /// an embankment, until it meets it. Past the meeting point it runs on for
    /// <see cref="BridgeShape.RampTail"/>, sinking out of sight under the road. Its sides and far
    /// end fall at <see cref="Slope"/>. Under the deck's end it is cut off plumb, an abutment wall.
    /// The terrain is left as it is.
    /// </summary>
    public struct BridgeRampSpec
    {
        /// <summary>Length of the top, out from the deck's end; 0 for no ramp.</summary>
        public double Length;
        /// <summary>The deck's rise per metre out from its end, which the top leaves at.</summary>
        public double StartGrade;
        /// <summary>The top's height at the end of the transition, from the deck top at its end.</summary>
        public double EndHeight;
        /// <summary>The top's rise per metre from the end of the transition on.</summary>
        public double EndGrade;
        /// <summary>How far below the deck top the ramp reaches (into the terrain).</summary>
        public double Depth;
        /// <summary>Drop of the top per metre out from the deck.</summary>
        public double Grade;
        /// <summary>Drop of the sides and far end per metre out from the top (tangent of the side angle).</summary>
        public double Slope;
    }

    /// <summary>
    /// A bridge as a signed distance field (metres, negative inside): a deck of road fill on a
    /// concrete slab between parapets, carried by an <see cref="IBridgeStructure"/> (beams or an
    /// arch), with an approach ramp at each end. The deck is a chain of straight segments along
    /// a <see cref="BridgeAxis"/>, which follows the road's curves and the planet's, each segment
    /// level to its own gravity; the axis miters the joints. Each segment gets its own voxel map
    /// rotated to it (<see cref="GetSegment"/>), so its deck lines up with the voxel grid. All maps
    /// sample this one field, so they agree where they overlap. Immutable once built, so background
    /// threads can sample it freely.
    /// </summary>
    public sealed class BridgeShape
    {
        // Concrete under the road fill, at least.
        const double MinSlabThickness = 2.5;
        const double DeckOverhang = 4.0;
        // Past each end the deck continues this far below its top, buried, so it does not
        // draw the same surface as the terrain it runs into.
        const double SillDrop = 0.75;
        /// <summary>Length of a ramp's vertical curve from the deck's grade into the road's.</summary>
        public const double TransitionMeters = 15.0;
        /// <summary>How far a ramp runs on past the point where it meets the road flush, sinking out of sight under it.</summary>
        public const double RampTail = 3.0;
        // Past the meeting point the ramp stays at the road's height this far, then sinks this deep.
        const double RampSinkStart = 1.0;
        const double RampSink = 0.75;
        // Ramp voxels this close to its abutment wall are concrete; the rest is road fill.
        const double AbutmentThickness = 2.0;
        // Parapets are 2 m wide and tall, whole metres on the deck's grid: their faces fall between
        // samples, and they always hold a sample of every second voxel (the first coarse LOD's), so
        // they stay up past arm's length. One metre held none, so they vanished at the first LOD.
        public const double ParapetHeight = 2.0;
        public const double ParapetWidth = 2.0;
        // Past each end the parapets run on along the ramp, sloping down into it over this far, as
        // real bridges' barriers end, instead of stopping as a blunt wall at the deck's end.
        const double ParapetTaperMeters = 10.0;
        // The structure is this much narrower than the deck on each side; whole metres keep its
        // faces between samples, like the deck's.
        const double StructureInset = 1.0;
        // Smooth-min radius: rounds the joint between the deck and the structure.
        const double Blend = 1.5;
        // Inner joints are cut by the axis's mitering planes alone; the slab runs on this far.
        const double Unbounded = 1e5;

        /// <summary>One straight piece of the deck, in its own frame.</summary>
        public struct Segment
        {
            public Vector3D Start, Forward, Up, Side;
            public double Length;
        }

        /// <summary>A ramp in its own frame: Out along the bridge away from the deck, Up along gravity.</summary>
        struct Ramp
        {
            public Vector3D Origin, Out, Up, Side;
            public BridgeRampSpec Spec;
            public double Sin, Cos;
            /// <summary>Highest point of its top, from the deck top (0 when it only falls).</summary>
            public double Highest;
        }


        readonly BridgeAxis _axis;
        readonly IBridgeStructure _structure;
        readonly double _halfWidth;
        readonly double _deckThickness;
        readonly double _fillDepth;
        readonly double _length;
        readonly double _deepestFoot;
        readonly Segment[] _segments;
        readonly Ramp[] _ramps;
        readonly Vector3D _planetCenter;
        readonly double _startRampReach;
        readonly double _endRampReach;

        /// <param name="axis">The deck's centreline (deck top centre at each joint).</param>
        /// <param name="planetCenter">Gravity points away from here.</param>
        /// <param name="width">Road width; the deck adds the parapets (see <see cref="HalfWidthFor"/>).</param>
        /// <param name="fillDepth">Depth of the road fill on the deck.</param>
        /// <param name="structure">What carries the deck, built for this deck's width and thickness.</param>
        /// <param name="startRamp">The ramp before the start (Length 0 for none).</param>
        /// <param name="endRamp">The ramp past the end.</param>
        public BridgeShape(BridgeAxis axis, Vector3D planetCenter, double width, double fillDepth,
            IBridgeStructure structure, BridgeRampSpec startRamp, BridgeRampSpec endRamp)
        {
            _axis = axis;
            _planetCenter = planetCenter;
            _structure = structure;
            _halfWidth = HalfWidthFor(width);
            _fillDepth = fillDepth;
            _deckThickness = SlabFor(fillDepth);
            _segments = new Segment[axis.PieceCount];
            for (int i = 0; i < _segments.Length; i++)
            {
                Vector3D from = axis.Point(i), to = axis.Point(i + 1);
                Vector3D forward = Vector3D.Normalize(to - from);
                Vector3D side = axis.Side(i);
                _segments[i] = new Segment
                {
                    Start = from,
                    Forward = forward,
                    Side = side,
                    Up = Vector3D.Normalize(Vector3D.Cross(side, forward)),
                    Length = Vector3D.Distance(from, to)
                };
                _length += _segments[i].Length;
            }
            foreach (BridgeFoot foot in structure.Feet)
            {
                Segment segment = _segments[axis.PieceAt(foot.Point)];
                _deepestFoot = Math.Max(_deepestFoot, -Vector3D.Dot(foot.Point - segment.Start, segment.Up));
            }
            var ramps = new List<Ramp>();
            if (startRamp.Length > 0.0)
            {
                ramps.Add(MakeRamp(axis.Start, -axis.Level(0), planetCenter, startRamp));
                _startRampReach = Reach(ramps[ramps.Count - 1]);
            }
            if (endRamp.Length > 0.0)
            {
                ramps.Add(MakeRamp(axis.End, axis.Level(axis.PieceCount - 1), planetCenter, endRamp));
                _endRampReach = Reach(ramps[ramps.Count - 1]);
            }
            _ramps = ramps.ToArray();
        }

        // Its top and the foot of its sloped far end.
        static double Reach(Ramp ramp)
        {
            return ramp.Spec.Length + ramp.Spec.Depth * ramp.Cos / ramp.Sin;
        }

        /// <summary>
        /// How far out from the deck's start (or its end) the approach ramp there reaches along the ground, its
        /// sloped far end included; 0 without a ramp.
        /// </summary>
        public double RampReach(bool atEnd)
        {
            return atEnd ? _endRampReach : _startRampReach;
        }

        static Ramp MakeRamp(Vector3D origin, Vector3D outward, Vector3D planetCenter, BridgeRampSpec spec)
        {
            Vector3D up = Vector3D.Normalize(origin - planetCenter);
            outward = Vector3D.Normalize(outward - up * Vector3D.Dot(outward, up));
            double angle = Math.Atan(Math.Max(0.05, spec.Slope));
            double highest = 0.0;
            for (int i = 0; i <= 32; i++)
                highest = Math.Max(highest, RampTop(spec, spec.Length * i / 32));
            return new Ramp
            {
                Highest = highest,
                Origin = origin,
                Out = outward,
                Up = up,
                Side = Vector3D.Normalize(Vector3D.Cross(outward, up)),
                Spec = spec,
                Sin = Math.Sin(angle),
                Cos = Math.Cos(angle)
            };
        }

        /// <summary>
        /// Height of a ramp's top <paramref name="a"/> metres out from the deck's end, from the deck
        /// top there: a cubic from the deck's grade to <see cref="BridgeRampSpec.EndHeight"/> and
        /// <see cref="BridgeRampSpec.EndGrade"/> over <see cref="TransitionMeters"/>, then straight on,
        /// so it is flush with the deck and with the road it meets; over its last
        /// <see cref="RampTail"/> it sinks <see cref="RampSink"/> under the road.
        /// </summary>
        public static double RampTop(BridgeRampSpec spec, double a)
        {
            double slope;
            return RampTop(spec, a, out slope);
        }

        static double RampTop(BridgeRampSpec spec, double a, out double slope)
        {
            const double l = TransitionMeters;
            a = MathHelper.Clamp(a, 0.0, Math.Max(spec.Length, 0.0));
            double height;
            if (a >= l)
            {
                slope = spec.EndGrade;
                height = spec.EndHeight + spec.EndGrade * (a - l);
            }
            else
            {
                double t = a / l, t2 = t * t, t3 = t2 * t;
                double m0 = spec.StartGrade * l, m1 = spec.EndGrade * l;
                height = (t3 - 2.0 * t2 + t) * m0 + (-2.0 * t3 + 3.0 * t2) * spec.EndHeight + (t3 - t2) * m1;
                slope = ((3.0 * t2 - 4.0 * t + 1.0) * m0 + (-6.0 * t2 + 6.0 * t) * spec.EndHeight +
                         (3.0 * t2 - 2.0 * t) * m1) / l;
            }
            // The buried tail: level with the road just past the meeting point, then easing down under it.
            double sinkFrom = spec.Length - RampTail + RampSinkStart;
            double sinkRun = spec.Length - sinkFrom;
            if (sinkRun > 0.0 && a > sinkFrom)
            {
                double s = (a - sinkFrom) / sinkRun;
                height -= RampSink * s * s * (3.0 - 2.0 * s);
                slope -= RampSink * 6.0 * s * (1.0 - s) / sinkRun;
            }
            return height;
        }


        /// <summary>Half the width of the deck, parapets included.</summary>
        public double HalfWidth => _halfWidth;

        /// <summary>
        /// Total deck thickness (fill and concrete) for a fill depth: an odd number of metres, so with
        /// the mid-plane on a sample plane the top and bottom faces fall halfway between samples (a
        /// sample exactly on a face meshes into slivers), and the mid-plane samples sit more than a
        /// voxel inside, saturated, so coarse LODs keep them.
        /// </summary>
        public static double SlabFor(double fillDepth)
        {
            double odd = Math.Ceiling((fillDepth + MinSlabThickness - 1.0) * 0.5) * 2.0 + 1.0;
            return Math.Max(3.0, odd);
        }

        /// <summary>
        /// Half the deck's width for a road width: the road between the parapets, and the parapets.
        /// A whole number of metres, so its sides fall between samples.
        /// </summary>
        public static double HalfWidthFor(double width)
        {
            return Math.Max(1.0, Math.Round(width * 0.5)) + ParapetWidth;
        }

        /// <summary>Half the width of the structure under a deck of a width.</summary>
        public static double StructureHalfWidthFor(double width)
        {
            return HalfWidthFor(width) - StructureInset;
        }

        /// <summary>Deck length along its segments.</summary>
        public double Length => _length;
        public double Width => _halfWidth * 2.0;
        public double DeckThickness => _deckThickness;
        /// <summary>How far below the deck top the structure's lowest point reaches.</summary>
        public double DeepestFoot => _deepestFoot;
        public IBridgeStructure Structure => _structure;
        public BridgeAxis Axis => _axis;
        public int SegmentCount => _segments.Length;
        public Segment GetSegment(int index) => _segments[index];

        /// <summary>
        /// Bounds of the solid parts between <paramref name="from"/> and <paramref name="to"/> along a
        /// segment, in its frame (X along, Y up, Z side, from its start). The ends overhang at the
        /// bridge's ends are included when the range reaches them, and so is any ramp in the range.
        /// </summary>
        public BoundingBoxD SegmentBounds(int index, double from, double to)
        {
            Segment segment = _segments[index];
            if (index == 0) from = Math.Min(from, -DeckOverhang);
            if (index == _segments.Length - 1) to = Math.Max(to, segment.Length + DeckOverhang);
            double bottom = -(_deckThickness + SillDrop);
            foreach (BridgeFoot foot in _structure.Feet)
            {
                Vector3D rel = foot.Point - segment.Start;
                double a = Vector3D.Dot(rel, segment.Forward);
                if (a > from - foot.Reach && a < to + foot.Reach)
                    bottom = Math.Min(bottom, Vector3D.Dot(rel, segment.Up));
            }
            var bounds = new BoundingBoxD(new Vector3D(from, bottom, -_halfWidth),
                new Vector3D(to, ParapetHeight, _halfWidth));
            foreach (Ramp ramp in _ramps)
            {
                BoundingBoxD box = RampBounds(ramp, segment);
                double min = Math.Max(box.Min.X, from), max = Math.Min(box.Max.X, to);
                if (min >= max) continue;
                bounds.Include(new Vector3D(min, box.Min.Y, box.Min.Z));
                bounds.Include(new Vector3D(max, box.Max.Y, box.Max.Z));
            }
            return bounds;
        }

        /// <summary>A ramp's bounds in a segment's frame, from the corners of the box around it.</summary>
        BoundingBoxD RampBounds(Ramp ramp, Segment segment)
        {
            BridgeRampSpec spec = ramp.Spec;
            double spread = spec.Depth * ramp.Cos / ramp.Sin;
            double outward = spec.Length + spread;
            double across = _halfWidth + spread;
            BoundingBoxD box = BoundingBoxD.CreateInvalid();
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3D world = ramp.Origin +
                                 ramp.Out * ((corner & 1) == 0 ? 0.0 : outward) +
                                 ramp.Side * ((corner & 2) == 0 ? -across : across) +
                                 ramp.Up * ((corner & 4) == 0 ? -spec.Depth : ramp.Highest + ParapetHeight);
                Vector3D rel = world - segment.Start;
                box.Include(new Vector3D(Vector3D.Dot(rel, segment.Forward), Vector3D.Dot(rel, segment.Up),
                    Vector3D.Dot(rel, segment.Side)));
            }
            return box;
        }

        public BridgePoint Locate(Vector3D world) => _axis.Locate(world);

        /// <summary>
        /// Whether a voxel takes the road material: the fill layer between the parapets, and the
        /// empty space above it, so the coarse LODs' majority vote keeps it too; and the ramps,
        /// except their abutment walls.
        /// </summary>
        public bool IsPavement(BridgePoint point)
        {
            int index = point.Piece;
            Segment segment = _segments[index];
            Vector3D rel = point.World - segment.Start;
            double a = Vector3D.Dot(rel, segment.Forward);
            double v = Vector3D.Dot(rel, segment.Up);
            double before = index == 0 ? DeckOverhang : Unbounded;
            double after = index == _segments.Length - 1 ? DeckOverhang : Unbounded;
            bool alongDeck = a > -before && a < segment.Length + after;
            if (v > -_fillDepth && Math.Abs(Vector3D.Dot(rel, segment.Side)) < _halfWidth - ParapetWidth && alongDeck)
                return true;
            foreach (Ramp ramp in _ramps)
            {
                // Its parapets' ends are concrete, like the deck's.
                if (RampParapet(ramp, point.World) < 0.5)
                    return false;
                Vector3D local = point.World - ramp.Origin;
                if (Vector3D.Dot(local, ramp.Out) > AbutmentThickness && RampDistance(ramp, point.World) < 1.0)
                    return true;
            }
            return false;
        }

        public double Distance(BridgePoint point)
        {
            double distance = Math.Min(BridgeSdf.SmoothMin(Deck(point), _structure.Distance(point), Blend),
                Parapet(point));
            foreach (Ramp ramp in _ramps)
                distance = Math.Min(distance, Math.Min(RampDistance(ramp, point.World), RampParapet(ramp, point.World)));
            return distance;
        }

        /// <summary>The slab of the point's segment, with the buried sills past the bridge's ends.</summary>
        double Deck(BridgePoint point)
        {
            int index = point.Piece;
            Segment segment = _segments[index];
            Vector3D rel = point.World - segment.Start;
            double a = Vector3D.Dot(rel, segment.Forward);
            double v = Vector3D.Dot(rel, segment.Up);
            double l = Vector3D.Dot(rel, segment.Side);
            bool first = index == 0, last = index == _segments.Length - 1;
            double from = first ? 0.0 : -Unbounded;
            double to = segment.Length + (last ? 0.0 : Unbounded);
            double slab = BridgeSdf.Box(a - (from + to) * 0.5, v + _deckThickness * 0.5, l,
                (to - from) * 0.5, _deckThickness * 0.5, _halfWidth);
            double end = Math.Min(first ? a : double.MaxValue, last ? segment.Length - a : double.MaxValue);
            if (end == double.MaxValue) return slab;
            double sill = BridgeSdf.Box(end + DeckOverhang * 0.5, v + SillDrop + _deckThickness * 0.5, l,
                DeckOverhang * 0.5, _deckThickness * 0.5, _halfWidth);
            return Math.Min(slab, sill);
        }

        double Parapet(BridgePoint point)
        {
            int index = point.Piece;
            Segment segment = _segments[index];
            Vector3D rel = point.World - segment.Start;
            double a = Vector3D.Dot(rel, segment.Forward);
            double from = index == 0 ? 0.0 : -Unbounded;
            double to = segment.Length + (index == _segments.Length - 1 ? 0.0 : Unbounded);
            double side = Math.Abs(Vector3D.Dot(rel, segment.Side)) - (_halfWidth - ParapetWidth * 0.5);
            return BridgeSdf.Box(a - (from + to) * 0.5, Vector3D.Dot(rel, segment.Up) - ParapetHeight * 0.5, side,
                (to - from) * 0.5, ParapetHeight * 0.5, ParapetWidth * 0.5);
        }

        /// <summary>
        /// A parapet's sloped end on a ramp: on its edges, carrying on from the deck's parapets, its
        /// top falling from <see cref="ParapetHeight"/> over the ramp at the deck's end to the ramp's
        /// top <see cref="ParapetTaperMeters"/> out (or at the ramp's end, when that comes first).
        /// It reaches a metre into the ramp, so it stands on it.
        /// </summary>
        double RampParapet(Ramp ramp, Vector3D world)
        {
            double taper = Math.Min(ParapetTaperMeters, ramp.Spec.Length);
            if (taper <= 0.0) return double.MaxValue;
            Vector3D rel = world - ramp.Origin;
            double a = Vector3D.Dot(rel, ramp.Out);
            double v = Vector3D.Dot(rel, ramp.Up);
            double l = Math.Abs(Vector3D.Dot(rel, ramp.Side));
            double slope;
            double surface = RampTop(ramp.Spec, a, out slope);
            double fall = ParapetHeight / taper;
            double height = ParapetHeight * (1.0 - MathHelper.Clamp(a, 0.0, taper) / taper);
            double top = (v - surface - height) / Math.Sqrt(1.0 + (slope - fall) * (slope - fall));
            double bottom = surface - 1.0 - v;
            double along = Math.Max(-a, a - taper);
            double side = Math.Abs(l - (_halfWidth - ParapetWidth * 0.5)) - ParapetWidth * 0.5;
            return Math.Max(Math.Max(top, bottom), Math.Max(along, side));
        }

        /// <summary>
        /// A ramp: under its top (sloping down out from the deck), inside its sloped sides and far
        /// end, above its bottom, and out from the plumb abutment wall under the deck's end.
        /// </summary>
        double RampDistance(Ramp ramp, Vector3D world)
        {
            BridgeRampSpec spec = ramp.Spec;
            Vector3D rel = world - ramp.Origin;
            double a = Vector3D.Dot(rel, ramp.Out);
            double v = Vector3D.Dot(rel, ramp.Up);
            double l = Math.Abs(Vector3D.Dot(rel, ramp.Side));
            double slope;
            double topHeight = RampTop(spec, a, out slope);
            double top = a < spec.Length
                ? (v - topHeight) / Math.Sqrt(1.0 + slope * slope)
                : v - topHeight;
            double sides = (l - _halfWidth) * ramp.Sin + (v - topHeight) * ramp.Cos;
            double far = (a - spec.Length) * ramp.Sin + (v - topHeight) * ramp.Cos;
            double wall = -a;
            double bottom = -(v + spec.Depth);
            return Math.Max(Math.Max(Math.Max(top, sides), Math.Max(far, wall)), bottom);
        }
    }
}
