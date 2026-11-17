using System;
using System.Collections.Generic;
using VRageMath;

namespace HighwayEngineer.Common.Roads
{
    public struct RoadProfilePoint
    {
        public Vector3D Direction;
        public Vector3D Left;
        public double Distance;
        public double Terrain;
        public double LeftTerrain;
        public double RightTerrain;
    }

    public struct RoadEarthworkEstimate
    {
        public double CutCubicMeters;
        public double FillCubicMeters;
        public double LengthMeters;
    }

    /// <summary>
    /// Expected road surface and cut/fill volumes. Terrain comes from a height
    /// function (unit direction from the planet centre to height above the radius),
    /// so the planner and the server can each use their own terrain.
    /// </summary>
    public static class RoadEarthwork
    {
        /// <summary>Samples per segment for volume estimates: one per 20 m, bounded.</summary>
        public static int EstimateSteps(RoadCurve curve, double radius)
        {
            return (int)Math.Max(16, Math.Min(1024, Math.Ceiling(curve.Arc * radius / 20.0)));
        }

        /// <summary>
        /// Samples a segment's terrain (centerline and both road edges) and the road
        /// surface height the API is expected to build there. Null when the segment
        /// has no length.
        /// </summary>
        public static RoadProfilePoint[] SampleProfile(RoadCurve curve, double radius,
            Func<Vector3D, double> heightAt, double startHeight, double endHeight,
            RoadBuildParameters parameters, int steps, out double[] profile)
        {
            profile = null;
            // The API cuts/fills until the shoulder meets the old terrain.
            // Sample both sides of the road edge so the profile reacts to
            // cross-slope as well as hills and valleys along the centerline.
            var points = new RoadProfilePoint[steps + 1];
            double edgeAngle = Math.Min(Math.PI * 0.5, parameters.WidthMeters * 0.5 / radius);
            double edgeCos = Math.Cos(edgeAngle);
            double edgeSin = Math.Sin(edgeAngle);
            Vector3D lastDirection = Vector3D.Zero;
            for (int i = 0; i <= steps; i++)
            {
                Vector3D direction = curve.Evaluate(i / (double)steps);
                if (i > 0)
                    points[i].Distance = points[i - 1].Distance + radius *
                        Math.Acos(MathHelper.Clamp(Vector3D.Dot(lastDirection, direction), -1.0, 1.0));
                lastDirection = direction;
                points[i].Direction = direction;
                points[i].Terrain = heightAt(direction);
                Vector3D tangent = curve.TangentAt(i / (double)steps);
                Vector3D side = Vector3D.Cross(direction, tangent);
                if (side.Normalize() < 1e-8)
                {
                    side = Vector3D.Cross(direction, Vector3D.Up);
                    if (side.Normalize() < 1e-8)
                        side = Vector3D.Right;
                }
                Vector3D left = direction * edgeCos + side * edgeSin;
                Vector3D right = direction * edgeCos - side * edgeSin;
                points[i].Left = left;
                points[i].LeftTerrain = heightAt(left);
                points[i].RightTerrain = heightAt(right);
            }

            double total = points[steps].Distance;
            if (total <= 0.0) return null;
            double average = 0.0;
            for (int i = 1; i <= steps; i++)
                average += (points[i - 1].Terrain + points[i].Terrain) * 0.5 *
                    (points[i].Distance - points[i - 1].Distance);
            average /= total;
            double grade = Math.Tan(parameters.MaxSlopeDegrees * Math.PI / 180.0);
            bool smooth = parameters.SegmentDistanceMeters > 0.0;
            profile = new double[steps + 1];
            var gradeSteps = new double[steps + 1];
            for (int i = 1; i <= steps; i++)
                gradeSteps[i] = grade * (points[i].Distance - points[i - 1].Distance) *
                    RoadGeometry.GradeRamp(points[i - 1].Distance, points[i].Distance, total,
                        parameters.SegmentDistanceMeters);
            profile[0] = startHeight;
            profile[steps] = endHeight;
            for (int i = 1; i < steps; i++)
            {
                double distance = points[i].Distance;
                double lo = Math.Max(startHeight - grade * distance,
                    endHeight - grade * (total - distance));
                double hi = Math.Min(startHeight + grade * distance,
                    endHeight + grade * (total - distance));
                // Smoothed averaged roads run at one constant grade between the nodes.
                double preferred = !parameters.AverageHeight ? points[i].Terrain
                    : smooth ? startHeight + (endHeight - startHeight) * distance / total
                    : average;
                profile[i] = Math.Max(lo, Math.Min(hi, preferred));
            }
            // Approximate the API's longitudinal grade projection. The exact
            // cubemap profile is resolved only when the operation is pushed.
            for (int pass = 0; pass < 4; pass++)
            {
                for (int i = 1; i < steps; i++)
                {
                    double limit = gradeSteps[i];
                    profile[i] = Math.Max(profile[i - 1] - limit,
                        Math.Min(profile[i - 1] + limit, profile[i]));
                }
                for (int i = steps - 1; i > 0; i--)
                {
                    double limit = gradeSteps[i + 1];
                    profile[i] = Math.Max(profile[i + 1] - limit,
                        Math.Min(profile[i + 1] + limit, profile[i]));
                }
            }
            return points;
        }

        /// <summary>
        /// Estimated cut and fill for a road. <paramref name="nodeHeights"/> holds the
        /// height of each node above the radius, so curve i runs from node i to i + 1.
        /// </summary>
        public static RoadEarthworkEstimate Estimate(IList<RoadCurve> curves, IList<double> nodeHeights,
            double radius, Func<Vector3D, double> heightAt, RoadBuildParameters parameters)
        {
            var estimate = new RoadEarthworkEstimate();
            for (int segment = 0; segment < curves.Count; segment++)
                Add(ref estimate, EstimateSegment(curves[segment], radius, heightAt,
                    nodeHeights[segment], nodeHeights[segment + 1], parameters,
                    EstimateSteps(curves[segment], radius)));
            return estimate;
        }

        /// <summary>
        /// Cut and fill for one segment, with <paramref name="steps"/> cross-sections.
        /// Each cross-section is the road surface against terrain varying linearly from
        /// the centerline to each edge, plus the shoulder wedges when sides are cut/filled.
        /// </summary>
        public static RoadEarthworkEstimate EstimateSegment(RoadCurve curve, double radius,
            Func<Vector3D, double> heightAt, double startHeight, double endHeight,
            RoadBuildParameters parameters, int steps)
        {
            var estimate = new RoadEarthworkEstimate();
            double shoulderSlope = parameters.ShoulderAngleDegrees > 0.0
                ? Math.Tan(parameters.ShoulderAngleDegrees * Math.PI / 180.0) : 0.0;
            double half = parameters.WidthMeters * 0.5;
            double[] profile;
            RoadProfilePoint[] points = SampleProfile(curve, radius, heightAt,
                startHeight, endHeight, parameters, steps, out profile);
            if (points == null) return estimate;
            double previousCut = 0.0, previousFill = 0.0;
            for (int i = 0; i <= steps; i++)
            {
                double cut = 0.0, fill = 0.0;
                // Positive difference: the road sits above the terrain (fill).
                double center = profile[i] - points[i].Terrain;
                AddHalfSection(center, profile[i] - points[i].LeftTerrain, half, shoulderSlope,
                    ref cut, ref fill);
                AddHalfSection(center, profile[i] - points[i].RightTerrain, half, shoulderSlope,
                    ref cut, ref fill);
                if (i > 0)
                {
                    double step = points[i].Distance - points[i - 1].Distance;
                    estimate.CutCubicMeters += (cut + previousCut) * 0.5 * step;
                    estimate.FillCubicMeters += (fill + previousFill) * 0.5 * step;
                }
                previousCut = cut;
                previousFill = fill;
            }
            estimate.LengthMeters = points[steps].Distance;
            return estimate;
        }

        public static void Add(ref RoadEarthworkEstimate total, RoadEarthworkEstimate segment)
        {
            total.CutCubicMeters += segment.CutCubicMeters;
            total.FillCubicMeters += segment.FillCubicMeters;
            total.LengthMeters += segment.LengthMeters;
        }

        /// <summary>Cut/fill area of one half of a cross-section, plus its shoulder wedge.</summary>
        static void AddHalfSection(double center, double edge, double halfWidth,
            double shoulderSlope, ref double cut, ref double fill)
        {
            double a = Math.Abs(center), b = Math.Abs(edge);
            if (center >= 0.0 == edge >= 0.0)
            {
                double area = halfWidth * (a + b) * 0.5;
                if (center >= 0.0) fill += area; else cut += area;
            }
            else if (a + b > 0.0)
            {
                // The road surface crosses the terrain inside this half.
                double toCenter = halfWidth * a * a / (2.0 * (a + b));
                double toEdge = halfWidth * b * b / (2.0 * (a + b));
                if (center >= 0.0) { fill += toCenter; cut += toEdge; }
                else { cut += toCenter; fill += toEdge; }
            }
            if (shoulderSlope > 0.0)
            {
                double wedge = b * b / (2.0 * shoulderSlope);
                if (edge >= 0.0) fill += wedge; else cut += wedge;
            }
        }
    }
}
