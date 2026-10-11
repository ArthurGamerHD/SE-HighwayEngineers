using System;
using System.Collections.Generic;
using VRageMath;

namespace HighwayEngineer.Common.Roads
{
    public struct RoadCrossing
    {
        public double DraftT;
        public Vector3D Direction;
    }

    /// <summary>Validity rules shared by the planner preview and the server.</summary>
    public static class RoadGeometry
    {
        public const int IntersectionSamples = 128;

        /// <summary>
        /// Crossings between two road curves, from sampled polylines. Samples are
        /// joined by great-circle arcs, which a gnomonic projection maps to
        /// straight lines, so each pair of arcs is tested exactly in 2D.
        /// <paramref name="adjacent"/>: <paramref name="road"/> starts where
        /// <paramref name="draft"/> ends, so their shared joint is not a crossing.
        /// With null <paramref name="hits"/> it stops at the first crossing.
        /// </summary>
        public static bool FindRoadCrossings(RoadCurve draft, RoadCurve road,
            List<RoadCrossing> hits, bool adjacent = false)
        {
            const int n = IntersectionSamples;
            // Cheap reject: the curves' bounding caps do not touch.
            Vector3D draftCenter = draft.Evaluate(0.5);
            Vector3D roadCenter = road.Evaluate(0.5);
            double reach = draft.Arc + road.Arc;
            if (reach < Math.PI && Vector3D.Dot(draftCenter, roadCenter) < Math.Cos(reach))
                return false;
            bool found = false;

            var a = new Vector3D[n + 1];
            var b = new Vector3D[n + 1];
            for (int i = 0; i <= n; i++)
            {
                a[i] = draft.Evaluate(i / (double)n);
                b[i] = road.Evaluate(i / (double)n);
            }
            for (int i = 0; i < n; i++)
            {
                Vector3D aMid = a[i] + a[i + 1];
                double aChord = Vector3D.Distance(a[i], a[i + 1]);
                for (int j = 0; j < n; j++)
                {
                    if (adjacent && i == n - 1 && j == 0) continue;
                    Vector3D bMid = b[j] + b[j + 1];
                    // Arcs can only cross if their chord midpoints are within the chord lengths.
                    double limit = aChord + Vector3D.Distance(b[j], b[j + 1]);
                    if (Vector3D.DistanceSquared(aMid * 0.5, bMid * 0.5) > limit * limit)
                        continue;
                    double s, r;
                    if (!TryIntersectArcs(a[i], a[i + 1], b[j], b[j + 1], out s, out r))
                        continue;
                    if (hits == null) return true;
                    found = true;
                    hits.Add(new RoadCrossing
                    {
                        DraftT = (i + s) / n,
                        Direction = road.Evaluate((j + r) / n)
                    });
                }
            }
            return found;
        }

        /// <summary>
        /// Adds to <paramref name="crossing"/> the indices of curves that cross another
        /// curve of the same road. <paramref name="segments"/> holds each curve's segment
        /// number; consecutive numbers share a node, which is not a crossing.
        /// </summary>
        public static void FindSelfCrossings(IList<RoadCurve> curves, IList<int> segments,
            ISet<int> crossing)
        {
            for (int i = 0; i < curves.Count; i++)
            for (int j = i + 1; j < curves.Count; j++)
            {
                bool adjacent = segments[j] == segments[i] + 1;
                if (!FindRoadCrossings(curves[i], curves[j], null, adjacent))
                    continue;
                crossing.Add(segments[i]);
                crossing.Add(segments[j]);
            }
        }

        /// <summary>
        /// The API rejects a road when the elevations at its fixed endpoints cannot be
        /// connected within the maximum slope. Heights are above the planet radius.
        /// </summary>
        public static bool ExceedsSlope(RoadCurve curve, double radius, double startHeight,
            double endHeight, RoadBuildParameters parameters)
        {
            // Sample the spherical Hermite centerline to estimate its actual travel distance.
            int samples = Math.Max(16, Math.Min(4096,
                (int)Math.Ceiling(curve.Arc * radius / 20.0)));
            Vector3D previous = curve.Start;
            double meters = 0.0;
            for (int i = 1; i <= samples; i++)
            {
                Vector3D next = curve.Evaluate(i / (double)samples);
                meters += radius * Math.Acos(MathHelper.Clamp(Vector3D.Dot(previous, next), -1.0, 1.0));
                previous = next;
            }
            double permissibleClimb = meters * Math.Tan(parameters.MaxSlopeDegrees * Math.PI / 180.0);
            return meters <= 0.0 || Math.Abs(endHeight - startHeight) > permissibleClimb;
        }

        static bool TryIntersectArcs(Vector3D a0, Vector3D a1, Vector3D b0, Vector3D b1,
            out double s, out double r)
        {
            s = r = 0;
            Vector3D center = a0 + a1 + b0 + b1;
            if (center.Normalize() < 1e-12) return false;
            Vector3D u = Vector3D.CalculatePerpendicularVector(center);
            Vector3D v = Vector3D.Cross(center, u);
            Vector2D p0, p1, q0, q1;
            if (!Gnomonic(a0, center, u, v, out p0) || !Gnomonic(a1, center, u, v, out p1) ||
                !Gnomonic(b0, center, u, v, out q0) || !Gnomonic(b1, center, u, v, out q1))
                return false;
            Vector2D d = p1 - p0;
            Vector2D e = q1 - q0;
            double denominator = d.X * e.Y - d.Y * e.X;
            if (Math.Abs(denominator) < 1e-18) return false;
            Vector2D w = q0 - p0;
            s = (w.X * e.Y - w.Y * e.X) / denominator;
            r = (w.X * d.Y - w.Y * d.X) / denominator;
            // Half-open on the end so a crossing exactly at a shared sample counts once.
            return s >= 0 && s < 1 && r >= 0 && r < 1;
        }

        static bool Gnomonic(Vector3D point, Vector3D center, Vector3D u, Vector3D v,
            out Vector2D projected)
        {
            double z = Vector3D.Dot(point, center);
            projected = z > 1e-9
                ? new Vector2D(Vector3D.Dot(point, u) / z, Vector3D.Dot(point, v) / z)
                : Vector2D.Zero;
            return z > 1e-9;
        }
    }
}
