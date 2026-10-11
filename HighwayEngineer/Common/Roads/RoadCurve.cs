using System;
using VRageMath;

namespace HighwayEngineer.Common.Roads
{
    /// <summary>
    /// One road segment: a spherical Hermite curve between two unit directions from
    /// the planet centre, matching the voxel cubemap API's line rasterizer.
    /// </summary>
    public struct RoadCurve
    {
        public Vector3D Start;
        public Vector3D End;
        public Vector3D StartTangent;
        public Vector3D EndTangent;
        public double Arc;
        // Built road width; zero when unknown (older saved segments).
        public double WidthMeters;
        // API modification and line index of a built road (from the API's
        // modification listing); null when only known from the road library.
        public string ModificationId;
        public int LineIndex;
        // The server road library's segment and bridge ids; 0 when the segment is not in it.
        public long SegmentId;
        public long BridgeId;
        // A library bridge's deck-top height above the planet's average radius at its ends.
        public double StartHeight;
        public double EndHeight;

        public Vector3D Evaluate(double t)
        {
            // Match SphericalLineRasterizer.ApplyOne in the voxel cubemap API.
            double t2 = t * t;
            double t3 = t2 * t;
            double magnitude = Arc * 0.5;
            Vector3D point = Start * (2 * t3 - 3 * t2 + 1) +
                             StartTangent * magnitude * (t3 - 2 * t2 + t) +
                             End * (-2 * t3 + 3 * t2) +
                             EndTangent * magnitude * (t3 - t2);
            point.Normalize();
            return point;
        }

        public Vector3D TangentAt(double t)
        {
            double t2 = t * t;
            double magnitude = Arc * 0.5;
            Vector3D tangent = Start * (6 * t2 - 6 * t) +
                               StartTangent * magnitude * (3 * t2 - 4 * t + 1) +
                               End * (-6 * t2 + 6 * t) +
                               EndTangent * magnitude * (3 * t2 - 2 * t);
            Vector3D normal = Evaluate(t);
            tangent -= normal * Vector3D.Dot(tangent, normal);
            tangent.Normalize();
            return tangent;
        }

        /// <summary>
        /// Reads the API's 12-value planet-local geometry (start, end, start tangent,
        /// end tangent) into world-oriented directions. False for degenerate input.
        /// </summary>
        public static bool TryFromLocalGeometry(MatrixD planetWorld, double[] geometry, out RoadCurve curve)
        {
            curve = new RoadCurve();
            if (geometry == null || geometry.Length != 12) return false;
            for (int i = 0; i < geometry.Length; i++)
                if (double.IsNaN(geometry[i]) || double.IsInfinity(geometry[i])) return false;
            curve.Start = FromLocal(planetWorld, geometry, 0);
            curve.End = FromLocal(planetWorld, geometry, 3);
            curve.StartTangent = FromLocal(planetWorld, geometry, 6);
            curve.EndTangent = FromLocal(planetWorld, geometry, 9);
            if (curve.Start.Normalize() < 1e-8 || curve.End.Normalize() < 1e-8)
                return false;
            curve.Arc = Math.Acos(MathHelper.Clamp(
                Vector3D.Dot(curve.Start, curve.End), -1.0, 1.0));
            if (curve.Arc < 1e-7 || curve.Arc > Math.PI - 1e-5) return false;
            curve.StartTangent -= curve.Start * Vector3D.Dot(curve.StartTangent, curve.Start);
            curve.EndTangent -= curve.End * Vector3D.Dot(curve.EndTangent, curve.End);
            return curve.StartTangent.Normalize() >= 1e-8 &&
                   curve.EndTangent.Normalize() >= 1e-8;
        }

        /// <summary>The API's 12-value planet-local geometry for this curve.</summary>
        public double[] ToLocalGeometry(MatrixD planetWorld)
        {
            var geometry = new double[12];
            ToLocal(planetWorld, Start, geometry, 0);
            ToLocal(planetWorld, End, geometry, 3);
            ToLocal(planetWorld, StartTangent, geometry, 6);
            ToLocal(planetWorld, EndTangent, geometry, 9);
            return geometry;
        }

        static Vector3D FromLocal(MatrixD world, double[] values, int offset)
        {
            return world.Right * values[offset] + world.Up * values[offset + 1] +
                   world.Backward * values[offset + 2];
        }

        static void ToLocal(MatrixD world, Vector3D direction, double[] values, int offset)
        {
            values[offset] = Vector3D.Dot(direction, world.Right);
            values[offset + 1] = Vector3D.Dot(direction, world.Up);
            values[offset + 2] = Vector3D.Dot(direction, world.Backward);
        }
    }
}
