using System;
using System.Collections.Generic;
using HighwayEngineer.Common.Helpers;
using HighwayEngineer.Common.Roads;
using Sandbox.Game.Entities;
using VRageMath;

namespace HighwayEngineer.Common.Structures
{
    /// <summary>What a bridge is planned with, besides the curves it follows.</summary>
    public struct BridgeOptions
    {
        public BridgeDesign Design;
        public double Width;
        public double FillDepth;
        public double SegmentLength;
        /// <summary>Steepest climb of an approach ramp's top, in degrees.</summary>
        public double RampGradeDegrees;
        /// <summary>Angle of an approach ramp's sides; 0 for the default.</summary>
        public double RampSideDegrees;
        /// <summary>Steepest climb of the deck, in degrees; 0 for <see cref="BridgePlanner.MaxGradeDegrees"/>.</summary>
        public double MaxGradeDegrees;
    }

    /// <summary>A planned bridge: its shape, with the design chosen, and what it is made of.</summary>
    public sealed class BridgePlan
    {
        public BridgeShape Shape;
        /// <summary>Deck length along its segments.</summary>
        public double Length;
        /// <summary>Concrete: the structure under the deck, the deck slab and the parapets.</summary>
        public double StructureCubicMeters;
        /// <summary>Road fill on the deck, between the parapets.</summary>
        public double PavementCubicMeters;
        /// <summary>Fill of the approach ramps above the terrain.</summary>
        public double RampCubicMeters;
        public BridgeDesign Design;
        /// <summary>Which design was chosen and why, for the player.</summary>
        public string Summary;
        /// <summary>The deck-top's distance from the planet's centre at each node of its curves.</summary>
        public double[] NodeRadii;
    }

    /// <summary>
    /// Plans bridges: lays a deck over road curves and chooses what carries it. Shared by the server, which
    /// prices and builds the plan, and the client, which previews the same shape while the road is drafted.
    /// </summary>
    public static class BridgePlanner
    {
        public const double MinLengthMeters = 10.0;
        public const double MaxGradeDegrees = 20.0;
        // Beam bridges: equal spans of about this.
        public const double BeamSpanMeters = 80.0;
        // Arch bridges: columns carry the deck about this far apart.
        public const double ColumnSpacingMeters = 40.0;
        // Arch bridges: the rise-to-span ratio aimed for, and the range accepted.
        public const double TargetArchRatio = 0.2;
        public const double MinArchRatio = 0.1;
        public const double MaxArchRatio = 0.6;
        public const double MinArchSpanMeters = 30.0;
        // The rib's top stays at least this far under the deck's underside, pylons bridging the gap.
        public const double CrownClearance = 2.0;
        // Arch bridges: pylons stand on the rib this far apart, one at the crown.
        public const double PylonSpacingMeters = 15.0;
        // Terrain must stay this far under the rib, except this close to its springs.
        public const double ArchClearance = 1.0;
        public const double SpringZone = 15.0;
        // Spacing of the terrain samples along the deck when looking for the arch's springs.
        public const double ProfileStep = 2.0;
        // Spacing of the samples of the structure's profile when estimating its volume.
        public const double VolumeStep = 2.0;
        // Pillars continue this far below the lowest terrain under them, to hide the joint.
        public const double FootingDepth = 6.0;
        // Deep canyons need tall pillars; the per-segment storage limit bounds them anyway.
        public const double MaxFootDepth = 3000.0;
        // Spacing of the terrain samples across a pillar's footprint.
        public const double FootprintStep = 4.0;
        // A curved deck gets joints this close, so each turns by at most this much; but no
        // segment gets shorter than MinJointMeters, and none turns by more than MaxJointBend.
        public const double JointBendDegrees = 5.0;
        public const double MaxJointBendDegrees = 15.0;
        public const double MinJointMeters = 6.0;
        // Spacing of the samples along the road's curves that the deck is laid out on.
        public const double CurveStepMeters = 1.0;
        // Approach ramps: an embankment runs out at most this far; they reach this far below the
        // lowest terrain they cover, at most this deep.
        public const double MaxRampLength = 60.0;
        public const double RampFooting = 2.0;
        public const double MaxRampDepth = 30.0;
        public const double RampStep = 3.0;
        public const double DefaultRampSideDegrees = 45.0;

        /// <summary>
        /// Plans a bridge along road curves, from the deck-top point <paramref name="start"/> over
        /// the first curve's start to <paramref name="end"/> over the last one's end (any thread: it
        /// only reads the planet's terrain). <paramref name="pins"/> (null, or one per node of the
        /// curves) holds the deck-top radius at nodes the deck must pass, NaN elsewhere; its height
        /// changes evenly between the pins and the ends. With <see cref="BridgeDesign.Auto"/> the
        /// design needing less concrete is chosen. Null with the reason in <paramref name="error"/>
        /// when no bridge fits.
        /// </summary>
        public static BridgePlan Plan(MyPlanet planet, IList<RoadCurve> curves, Vector3D start, Vector3D end,
            double[] pins, BridgeOptions options, out string error)
        {
            error = null;
            Vector3D center = planet.PositionComp.GetPosition();
            double startRadius = Vector3D.Distance(start, center), endRadius = Vector3D.Distance(end, center);
            var nodeRadii = new double[curves.Count + 1];
            for (int i = 0; i < nodeRadii.Length; i++)
                nodeRadii[i] = pins != null && i < pins.Length ? pins[i] : double.NaN;
            nodeRadii[0] = startRadius;
            nodeRadii[curves.Count] = endRadius;
            double width = MathHelper.Clamp(options.Width, RoadLimits.MinWidthMeters, RoadLimits.MaxWidthMeters);
            double fillDepth = MathHelper.Clamp(options.FillDepth, RoadLimits.MinDepthMeters, RoadLimits.MaxDepthMeters);
            double segmentLength = MathHelper.Clamp(options.SegmentLength, RoadLimits.MinSegmentDistanceMeters,
                RoadLimits.MaxSegmentDistanceMeters);
            double[] nodeDistances;
            List<Vector3D> deck = DeckJoints(curves, center, nodeRadii, segmentLength,
                BridgeShape.HalfWidthFor(width), out nodeDistances, out error);
            if (deck == null) return null;
            var axis = new BridgeAxis(deck, center);
            double length = axis.Length;
            if (length < MinLengthMeters)
            {
                error = LocHelper.Message("HighwayEngineer_Bridge_TooShort", MinLengthMeters, Math.Round(length));
                return null;
            }
            double gradeDegrees = options.MaxGradeDegrees > 0.0
                ? Math.Min(options.MaxGradeDegrees, MaxGradeDegrees)
                : MaxGradeDegrees;
            double maxGrade = Math.Tan(MathHelper.ToRadians(gradeDegrees));
            for (int i = 1; i < nodeRadii.Length; i++)
                if (Math.Abs(nodeRadii[i] - nodeRadii[i - 1]) >
                    Math.Max(nodeDistances[i] - nodeDistances[i - 1], 1e-3) * maxGrade)
                {
                    error = curves.Count == 1
                        ? LocHelper.Message("HighwayEngineer_Bridge_EndsTooSteep", gradeDegrees)
                        : LocHelper.Message("HighwayEngineer_Bridge_DeckTooSteep", i - 1, i, gradeDegrees);
                    return null;
                }
            var layout = new Layout(planet, axis, width, fillDepth);
            string tooHigh;
            if (!layout.TryClearance(out error, out tooHigh))
            {
                if (error == null)
                    error = LocHelper.Message("HighwayEngineer_Bridge_NoSurface");
                return null;
            }
            // Piers stand on the gap's floor, so a deck too high above it is for an arch, which
            // only needs its springs on the ground or the walls.
            if (tooHigh != null && options.Design == BridgeDesign.Beam)
            {
                error = LocHelper.Message("HighwayEngineer_Bridge_TooHighForBeam", tooHigh);
                return null;
            }

            BeamStructure beam = null;
            ArchStructure arch = null;
            string archProblem = null;
            if (options.Design != BridgeDesign.Arch && tooHigh == null && !layout.TryBeam(out beam) ||
                options.Design != BridgeDesign.Beam && !layout.TryArch(out arch, out archProblem))
            {
                error = LocHelper.Message("HighwayEngineer_Bridge_NoSurface");
                return null;
            }
            if (arch == null && (options.Design == BridgeDesign.Arch || tooHigh != null))
            {
                error = tooHigh != null
                    ? LocHelper.Message("HighwayEngineer_Bridge_TooHighNoArch", tooHigh, archProblem)
                    : LocHelper.Message("HighwayEngineer_Bridge_ArchNoFit", archProblem);
                return null;
            }
            double rampGrade = Math.Tan(MathHelper.ToRadians(MathHelper.Clamp(options.RampGradeDegrees, 1.0, 30.0)));
            double rampSlope = Math.Tan(MathHelper.ToRadians(options.RampSideDegrees > 0.0
                ? MathHelper.Clamp(options.RampSideDegrees, 15.0, 75.0)
                : DefaultRampSideDegrees));
            double startFill, endFill;
            BridgeRampSpec startRamp, endRamp;
            if (!layout.TryRamp(false, rampGrade, rampSlope, out startRamp, out startFill) ||
                !layout.TryRamp(true, rampGrade, rampSlope, out endRamp, out endFill))
            {
                error = LocHelper.Message("HighwayEngineer_Bridge_NoSurfaceEnds");
                return null;
            }
            double beamVolume = beam != null ? layout.Volume(beam) : double.MaxValue;
            double archVolume = arch != null ? layout.Volume(arch) : double.MaxValue;
            IBridgeStructure chosen = archVolume < beamVolume ? (IBridgeStructure)arch : beam;
            var shape = new BridgeShape(axis, center, width, fillDepth, chosen, startRamp, endRamp);
            // The slab is concrete except the fill between the parapets; the parapets stand on it.
            double inner = shape.Width - 2.0 * BridgeShape.ParapetWidth;
            double slab = shape.Width * shape.DeckThickness - inner * fillDepth +
                2.0 * BridgeShape.ParapetWidth * BridgeShape.ParapetHeight;
            return new BridgePlan
            {
                Shape = shape,
                Length = shape.Length,
                StructureCubicMeters = Math.Min(beamVolume, archVolume) + slab * shape.Length,
                PavementCubicMeters = inner * fillDepth * shape.Length,
                RampCubicMeters = startFill + endFill,
                Design = chosen == arch ? BridgeDesign.Arch : BridgeDesign.Beam,
                Summary = Summary(options.Design, chosen, beamVolume, archVolume, archProblem),
                NodeRadii = nodeRadii
            };
        }

        /// <summary>
        /// The deck's joints: evenly spaced along the curves, about <paramref name="segmentLength"/>
        /// apart, closer where the road bends so no joint turns much. Null with the reason when the
        /// road bends too tightly for a deck of this width. <paramref name="nodeRadii"/>' NaN nodes
        /// get the radius straight between their pinned neighbours; <paramref name="nodeDistances"/>
        /// is each node's distance along the deck.
        /// </summary>
        static List<Vector3D> DeckJoints(IList<RoadCurve> curves, Vector3D center, double[] nodeRadii,
            double segmentLength, double halfWidth, out double[] nodeDistances, out string error)
        {
            error = null;
            nodeDistances = new double[curves.Count + 1];
            double radius = (nodeRadii[0] + nodeRadii[curves.Count]) * 0.5;
            // The curves as a fine polyline of directions from the planet's centre, with distances.
            var directions = new List<Vector3D>();
            var distances = new List<double>();
            for (int c = 0; c < curves.Count; c++)
            {
                RoadCurve curve = curves[c];
                int steps = Math.Max(8, (int)Math.Ceiling(curve.Arc * radius / CurveStepMeters));
                for (int i = directions.Count == 0 ? 0 : 1; i <= steps; i++)
                {
                    Vector3D direction = curve.Evaluate(i / (double)steps);
                    distances.Add(directions.Count == 0 ? 0.0 :
                        distances[distances.Count - 1] + Angle(directions[directions.Count - 1], direction) * radius);
                    directions.Add(direction);
                }
                nodeDistances[c + 1] = distances[distances.Count - 1];
            }
            // Unpinned nodes: straight between the pins on either side.
            for (int i = 1, pinned = 0; i < nodeRadii.Length; i++)
            {
                if (double.IsNaN(nodeRadii[i])) continue;
                double span = nodeDistances[i] - nodeDistances[pinned];
                for (int k = pinned + 1; k < i; k++)
                    nodeRadii[k] = nodeRadii[pinned] + (nodeRadii[i] - nodeRadii[pinned]) *
                        (span > 1e-9 ? (nodeDistances[k] - nodeDistances[pinned]) / span : 0.0);
                pinned = i;
            }
            double length = distances[distances.Count - 1];
            if (length < 1e-3)
            {
                error = LocHelper.Message("HighwayEngineer_Bridge_NoLength");
                return null;
            }
            // How far the road turns, measured over a few metres so the polyline's noise does not count.
            double turn = 0.0;
            int stride = Math.Max(1, (int)Math.Round(4.0 / CurveStepMeters));
            for (int i = stride; i + stride < directions.Count; i += stride)
                turn += Heading(directions[i - stride], directions[i], directions[i + stride]);
            int joints = Math.Max(1, (int)Math.Round(length / segmentLength));
            joints = Math.Max(joints, (int)Math.Ceiling(turn / MathHelper.ToRadians(JointBendDegrees)));
            joints = Math.Max(1, Math.Min(joints, (int)Math.Floor(length / MinJointMeters)));
            double bend = turn / joints;
            // The joints' mitering planes must not cross under the deck on the inside of a bend.
            if (bend > MathHelper.ToRadians(MaxJointBendDegrees) || bend > 1e-6 && length / joints / bend < halfWidth * 1.5)
            {
                error = LocHelper.Message("HighwayEngineer_Bridge_BendTooTight");
                return null;
            }

            var deck = new List<Vector3D>(joints + 1);
            int at = 0, piece = 1;
            for (int j = 0; j <= joints; j++)
            {
                double target = length * j / joints;
                while (at < distances.Count - 2 && distances[at + 1] < target)
                    at++;
                double span = distances[at + 1] - distances[at];
                double t = span > 1e-9 ? MathHelper.Clamp((target - distances[at]) / span, 0.0, 1.0) : 0.0;
                Vector3D direction = Vector3D.Normalize(Vector3D.Lerp(directions[at], directions[at + 1], t));
                while (piece < nodeDistances.Length - 1 && nodeDistances[piece] < target)
                    piece++;
                double pieceSpan = nodeDistances[piece] - nodeDistances[piece - 1];
                double f = pieceSpan > 1e-9
                    ? MathHelper.Clamp((target - nodeDistances[piece - 1]) / pieceSpan, 0.0, 1.0)
                    : 1.0;
                deck.Add(center + direction * (nodeRadii[piece - 1] + (nodeRadii[piece] - nodeRadii[piece - 1]) * f));
            }
            return deck;
        }

        /// <summary>The change of heading at <paramref name="at"/> between the chords from and to its neighbours.</summary>
        static double Heading(Vector3D before, Vector3D at, Vector3D after)
        {
            Vector3D a = at - before, b = after - at;
            a -= at * Vector3D.Dot(a, at);
            b -= at * Vector3D.Dot(b, at);
            if (a.Normalize() < 1e-12 || b.Normalize() < 1e-12) return 0.0;
            return Math.Acos(MathHelper.Clamp(Vector3D.Dot(a, b), -1.0, 1.0));
        }

        /// <summary>
        /// The curves of geometry segments <paramref name="run"/> (a quote's or contract's), and the
        /// deck-top ends over them: on the terrain (roads included) under the first's start and the
        /// last's end, raised by their <paramref name="lifts"/> (per geometry node, or null).
        /// <paramref name="pins"/>: for <see cref="Plan"/>, the deck-top radius at the run's lifted
        /// inner nodes (that far above the terrain under them), NaN at the others.
        /// </summary>
        public static bool TryReadRun(MyPlanet planet, double[] geometry, BridgeRun run, double[] lifts,
            List<RoadCurve> curves, out Vector3D start, out Vector3D end, out double[] pins)
        {
            start = end = Vector3D.Zero;
            pins = null;
            curves.Clear();
            int per = Constants.GEOMETRY_VALUES_PER_SEGMENT;
            if (planet == null || geometry == null || run.First < 0 || (run.Last + 1) * per > geometry.Length)
                return false;
            var values = new double[per];
            for (int i = run.First; i <= run.Last; i++)
            {
                Array.Copy(geometry, i * per, values, 0, per);
                RoadCurve curve;
                if (!RoadCurve.TryFromLocalGeometry(planet.WorldMatrix, values, out curve)) return false;
                curves.Add(curve);
            }
            Vector3D center = planet.PositionComp.GetPosition();
            pins = new double[curves.Count + 1];
            for (int node = 0; node <= curves.Count; node++)
            {
                pins[node] = double.NaN;
                double lift = Lift(lifts, run.First + node);
                if (node > 0 && node < curves.Count && lift <= 0.0) continue;
                Vector3D direction = node < curves.Count ? curves[node].Start : curves[node - 1].End;
                Vector3D surface = SurfaceUnder(planet, center + direction * planet.AverageRadius);
                pins[node] = Vector3D.Distance(surface, center) + lift;
                if (node == 0) start = center + direction * pins[node];
                else if (node == curves.Count) end = center + direction * pins[node];
            }
            return true;
        }

        static double Lift(double[] lifts, int node)
        {
            return lifts != null && node >= 0 && node < lifts.Length && lifts[node] > 0.0 ? lifts[node] : 0.0;
        }

        /// <summary>Which design was built and why, with the concrete each would have needed.</summary>
        static string Summary(BridgeDesign design, IBridgeStructure chosen, double beamVolume, double archVolume,
            string archProblem)
        {
            bool hasBeam = beamVolume < double.MaxValue, hasArch = archVolume < double.MaxValue;
            string beam = hasBeam ? LocHelper.Message("HighwayEngineer_Bridge_Volume_Beam", Cubic(beamVolume)) : null;
            string arch = hasArch ? LocHelper.Message("HighwayEngineer_Bridge_Volume_Arch", Cubic(archVolume)) : null;
            string volumes = beam != null && arch != null
                ? LocHelper.Message("HighwayEngineer_Bridge_Volumes", beam, arch)
                : beam ?? arch ?? string.Empty;
            bool isArch = chosen.Name == "arch";
            if (design != BridgeDesign.Auto)
                return LocHelper.Message(isArch ? "HighwayEngineer_Bridge_Summary_ChosenArch" : "HighwayEngineer_Bridge_Summary_ChosenBeam", volumes);
            if (archProblem != null)
                return LocHelper.Message("HighwayEngineer_Bridge_Summary_NoArch", archProblem);
            return LocHelper.Message(isArch ? "HighwayEngineer_Bridge_Summary_CheaperArch" : "HighwayEngineer_Bridge_Summary_CheaperBeam", volumes);
        }

        static string Cubic(double volume)
        {
            return volume >= 1e6
                ? LocHelper.Message("HighwayEngineer_Bridge_MillionCubicMeters", volume / 1e6)
                : LocHelper.Message("HighwayEngineer_Common_CubicMeters", volume);
        }

        /// <summary>
        /// Lays out the structures that could carry a deck and its approach ramps (any thread: it
        /// only reads the terrain) and estimates their concrete and fill. Pillars and columns standing
        /// on the terrain reach just below the lowest ground under their whole footprint, so a wide
        /// one on a cross-slope does not hang off its downhill side.
        /// </summary>
        sealed class Layout
        {
            readonly MyPlanet _planet;
            readonly Vector3D _center;
            readonly BridgeAxis _axis;
            readonly double _length;
            readonly double _halfWidth;
            readonly double _deckHalfWidth;
            readonly double _deckThickness;

            public Layout(MyPlanet planet, BridgeAxis axis, double width, double fillDepth)
            {
                _planet = planet;
                _center = planet.PositionComp.GetPosition();
                _axis = axis;
                _length = axis.Length;
                _halfWidth = BridgeShape.StructureHalfWidthFor(width);
                _deckHalfWidth = BridgeShape.HalfWidthFor(width);
                _deckThickness = BridgeShape.SlabFor(fillDepth);
            }

            /// <summary>
            /// Whether the deck stays over the terrain, dipping at most
            /// <see cref="RoadLimits.BridgeTerrainToleranceMeters"/> into it; false with the reason in
            /// <paramref name="problem"/>, or none when the terrain could not be read.
            /// <paramref name="tooHigh"/>: where it is more than <see cref="RoadLimits.MaxBridgeHeightMeters"/>
            /// above the terrain (too tall for piers), or null.
            /// </summary>
            public bool TryClearance(out string problem, out string tooHigh)
            {
                problem = tooHigh = null;
                int samples = Math.Max(8, (int)Math.Ceiling(_length / ProfileStep));
                for (int i = 0; i <= samples; i++)
                {
                    double along = _length * i / samples;
                    Vector3D top = _axis.PointAt(along);
                    Vector3D ground = SurfaceUnder(_planet, top);
                    if (!IsTerrain(ground)) return false;
                    double height = Vector3D.Dot(top - ground, Up(top));
                    if (height < -RoadLimits.BridgeTerrainToleranceMeters)
                    {
                        problem = LocHelper.Message("HighwayEngineer_Bridge_IntoTerrain", Math.Round(along));
                        return false;
                    }
                    if (height > RoadLimits.MaxBridgeHeightMeters && tooHigh == null)
                        tooHigh = LocHelper.Message("HighwayEngineer_Bridge_TooHigh", RoadLimits.MaxBridgeHeightMeters, Math.Round(along));
                }
                return true;
            }

            /// <summary>Equal spans of about <see cref="BeamSpanMeters"/>. False when the terrain could not be read.</summary>
            public bool TryBeam(out BeamStructure beam)
            {
                beam = null;
                int spans = Math.Max(1, (int)Math.Round(_length / BeamSpanMeters));
                var supports = new List<BridgeSupport>();
                for (int i = 0; i <= spans; i++)
                {
                    double along = _length * i / spans;
                    Vector3D top = _axis.PointAt(along);
                    double gap = LowestGround(top, along, BeamStructure.LegHalfWidth);
                    if (double.IsNaN(gap)) return false;
                    supports.Add(new BridgeSupport
                    {
                        Along = along,
                        Deck = top,
                        FootDepth = MathHelper.Clamp(gap + FootingDepth, _deckThickness, MaxFootDepth)
                    });
                }
                beam = new BeamStructure(_axis, supports, _center, _halfWidth, _deckThickness);
                return true;
            }

            /// <summary>
            /// One parabolic rib springing from the terrain where the gap is deep enough for a rise of
            /// about <see cref="TargetArchRatio"/> of its span, its crown <see cref="CrownClearance"/> under
            /// the deck's underside, with slim pylons every <see cref="PylonSpacingMeters"/> on it (one
            /// at the crown) and columns every <see cref="ColumnSpacingMeters"/> on the terrain past it.
            /// <paramref name="arch"/> is null, with the reason in <paramref name="problem"/>, when no
            /// arch fits. False when the terrain could not be read.
            /// </summary>
            public bool TryArch(out ArchStructure arch, out string problem)
            {
                arch = null;
                problem = null;
                int samples = Math.Max(8, (int)Math.Ceiling(_length / ProfileStep));
                var tops = new Vector3D[samples + 1];
                var grounds = new Vector3D[samples + 1];
                var depths = new double[samples + 1];
                double deepest = 0.0;
                for (int i = 0; i <= samples; i++)
                {
                    tops[i] = _axis.PointAt(_length * i / samples);
                    grounds[i] = SurfaceUnder(_planet, tops[i]);
                    if (!IsTerrain(grounds[i])) return false;
                    depths[i] = Vector3D.Dot(tops[i] - grounds[i], Up(tops[i]));
                    deepest = Math.Max(deepest, depths[i]);
                }

                // The spring depth whose arch comes closest to the target rise-to-span ratio.
                int first = -1, last = -1;
                double bestScore = double.MaxValue;
                for (double spring = _deckThickness + ArchStructure.RibThickness + 5.0; spring <= deepest; spring += 5.0)
                {
                    double depth = spring;
                    int i0 = Array.FindIndex(depths, d => d >= depth);
                    int i1 = Array.FindLastIndex(depths, d => d >= depth);
                    if (i0 < 0) break;
                    double span = _length * (i1 - i0) / samples;
                    if (span < MinArchSpanMeters) break;
                    double ratio = (spring - _deckThickness - ArchStructure.RibThickness * 0.5) / span;
                    if (ratio < MinArchRatio || ratio > MaxArchRatio) continue;
                    double score = Math.Abs(ratio - TargetArchRatio);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        first = i0;
                        last = i1;
                    }
                }
                if (first < 0)
                {
                    problem = LocHelper.Message("HighwayEngineer_Bridge_Arch_ShallowNarrow");
                    return true;
                }

                // The arch's frame, at the deck above its middle: positions along the bridge, heights along gravity.
                double middleAlong = _length * (first + last) * 0.5 / samples;
                Vector3D middle = _axis.PointAt(middleAlong);
                Vector3D up = Up(middle);
                double h0 = _length * first / samples - middleAlong, g0 = Vector3D.Dot(grounds[first] - middle, up);
                double h1 = _length * last / samples - middleAlong, g1 = Vector3D.Dot(grounds[last] - middle, up);
                Func<double, double> deck = h => ArchStructure.DeckHeight(_axis, middleAlong, middle, up, h);

                // A parabola through both springs, measured down from the deck (so it follows its
                // grade and the planet's curve), whose crown keeps its clearance under the deck.
                double offset = -_deckThickness - ArchStructure.RibThickness * 0.5 - CrownClearance;
                double below0 = deck(h0) + offset - g0, below1 = deck(h1) + offset - g1;
                if (below0 <= 1.0 || below1 <= 1.0)
                {
                    problem = LocHelper.Message("HighwayEngineer_Bridge_Arch_Shallow");
                    return true;
                }
                double k = Math.Sqrt(below0 / below1);
                double crownH = (h0 + k * h1) / (1.0 + k);
                double curvature = -below0 / ((crownH - h0) * (crownH - h0));
                Func<double, double> rib = h => deck(h) + offset + curvature * (h - crownH) * (h - crownH);

                // The rib follows the deck, so a deck bent at lifted nodes bends the rib: its rise over
                // the line between its springs must still be within the accepted ratios.
                double rise = 0.0;
                for (int i = first + 1; i < last; i++)
                {
                    double h = _length * i / samples - middleAlong;
                    rise = Math.Max(rise, rib(h) - (g0 + (g1 - g0) * (h - h0) / (h1 - h0)));
                }
                double riseRatio = rise / (h1 - h0);
                if (riseRatio < MinArchRatio || riseRatio > MaxArchRatio)
                {
                    problem = LocHelper.Message("HighwayEngineer_Bridge_Arch_Bends", riseRatio, MinArchRatio, MaxArchRatio);
                    return true;
                }

                // The terrain must stay under the rib, except near its springs.
                for (int i = first + 1; i < last; i++)
                {
                    double h = _length * i / samples - middleAlong;
                    if (Math.Min(h - h0, h1 - h) < SpringZone) continue;
                    double underside = rib(h) - ArchStructure.RibThickness * 0.5;
                    if (Vector3D.Dot(grounds[i] - middle, up) > underside - ArchClearance)
                    {
                        problem = LocHelper.Message("HighwayEngineer_Bridge_Arch_Terrain");
                        return true;
                    }
                }

                // Pylons on the rib, out from the crown (so one stands there) every PylonSpacingMeters,
                // into the rib so the blend closes the joint; columns on the terrain past the arch.
                var columns = new List<BridgeColumn>();
                int pylons = (int)Math.Ceiling(Math.Max(crownH - h0, h1 - crownH) / PylonSpacingMeters);
                for (int i = -pylons; i <= pylons; i++)
                {
                    double h = crownH + i * PylonSpacingMeters;
                    if (h <= h0 + ArchStructure.PylonHalfThickness || h >= h1 - ArchStructure.PylonHalfThickness) continue;
                    Vector3D top = _axis.PointAt(middleAlong + h);
                    double depth = Vector3D.Dot(top - middle, up) - (rib(h) + ArchStructure.RibThickness * 0.5) + 1.0;
                    columns.Add(new BridgeColumn
                    {
                        Along = middleAlong + h,
                        Deck = top,
                        FootDepth = Math.Min(depth, MaxFootDepth),
                        HalfThickness = ArchStructure.PylonHalfThickness
                    });
                }
                int count = Math.Max(1, (int)Math.Round(_length / ColumnSpacingMeters));
                for (int i = 1; i < count; i++)
                {
                    double along = _length * i / count;
                    double h = along - middleAlong;
                    if (h > h0 - ArchStructure.ColumnHalfThickness && h < h1 + ArchStructure.ColumnHalfThickness) continue;
                    Vector3D top = _axis.PointAt(along);
                    double gap = LowestGround(top, along, ArchStructure.ColumnHalfThickness);
                    if (double.IsNaN(gap)) return false;
                    // None where the deck already rests on the ground.
                    if (gap < _deckThickness + 1.0) continue;
                    columns.Add(new BridgeColumn
                    {
                        Along = along,
                        Deck = top,
                        FootDepth = Math.Min(gap + FootingDepth, MaxFootDepth),
                        HalfThickness = ArchStructure.ColumnHalfThickness
                    });
                }
                columns.Sort((a, b) => a.Along.CompareTo(b.Along));
                arch = new ArchStructure(_axis, middleAlong, new Vector2D(h0, h1), crownH, curvature, offset,
                    columns, _center, _halfWidth, _deckThickness);
                return true;
            }

            /// <summary>
            /// The approach ramp at one end: its top runs out from the deck until the terrain reaches
            /// it, and it reaches just below the lowest terrain it covers. <paramref name="fill"/> is
            /// its volume above the terrain. False when the terrain could not be read.
            /// </summary>
            public bool TryRamp(bool atEnd, double grade, double slope, out BridgeRampSpec ramp, out double fill)
            {
                ramp = new BridgeRampSpec { Grade = grade, Slope = slope };
                fill = 0.0;
                Vector3D origin = atEnd ? _axis.End : _axis.Start;
                Vector3D up = Up(origin);
                Vector3D outward = atEnd ? _axis.Level(_axis.PieceCount - 1) : -_axis.Level(0);
                outward = Vector3D.Normalize(outward - up * Vector3D.Dot(outward, up));
                Vector3D side = Vector3D.Normalize(Vector3D.Cross(outward, up));

                // Leave the deck at its own grade (rise per metre outward from its end piece).
                Vector3D endPiece = atEnd
                    ? _axis.Point(_axis.PieceCount) - _axis.Point(_axis.PieceCount - 1)
                    : _axis.Point(0) - _axis.Point(1);
                double run = Vector3D.Dot(endPiece, outward);
                ramp.StartGrade = run > 1e-6 ? MathHelper.Clamp(Vector3D.Dot(endPiece, up) / run, -1.0, 1.0) : 0.0;

                // Ease into the road past the end: its height there (the median across the road, so a
                // boulder does not count) and its grade, at most the ramp's.
                const double transition = BridgeShape.TransitionMeters;
                double ahead = RoadHeight(origin, outward, side, up, transition + 4.0);
                double behind = RoadHeight(origin, outward, side, up, transition - 4.0);
                double target = RoadHeight(origin, outward, side, up, transition);
                if (double.IsNaN(target) || double.IsNaN(ahead) || double.IsNaN(behind)) return false;
                double roadGrade = MathHelper.Clamp((ahead - behind) / 8.0, -grade, grade);
                // Where the terrain falls away faster than the ramp may, it runs on down at its grade
                // as an embankment until it meets the terrain.
                double lowest = Math.Min(0.0, ramp.StartGrade) * transition - grade * transition;
                ramp.EndHeight = Math.Max(target, lowest);
                ramp.EndGrade = ramp.EndHeight > target ? -grade : roadGrade;
                // It meets the road at the transition's end, flush, then runs on under it for the tail.
                double tail = BridgeShape.RampTail;
                ramp.Length = transition + tail;
                if (ramp.EndHeight > target)
                {
                    ramp.Length = MaxRampLength + tail;
                    for (double a = transition; a <= MaxRampLength; a += RampStep)
                    {
                        double ground = GroundHeight(origin + outward * a, origin, up);
                        if (double.IsNaN(ground)) return false;
                        if (ground >= BridgeShape.RampTop(ramp, a))
                        {
                            ramp.Length = a + tail;
                            break;
                        }
                    }
                }

                // Over the footprint the ramp could cover, the terrain under its surface.
                double spread = MaxRampDepth / slope;
                var covered = new List<Vector2D>();
                double deepest = 0.0;
                for (double a = RampStep * 0.5; a < ramp.Length + spread; a += RampStep)
                for (double l = -(_deckHalfWidth + spread); l <= _deckHalfWidth + spread; l += RampStep)
                {
                    double surface = BridgeShape.RampTop(ramp, a) -
                                     (Math.Max(0.0, Math.Abs(l) - _deckHalfWidth) + Math.Max(0.0, a - ramp.Length)) * slope;
                    if (surface < -MaxRampDepth) continue;
                    double ground = GroundHeight(origin + outward * a + side * l, origin, up);
                    if (double.IsNaN(ground)) return false;
                    if (ground >= surface) continue;
                    covered.Add(new Vector2D(surface, ground));
                    deepest = Math.Max(deepest, -ground);
                }
                ramp.Depth = MathHelper.Clamp(deepest + RampFooting, _deckThickness + 1.0, MaxRampDepth);
                foreach (Vector2D cell in covered)
                    fill += (cell.X - Math.Max(cell.Y, -ramp.Depth)) * RampStep * RampStep;
                return true;
            }

            /// <summary>
            /// The road's (or terrain's) height <paramref name="a"/> metres out from a bridge end: the
            /// median across the road's width, so a boulder or a dip does not count. NaN when the
            /// planet reports no terrain there.
            /// </summary>
            double RoadHeight(Vector3D origin, Vector3D outward, Vector3D side, Vector3D up, double a)
            {
                var heights = new List<double>();
                double half = Math.Max(1.0, _deckHalfWidth - BridgeShape.ParapetWidth);
                for (int i = -2; i <= 2; i++)
                {
                    double height = GroundHeight(origin + outward * a + side * (half * i / 2.0), origin, up);
                    if (double.IsNaN(height)) return double.NaN;
                    heights.Add(height);
                }
                heights.Sort();
                return heights[2];
            }

            /// <summary>Height of the terrain under a point, along <paramref name="up"/>, from <paramref name="origin"/>; NaN when there is none.</summary>
            double GroundHeight(Vector3D point, Vector3D origin, Vector3D up)
            {
                Vector3D ground = SurfaceUnder(_planet, point);
                return IsTerrain(ground) ? Vector3D.Dot(ground - origin, up) : double.NaN;
            }

            /// <summary>
            /// Concrete in a structure, m³: its profile under the deck's centreline, sampled every
            /// <see cref="VolumeStep"/>, times its width (structures are uniform across the bridge).
            /// </summary>
            public double Volume(IBridgeStructure structure)
            {
                double deepest = _deckThickness;
                foreach (BridgeFoot foot in structure.Feet)
                    deepest = Math.Max(deepest,
                        Vector3D.Distance(_axis.PointAt(_axis.Locate(foot.Point).Along), foot.Point));
                int along = Math.Max(1, (int)Math.Ceiling(_length / VolumeStep));
                int down = (int)Math.Ceiling((deepest + 10.0) / VolumeStep);
                long inside = 0;
                for (int i = 0; i <= along; i++)
                {
                    Vector3D top = _axis.PointAt(_length * i / along);
                    Vector3D up = Up(top);
                    for (int j = 0; j <= down; j++)
                        if (structure.Distance(_axis.Locate(top - up * (j * VolumeStep))) < 0.0)
                            inside++;
                }
                return inside * VolumeStep * VolumeStep * _halfWidth * 2.0;
            }

            Vector3D Up(Vector3D point) => Vector3D.Normalize(point - _center);

            /// <summary>
            /// How far below <paramref name="top"/>, along gravity, the lowest terrain lies under a
            /// footprint as wide as the structure and <paramref name="halfLength"/> either side along
            /// the bridge: across it every <see cref="FootprintStep"/>, at its front, middle and back.
            /// NaN when the planet reports no terrain there.
            /// </summary>
            double LowestGround(Vector3D top, double along, double halfLength)
            {
                Vector3D up = Up(top);
                Vector3D side = _axis.SideAt(along);
                Vector3D level = _axis.LevelAt(along);
                int across = Math.Max(1, (int)Math.Ceiling(_halfWidth / FootprintStep));
                double deepest = double.MinValue;
                for (int i = -across; i <= across; i++)
                    for (int j = -1; j <= 1; j++)
                    {
                        Vector3D ground = SurfaceUnder(_planet,
                            top + side * (_halfWidth * i / across) + level * (halfLength * j));
                        if (!IsTerrain(ground)) return double.NaN;
                        deepest = Math.Max(deepest, Vector3D.Dot(top - ground, up));
                    }
                return deepest;
            }

            /// <summary>False when the planet reports a surface below its minimum radius (no terrain data there).</summary>
            bool IsTerrain(Vector3D ground)
            {
                return Vector3D.Distance(ground, _center) >= _planet.MinimumRadius - 1.0;
            }
        }

        static double Angle(Vector3D a, Vector3D b)
        {
            return Math.Acos(MathHelper.Clamp(Vector3D.Dot(Vector3D.Normalize(a), Vector3D.Normalize(b)), -1.0, 1.0));
        }

        /// <summary>The terrain straight below (towards the planet centre) a world point.</summary>
        public static Vector3D SurfaceUnder(MyPlanet planet, Vector3D point)
        {
            Vector3D center = planet.PositionComp.GetPosition();
            Vector3D direction = Vector3D.Normalize(point - center);
            double top = Math.Max(planet.AverageRadius, planet.MaximumRadius);
            return planet.GetClosestSurfacePointGlobal(center + direction * top);
        }

        /// <summary>How a road's bridges are planned: its width, depth, slope and sides.</summary>
        public static BridgeOptions OptionsFor(RoadBuildParameters road, BridgeBuildParameters bridge,
            BridgeDesign design)
        {
            return new BridgeOptions
            {
                Design = design,
                Width = road.WidthMeters,
                FillDepth = road.DepthMeters,
                SegmentLength = bridge.SegmentMeters,
                RampGradeDegrees = Math.Min(road.MaxSlopeDegrees, 15.0),
                RampSideDegrees = road.ShoulderAngleDegrees,
                // A bridge climbs no more than the road it carries; 0 would mean the default.
                MaxGradeDegrees = Math.Max(road.MaxSlopeDegrees, 0.01)
            };
        }
    }
}
