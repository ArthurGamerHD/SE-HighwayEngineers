using System;
using VRageMath;

namespace HighwayEngineer.Common.Structures
{
    /// <summary>Signed distance primitives (negative inside) shared by the bridge shapes.</summary>
    public static class BridgeSdf
    {
        public static double Box(double x, double y, double z, double hx, double hy, double hz)
        {
            double qx = Math.Abs(x) - hx, qy = Math.Abs(y) - hy, qz = Math.Abs(z) - hz;
            double ox = Math.Max(qx, 0.0), oy = Math.Max(qy, 0.0), oz = Math.Max(qz, 0.0);
            return Math.Sqrt(ox * ox + oy * oy + oz * oz) + Math.Min(Math.Max(qx, Math.Max(qy, qz)), 0.0);
        }

        public static double Rectangle(double x, double y, double hx, double hy)
        {
            double qx = Math.Abs(x) - hx, qy = Math.Abs(y) - hy;
            double ox = Math.Max(qx, 0.0), oy = Math.Max(qy, 0.0);
            return Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0.0);
        }

        /// <summary>Approximate signed distance to an axis-aligned ellipse (exact on the surface).</summary>
        public static double Ellipse(double x, double y, double rx, double ry)
        {
            double k0 = Math.Sqrt(x * x / (rx * rx) + y * y / (ry * ry));
            double k1 = Math.Sqrt(x * x / (rx * rx * rx * rx) + y * y / (ry * ry * ry * ry));
            if (k1 < 1e-12) return -Math.Min(rx, ry);
            return k0 * (k0 - 1.0) / k1;
        }

        public static double SmoothMin(double a, double b, double k)
        {
            double h = MathHelper.Clamp(0.5 + 0.5 * (b - a) / k, 0.0, 1.0);
            return b + (a - b) * h - k * h * (1.0 - h);
        }
    }
}
