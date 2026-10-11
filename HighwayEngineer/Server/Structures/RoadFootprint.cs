using System;
using System.Collections.Generic;
using HighwayEngineer.Common.Roads;
using Sandbox.Game.Entities;
using VRageMath;

namespace HighwayEngineer.Server.Structures
{
    /// <summary>
    /// The planet storage blocks under and over built roads, for a repair to drop the voxel edits
    /// in (<see cref="VoxelRepairQueue"/>): holes dug into the road and its edges, and dirt or
    /// rock piled on it.
    /// </summary>
    public static class RoadFootprint
    {
        /// <summary>Voxels per block side; small enough to stay close to the road, large enough to be few.</summary>
        public const int BlockSize = 8;
        // Sampled this often along and across the road, and up and down: under a block's size,
        // so every block the footprint passes through gets a sample.
        const double StepMeters = 4.0;
        // Beyond each edge of the road, and below and above its surface.
        const double MarginMeters = 2.0;
        const double DepthMeters = 12.0;
        const double HeightMeters = 8.0;

        /// <summary>
        /// The blocks (x, y, z each) under <paramref name="curves"/>, each as wide as its
        /// <see cref="RoadCurve.WidthMeters"/>. Reads the planet's terrain only: any thread.
        /// </summary>
        public static int[] Blocks(MyPlanet planet, IList<RoadCurve> curves)
        {
            var blocks = new HashSet<Vector3I>();
            Vector3D center = planet.PositionComp.GetPosition();
            double radius = planet.AverageRadius;
            double top = Math.Max(planet.AverageRadius, planet.MaximumRadius);
            foreach (RoadCurve curve in curves)
            {
                double half = Math.Max(curve.WidthMeters, 1.0) * 0.5 + MarginMeters;
                int along = Math.Max(1, (int)Math.Ceiling(curve.Arc * radius / StepMeters));
                int across = Math.Max(1, (int)Math.Ceiling(half * 2.0 / StepMeters));
                for (int i = 0; i <= along; i++)
                {
                    double t = i / (double)along;
                    Vector3D direction = curve.Evaluate(t);
                    Vector3D right = Vector3D.Normalize(Vector3D.Cross(curve.TangentAt(t), direction));
                    for (int j = 0; j <= across; j++)
                    {
                        double offset = -half + 2.0 * half * j / across;
                        Vector3D at = Vector3D.Normalize(direction + right * (offset / radius));
                        Vector3D surface = planet.GetClosestSurfacePointGlobal(center + at * top);
                        Vector3D up = Vector3D.Normalize(surface - center);
                        for (double h = -DepthMeters; h <= HeightMeters + 1e-6; h += StepMeters)
                            blocks.Add(VoxelRepairQueue.BlockAt(planet, surface + up * h, BlockSize));
                    }
                }
            }
            var packed = new int[blocks.Count * 3];
            int k = 0;
            foreach (Vector3I block in blocks)
            {
                packed[k++] = block.X;
                packed[k++] = block.Y;
                packed[k++] = block.Z;
            }
            return packed;
        }
    }
}
