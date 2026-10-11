using VRageMath;

namespace HighwayEngineer.Common.Roads
{
    /// <summary>
    /// The Road Logistic Depot prefab (Content/Data/Prefabs/RoadLogisticDepot.sbc) and its
    /// delivery box. Everything is measured from the grid pivot, the centre of block (0,0,0)
    /// on the bay floor; the grid's Forward is block -Z. Once spawned, the pivot is recovered
    /// from the depot's contract terminal, so the box follows the real grid.
    /// </summary>
    public static class RoadLogisticDepot
    {
        public const string PrefabName = "Road Logistic Depot";

        const double Block = 2.5;

        // Delivery box from the pivot: the bay between the four floor-level corner stairs
        // (blocks x -3 and 4, z -4 and 5), i.e. blocks x -2..3 and z -3..4, from the floor
        // (half a block down) to 3.5 blocks up. Grid Forward is -Z, Right is +X.
        const double BoxDown = 0.5 * Block;
        const double BoxUp = 3.5 * Block;
        const double BoxForward = 3.5 * Block;
        const double BoxBack = 4.5 * Block;
        const double BoxLeft = 2.5 * Block;
        const double BoxRight = 3.5 * Block;

        /// <summary>The contract terminal in the grid: block (-4, 1, 2), facing grid Right (+X), Up up.</summary>
        static readonly MatrixD TerminalInGrid = MatrixD.CreateWorld(
            new Vector3D(-4, 1, 2) * Block, Vector3D.Right, Vector3D.Up);

        /// <summary>The pivot sits half a block above the ground the depot stands on.</summary>
        public const double PivotHeight = 0.5 * Block;

        // Grid footprint from the pivot (blocks x -5..6, z -5..6), to the outer block faces.
        public const double FootprintLeft = 5.5 * Block;
        public const double FootprintRight = 6.5 * Block;
        public const double FootprintForward = 5.5 * Block;
        public const double FootprintBack = 6.5 * Block;
        public const double FootprintHeight = 6.0 * Block;

        /// <summary>The grid pivot of a spawned depot, from its contract terminal's world matrix.</summary>
        public static MatrixD PivotFromTerminal(MatrixD terminal)
        {
            return MatrixD.Invert(TerminalInGrid) * terminal;
        }

        /// <summary>The delivery box of a depot whose pivot is at <paramref name="pivot"/>.</summary>
        public static DeliveryZone ZoneFromPivot(MatrixD pivot)
        {
            Vector3D center = pivot.Translation +
                              pivot.Up * ((BoxUp - BoxDown) * 0.5) +
                              pivot.Forward * ((BoxForward - BoxBack) * 0.5) +
                              pivot.Right * ((BoxRight - BoxLeft) * 0.5);
            var halfExtents = new Vector3(
                (float)((BoxLeft + BoxRight) * 0.5),
                (float)((BoxUp + BoxDown) * 0.5),
                (float)((BoxForward + BoxBack) * 0.5));
            return DeliveryZone.Create(center, pivot.Forward, pivot.Up, halfExtents);
        }

        /// <summary>The depot pivot a delivery box was made from (inverse of <see cref="ZoneFromPivot"/>).</summary>
        public static MatrixD PivotFromZone(DeliveryZone zone)
        {
            MatrixD world = zone.WorldMatrix;
            world.Translation = world.Translation -
                                world.Up * ((BoxUp - BoxDown) * 0.5) -
                                world.Forward * ((BoxForward - BoxBack) * 0.5) -
                                world.Right * ((BoxRight - BoxLeft) * 0.5);
            return world;
        }

        /// <summary>The space the depot grid takes up, for overlap checks before spawning it.</summary>
        public static MyOrientedBoundingBoxD Footprint(MatrixD pivot)
        {
            var local = new BoundingBoxD(
                new Vector3D(-FootprintLeft, -PivotHeight, -FootprintForward),
                new Vector3D(FootprintRight, FootprintHeight - PivotHeight, FootprintBack));
            return new MyOrientedBoundingBoxD(local, pivot);
        }
    }
}
