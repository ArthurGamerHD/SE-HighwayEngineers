using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace HighwayEngineer.Server.Structures
{
    /// <summary>
    /// Settles spawned structures on the terrain: every cell of the grid's bottom layer that hangs
    /// above the ground gets a column of armor blocks under it, down to the terrain.
    /// </summary>
    public static class StructureService
    {
        const string SupportSubtype = "LargeBlockArmorBlock";
        // A bottom face closer to the terrain than this already stands on it.
        const double GroundToleranceMeters = 0.25;
        // Longest support column, so a site at a cliff edge does not grow a tower.
        const int MaxSupportBlocks = 32;

        /// <summary>Adds the supports under <paramref name="grid"/> (game thread, right after it spawned).</summary>
        public static int AddGroundSupports(IMyCubeGrid grid, MyPlanet planet, long builtBy)
        {
            if (grid == null || grid.MarkedForClose || planet == null || planet.MarkedForClose) return 0;
            int bottom = grid.Min.Y;
            var blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks, block => block.Min.Y == bottom);
            int added = 0;
            foreach (IMySlimBlock block in blocks)
                for (int x = block.Min.X; x <= block.Max.X; x++)
                for (int z = block.Min.Z; z <= block.Max.Z; z++)
                    added += AddColumn(grid, planet, block, new Vector3I(x, bottom, z), builtBy);
            return added;
        }

        static int AddColumn(IMyCubeGrid grid, MyPlanet planet, IMySlimBlock above, Vector3I cell, long builtBy)
        {
            Vector3D up = grid.WorldMatrix.Up;
            double size = grid.GridSize;
            Vector3D face = grid.GridIntegerToWorld(cell) - up * (size * 0.5);
            double gap = Vector3D.Dot(face - SurfaceUnder(planet, face), up);
            if (gap <= GroundToleranceMeters) return 0;
            int count = Math.Min(MaxSupportBlocks, (int)Math.Ceiling((gap - GroundToleranceMeters) / size));
            int added = 0;
            for (int i = 1; i <= count; i++)
            {
                Vector3I position = cell - new Vector3I(0, i, 0);
                if (grid.CubeExists(position)) break;
                var builder = (MyObjectBuilder_CubeBlock)MyObjectBuilderSerializer.CreateNewObject(
                    new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), SupportSubtype));
                builder.Min = position;
                builder.BlockOrientation = new SerializableBlockOrientation(
                    Base6Directions.Direction.Forward, Base6Directions.Direction.Up);
                builder.ColorMaskHSV = above.ColorMaskHSV;
                builder.SkinSubtypeId = above.SkinSubtypeId.String;
                builder.BuiltBy = builtBy;
                if (grid.AddBlock(builder, false) == null) break;
                added++;
            }
            return added;
        }

        /// <summary>The terrain straight below (towards the planet centre) a world point.</summary>
        internal static Vector3D SurfaceUnder(MyPlanet planet, Vector3D point)
        {
            Vector3D center = planet.PositionComp.GetPosition();
            Vector3D direction = Vector3D.Normalize(point - center);
            double top = Math.Max(planet.AverageRadius, planet.MaximumRadius);
            return planet.GetClosestSurfacePointGlobal(center + direction * top);
        }
    }
}
