using System;
using System.Collections.Generic;
using VRageMath;

namespace HighwayEngineer.Common.Structures
{
    /// <summary>
    /// A bridge's centreline: straight pieces between its joints (deck top centre), which follow the
    /// road's curves. Points are assigned to a piece by the vertical planes bisecting each joint, so
    /// a curved deck splits into mitered pieces with no gap and no overlap. A point's position along
    /// the bridge is its level distance from the start, continuous across the joints at any offset
    /// to the side, so structures laid out along it (spans, columns, the arch) follow the curve too.
    /// Immutable, so background threads can share it.
    /// </summary>
    public sealed class BridgeAxis
    {
        readonly Vector3D[] _points;
        readonly Vector3D[] _levels;
        readonly Vector3D[] _sides;
        readonly Vector3D[] _ups;
        // Per joint: the normal of its bisecting plane.
        readonly Vector3D[] _normals;
        // Per joint: its distance from the start.
        readonly double[] _alongs;
        // Per piece: its level length, and how far its start and end planes lean along it per metre to the side.
        readonly double[] _lengths;
        readonly double[] _startShear;
        readonly double[] _endShear;

        /// <param name="points">Deck top centre at each joint, both ends included (at least two).</param>
        /// <param name="planetCenter">Gravity points away from here.</param>
        public BridgeAxis(IList<Vector3D> points, Vector3D planetCenter)
        {
            int pieces = points.Count - 1;
            _points = new Vector3D[points.Count];
            points.CopyTo(_points, 0);
            _levels = new Vector3D[pieces];
            _sides = new Vector3D[pieces];
            _ups = new Vector3D[pieces];
            _lengths = new double[pieces];
            _startShear = new double[pieces];
            _endShear = new double[pieces];
            _alongs = new double[points.Count];
            _normals = new Vector3D[points.Count];
            for (int i = 0; i < pieces; i++)
            {
                Vector3D up = Vector3D.Normalize((_points[i] + _points[i + 1]) * 0.5 - planetCenter);
                Vector3D run = _points[i + 1] - _points[i];
                Vector3D level = Vector3D.Normalize(run - up * Vector3D.Dot(run, up));
                _ups[i] = up;
                _levels[i] = level;
                _sides[i] = Vector3D.Normalize(Vector3D.Cross(level, up));
                _lengths[i] = Vector3D.Dot(run, level);
                _alongs[i + 1] = _alongs[i] + _lengths[i];
            }
            _normals[0] = _levels[0];
            _normals[pieces] = _levels[pieces - 1];
            for (int j = 1; j < pieces; j++)
                _normals[j] = Vector3D.Normalize(_levels[j - 1] + _levels[j]);
            for (int i = 0; i < pieces; i++)
            {
                _startShear[i] = Shear(i, _normals[i]);
                _endShear[i] = Shear(i, _normals[i + 1]);
            }
        }

        /// <summary>Along a piece, where a joint's plane crosses it per metre to the side.</summary>
        double Shear(int piece, Vector3D normal)
        {
            return Vector3D.Dot(_sides[piece], normal) / Math.Max(1e-6, Vector3D.Dot(_levels[piece], normal));
        }

        /// <summary>Level length from the start to the end.</summary>
        public double Length => _alongs[_alongs.Length - 1];
        public int PieceCount => _lengths.Length;
        public Vector3D Start => _points[0];
        public Vector3D End => _points[_points.Length - 1];
        /// <summary>Deck top centre at a joint.</summary>
        public Vector3D Point(int joint) => _points[joint];
        /// <summary>Distance of a joint from the start.</summary>
        public double JointAlong(int joint) => _alongs[joint];
        /// <summary>Level direction of a piece, from its start to its end.</summary>
        public Vector3D Level(int piece) => _levels[piece];
        /// <summary>Level direction across a piece (right of its direction).</summary>
        public Vector3D Side(int piece) => _sides[piece];

        /// <summary>The piece whose bisecting planes enclose a point (the first or last past the ends).</summary>
        public int PieceAt(Vector3D point)
        {
            // The planes are passed in order along the deck, so a binary search finds the last one passed.
            int low = 0, high = _lengths.Length - 1;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (Vector3D.Dot(point - _points[middle], _normals[middle]) >= 0.0)
                    low = middle;
                else
                    high = middle - 1;
            }
            return low;
        }

        /// <summary>Where a point is: its piece, distance along the bridge and level offset to the side.</summary>
        public BridgePoint Locate(Vector3D point)
        {
            int piece = PieceAt(point);
            Vector3D rel = point - _points[piece];
            double x = Vector3D.Dot(rel, _levels[piece]);
            double side = Vector3D.Dot(rel, _sides[piece]);
            // Measured between the piece's two mitered ends at this offset, so it runs on at the next piece.
            double from = -side * _startShear[piece];
            double to = _lengths[piece] - side * _endShear[piece];
            double fraction = to - from > 1e-6 ? (x - from) / (to - from) : 0.0;
            return new BridgePoint
            {
                World = point,
                Piece = piece,
                Along = _alongs[piece] + fraction * _lengths[piece],
                Lateral = side
            };
        }

        int PieceFor(double along)
        {
            int piece = 0;
            while (piece < _lengths.Length - 1 && _alongs[piece + 1] <= along)
                piece++;
            return piece;
        }

        /// <summary>The deck top centre a distance along the bridge (clamped to it).</summary>
        public Vector3D PointAt(double along)
        {
            along = MathHelper.Clamp(along, 0.0, Length);
            int piece = PieceFor(along);
            double fraction = _lengths[piece] > 1e-9 ? (along - _alongs[piece]) / _lengths[piece] : 0.0;
            return Vector3D.Lerp(_points[piece], _points[piece + 1], MathHelper.Clamp(fraction, 0.0, 1.0));
        }

        /// <summary>The level direction of the deck a distance along the bridge.</summary>
        public Vector3D LevelAt(double along) => _levels[PieceFor(along)];

        /// <summary>The level direction across the deck a distance along the bridge.</summary>
        public Vector3D SideAt(double along) => _sides[PieceFor(along)];
    }

    /// <summary>A point sampled from a bridge's shape, located on its <see cref="BridgeAxis"/> once for every part.</summary>
    public struct BridgePoint
    {
        public Vector3D World;
        public int Piece;
        public double Along;
        public double Lateral;
    }

    /// <summary>A lowest point of a structure, and how far along the axis it lies under the bridge.</summary>
    public struct BridgeFoot
    {
        public Vector3D Point;
        /// <summary>Distance along the axis, either side of the point, that its part extends over.</summary>
        public double Reach;
    }

    /// <summary>Everything under a bridge's deck, as a signed distance field (metres, negative inside).</summary>
    public interface IBridgeStructure
    {
        /// <summary>"beam" or "arch", as the player names it.</summary>
        string Name { get; }
        double Distance(BridgePoint point);
        /// <summary>Its lowest points, so each segment's voxel map reaches down far enough.</summary>
        IList<BridgeFoot> Feet { get; }
    }

    /// <summary>A support of a beam bridge: where the deck is, and how deep its pillar goes.</summary>
    public struct BridgeSupport
    {
        /// <summary>Distance along the bridge.</summary>
        public double Along;
        /// <summary>Deck top centre above the support.</summary>
        public Vector3D Deck;
        /// <summary>How far below the deck top, along gravity, the pillar reaches; below the terrain.</summary>
        public double FootDepth;
    }

    /// <summary>
    /// A beam bridge: the deck rests on a wall that stands on the terrain, pierced by an opening per
    /// span with straight sides and an arched top, leaving straight rectangular pillars between them.
    /// Each span is drawn along the bridge's curve, in the gravity at its middle, so its pillars stand
    /// plumb and the wall stays under a curved deck.
    /// </summary>
    public sealed class BeamStructure : IBridgeStructure
    {
        // Half the width of the straight pillar left between two openings. Pillars and the wall are
        // kept wide so they still hold samples at the coarsest LODs.
        public const double LegHalfWidth = 4.0;
        // Solid between the top of each opening and the deck's underside.
        const double CrownThickness = 2.0;

        struct Span
        {
            public Vector3D Center, GravityUp;
            public double CenterAlong, HalfLength;
            /// <summary>Rise of the deck per horizontal metre.</summary>
            public double Grade;
            /// <summary>Height of the lower pillar foot, along gravity, from the deck top at the centre.</summary>
            public double Spring;
            public double Along;
        }

        readonly double _halfWidth;
        readonly double _deckThickness;
        readonly Span[] _spans;
        readonly BridgeFoot[] _feet;

        /// <param name="supports">At both ends and between equal spans, in order (at least two).</param>
        /// <param name="halfWidth">Half the wall's width.</param>
        public BeamStructure(BridgeAxis axis, IList<BridgeSupport> supports, Vector3D planetCenter, double halfWidth,
            double deckThickness)
        {
            _halfWidth = halfWidth;
            _deckThickness = deckThickness;
            _spans = new Span[supports.Count - 1];
            double maxSpan = 0.0;
            for (int i = 0; i < _spans.Length; i++)
            {
                BridgeSupport left = supports[i], right = supports[i + 1];
                double centerAlong = (left.Along + right.Along) * 0.5;
                Vector3D center = axis.PointAt(centerAlong);
                Vector3D gravityUp = Vector3D.Normalize(center - planetCenter);
                double horizontal = right.Along - left.Along;
                _spans[i] = new Span
                {
                    Center = center,
                    GravityUp = gravityUp,
                    CenterAlong = centerAlong,
                    HalfLength = horizontal * 0.5,
                    Grade = Vector3D.Dot(right.Deck - left.Deck, gravityUp) / horizontal,
                    Spring = Math.Min(Foot(left, planetCenter, center, gravityUp),
                        Foot(right, planetCenter, center, gravityUp)),
                    Along = left.Along
                };
                maxSpan = Math.Max(maxSpan, horizontal);
            }
            _feet = new BridgeFoot[supports.Count];
            for (int i = 0; i < supports.Count; i++)
                _feet[i] = new BridgeFoot
                {
                    Point = supports[i].Deck - Vector3D.Normalize(supports[i].Deck - planetCenter) * supports[i].FootDepth,
                    Reach = maxSpan + LegHalfWidth
                };
        }

        public string Name => "beam";
        public IList<BridgeFoot> Feet => _feet;
        public int SpanCount => _spans.Length;

        public double Distance(BridgePoint point)
        {
            int index = 0;
            while (index < _spans.Length - 1 && _spans[index + 1].Along <= point.Along)
                index++;
            Span span = _spans[index];
            double h = point.Along - span.CenterAlong;
            double g = Vector3D.Dot(point.World - span.Center, span.GravityUp);
            double s = point.Lateral;
            // The deck's mid-plane, so the wall overlaps the deck and the blend closes the joint.
            double top = (g - (h * span.Grade - _deckThickness * 0.5)) / Math.Sqrt(1.0 + span.Grade * span.Grade);
            double bottom = span.Spring - g;
            double ends = Math.Abs(h) - (span.HalfLength + LegHalfWidth);
            double sides = Math.Abs(s) - _halfWidth;
            double wall = Math.Max(Math.Max(top, bottom), Math.Max(ends, sides));

            double radius = span.HalfLength - LegHalfWidth;
            double opening = -_deckThickness - CrownThickness;
            double height = opening - span.Spring;
            if (radius < 1.0 || height < 1.0) return wall;
            double rise = Math.Min(radius, height);
            double springLine = opening - rise;
            // The straight part reaches below the feet so it cuts the wall all the way down.
            double straightBottom = span.Spring - 1.0;
            double straight = BridgeSdf.Rectangle(h, g - (springLine + straightBottom) * 0.5,
                radius, (springLine - straightBottom) * 0.5);
            double arched = BridgeSdf.Ellipse(h, g - springLine, radius, rise);
            return Math.Max(wall, -Math.Min(straight, arched));
        }

        /// <summary>Height of a support's pillar foot, along a span's gravity, from the span's centre.</summary>
        static double Foot(BridgeSupport support, Vector3D planetCenter, Vector3D center, Vector3D gravityUp)
        {
            Vector3D foot = support.Deck - Vector3D.Normalize(support.Deck - planetCenter) * support.FootDepth;
            return Vector3D.Dot(foot - center, gravityUp);
        }
    }

    /// <summary>A column of an arch bridge, standing on the arch or, past it, on the terrain.</summary>
    public struct BridgeColumn
    {
        /// <summary>Distance along the bridge.</summary>
        public double Along;
        /// <summary>Deck top centre above the column.</summary>
        public Vector3D Deck;
        /// <summary>How far below the deck top, along gravity, the column reaches.</summary>
        public double FootDepth;
        /// <summary>Half its thickness along the bridge.</summary>
        public double HalfThickness;
    }

    /// <summary>
    /// An arch bridge: one parabolic arch rib springing from the ground on each side of the gap and
    /// rising to meet the deck's underside, with columns standing on it (and on the terrain past it)
    /// to carry the deck. The arch is drawn along the bridge's curve, in the gravity at its middle.
    /// The parabola is measured down from the deck's own height (which climbs with the bridge's
    /// grade and falls with the planet's curve), so the rib keeps its clearance under the deck all
    /// along; a parabola measured from a level line would rise through a sloping deck beside its
    /// crown. Slim pylons on the rib carry the deck across that clearance.
    /// </summary>
    public sealed class ArchStructure : IBridgeStructure
    {
        // Rib and columns are thick so they still hold samples at the coarsest LODs.
        public const double RibThickness = 10.0;
        // Along the axis: columns on the terrain, and the slimmer pylons on the rib (still two
        // samples thick at the second LOD).
        public const double ColumnHalfThickness = 4.0;
        public const double PylonHalfThickness = 2.0;
        // The rib continues this far past each spring point, into the ground, so it looks rooted.
        public const double SpringExtension = 8.0;
        // Smooth-min radius: rounds the columns into the rib.
        const double Blend = 1.0;
        // Spacing of the deck height samples the rib is measured from.
        const double ProfileStep = 1.0;

        readonly double _halfWidth;
        readonly double _deckThickness;
        readonly Vector3D _center, _gravityUp;
        readonly double _centerAlong;
        readonly double _from, _to;          // the rib's range along the bridge, springs extended
        readonly double _crownH, _curvature, _deckOffset;
        // Deck-top height along gravity at the middle, every ProfileStep from _profileStart.
        readonly double[] _deckProfile;
        readonly double _profileStart;
        readonly Vector3D[] _columnDecks;
        readonly Vector3D[] _columnUps;
        readonly double[] _columnDepths;
        readonly double[] _columnAlongs;
        readonly double[] _columnHalfThicknesses;
        readonly BridgeFoot[] _feet;

        /// <param name="centerAlong">Where the arch's middle is along the bridge; its frame starts at the deck top there.</param>
        /// <param name="springs">Positions of the two spring points along the bridge, from its middle.</param>
        /// <param name="crownH">Position of the rib's crown along the bridge, from its middle.</param>
        /// <param name="curvature">See <see cref="RibCenter"/> (negative).</param>
        /// <param name="deckOffset">Height of the rib's centreline at the crown, from the deck top above it.</param>
        public ArchStructure(BridgeAxis axis, double centerAlong, Vector2D springs, double crownH, double curvature,
            double deckOffset, IList<BridgeColumn> columns, Vector3D planetCenter, double halfWidth, double deckThickness)
        {
            _halfWidth = halfWidth;
            _deckThickness = deckThickness;
            _centerAlong = centerAlong;
            _center = axis.PointAt(centerAlong);
            _gravityUp = Vector3D.Normalize(_center - planetCenter);
            _from = springs.X - SpringExtension;
            _to = springs.Y + SpringExtension;
            _crownH = crownH;
            _curvature = curvature;
            _deckOffset = deckOffset;
            _profileStart = _from - ProfileStep;
            int samples = (int)Math.Ceiling((_to - _from) / ProfileStep) + 3;
            _deckProfile = new double[samples];
            for (int i = 0; i < samples; i++)
                _deckProfile[i] = DeckHeight(axis, _centerAlong, _center, _gravityUp, _profileStart + i * ProfileStep);

            _columnDecks = new Vector3D[columns.Count];
            _columnUps = new Vector3D[columns.Count];
            _columnDepths = new double[columns.Count];
            _columnAlongs = new double[columns.Count];
            _columnHalfThicknesses = new double[columns.Count];
            var feet = new List<BridgeFoot>();
            for (int i = 0; i < columns.Count; i++)
            {
                Vector3D up = Vector3D.Normalize(columns[i].Deck - planetCenter);
                _columnDecks[i] = columns[i].Deck;
                _columnUps[i] = up;
                _columnDepths[i] = columns[i].FootDepth;
                _columnAlongs[i] = columns[i].Along;
                _columnHalfThicknesses[i] = columns[i].HalfThickness;
                feet.Add(new BridgeFoot
                {
                    Point = columns[i].Deck - up * columns[i].FootDepth,
                    Reach = columns[i].HalfThickness + 2.0
                });
            }
            // The rib's underside, every few metres.
            for (double h = _from; h <= _to + 0.01; h += 8.0)
            {
                Vector3D deck = axis.PointAt(_centerAlong + h);
                feet.Add(new BridgeFoot
                {
                    Point = deck + _gravityUp * (RibCenter(h) - RibThickness - Vector3D.Dot(deck - _center, _gravityUp)),
                    Reach = 10.0
                });
            }
            _feet = feet.ToArray();
        }

        public string Name => "arch";
        public IList<BridgeFoot> Feet => _feet;
        public int ColumnCount => _columnDecks.Length;

        public double Distance(BridgePoint point)
        {
            double h = point.Along - _centerAlong;
            double g = Vector3D.Dot(point.World - _center, _gravityUp);
            double s = point.Lateral;
            double clamped = MathHelper.Clamp(h, _from, _to);
            double slope = DeckSlope(clamped) + 2.0 * _curvature * (clamped - _crownH);
            double rib = Math.Abs(g - RibCenter(clamped)) / Math.Sqrt(1.0 + slope * slope) - RibThickness * 0.5;
            double ends = Math.Abs(h - (_from + _to) * 0.5) - (_to - _from) * 0.5;
            rib = Math.Max(Math.Max(rib, ends), Math.Abs(s) - _halfWidth);
            return BridgeSdf.SmoothMin(rib, Columns(point), Blend);
        }

        /// <summary>
        /// Height of the rib's centreline at a position along the bridge, from its middle: a parabola
        /// below the deck, <c>deck(h) + deckOffset + curvature (h - crownH)²</c>.
        /// </summary>
        public double RibCenter(double h)
        {
            return Deck(h) + _deckOffset + _curvature * (h - _crownH) * (h - _crownH);
        }

        /// <summary>The deck-top height along the arch's gravity at a position from its middle.</summary>
        double Deck(double h)
        {
            double position = MathHelper.Clamp((h - _profileStart) / ProfileStep, 0.0, _deckProfile.Length - 1.001);
            int index = (int)position;
            return _deckProfile[index] + (_deckProfile[index + 1] - _deckProfile[index]) * (position - index);
        }

        double DeckSlope(double h)
        {
            double position = MathHelper.Clamp((h - _profileStart) / ProfileStep, 0.0, _deckProfile.Length - 1.001);
            int index = (int)position;
            return (_deckProfile[index + 1] - _deckProfile[index]) / ProfileStep;
        }

        /// <summary>
        /// The deck-top height, along <paramref name="gravityUp"/> from <paramref name="center"/>, a
        /// distance <paramref name="h"/> from <paramref name="centerAlong"/> along the bridge.
        /// </summary>
        public static double DeckHeight(BridgeAxis axis, double centerAlong, Vector3D center, Vector3D gravityUp, double h)
        {
            return Vector3D.Dot(axis.PointAt(centerAlong + h) - center, gravityUp);
        }

        /// <summary>The nearest columns along the axis: each a box from the deck's mid-plane down to its foot.</summary>
        double Columns(BridgePoint point)
        {
            if (_columnDecks.Length == 0) return double.MaxValue;
            double along = point.Along;
            int nearest = 0;
            while (nearest < _columnAlongs.Length - 1 &&
                   Math.Abs(_columnAlongs[nearest + 1] - along) < Math.Abs(_columnAlongs[nearest] - along))
                nearest++;
            double best = double.MaxValue;
            for (int i = Math.Max(0, nearest - 1); i <= Math.Min(_columnDecks.Length - 1, nearest + 1); i++)
            {
                double top = -_deckThickness * 0.5;
                double half = (_columnDepths[i] + top) * 0.5;
                double g = Vector3D.Dot(point.World - _columnDecks[i], _columnUps[i]);
                best = Math.Min(best, BridgeSdf.Box(along - _columnAlongs[i], g - (top - half), point.Lateral,
                    _columnHalfThicknesses[i], half, _halfWidth));
            }
            return best;
        }
    }
}
