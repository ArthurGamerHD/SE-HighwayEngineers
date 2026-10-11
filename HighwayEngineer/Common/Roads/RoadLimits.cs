namespace HighwayEngineer.Common.Roads
{
    /// <summary>
    /// The one place for the road settings' allowed ranges: the planner controls bind to
    /// these, the saved settings are clamped to them and the server rejects roads outside them.
    /// </summary>
    public static class RoadLimits
    {
        public const double MinWidthMeters = 30.0;
        public const double MaxWidthMeters = 500.0;
        public const double MinDepthMeters = 2.5;
        public const double MaxDepthMeters = 15.0;
        public const double MinSlopeDegrees = 0.0;
        public const double MaxSlopeDegrees = 45.0;
        public const double MinShoulderAngleDegrees = 0.0;
        public const double MaxShoulderAngleDegrees = 80.0;
        public const double MinSegmentDistanceMeters = 20.0;
        public const double MaxSegmentDistanceMeters = 500.0;
        /// <summary>
        /// Highest a beam bridge's deck may be above the terrain (its piers stand on it; an arch only
        /// needs its springs on the ground), and the most a node may be lifted.
        /// </summary>
        public const double MaxBridgeHeightMeters = 500.0;
        /// <summary>How far a bridge's deck may dip into the terrain (where it meets it at its ends).</summary>
        public const double BridgeTerrainToleranceMeters = 2.0;

        // Auto-road weights, as the auto-road sliders show them (earthwork per 1000 m³).
        public const double MinAutoRouteWeight = 0.0;
        public const double MaxAutoRouteDistance = 5.0;
        public const double MaxAutoRouteInclination = 20.0;
        public const double MaxAutoRouteEarthwork = 20.0;
    }
}
