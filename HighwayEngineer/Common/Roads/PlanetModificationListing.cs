using System.Collections.Generic;

namespace HighwayEngineer.Common.Roads
{
    // Read-only XML shape of the API's GetPlanetModificationsXml (API 0.0.17+), read by the
    // planner (to show roads built some other way) and the server (to bulldoze them). Class
    // names must match the API's element names; XmlSerializer ignores the other operations.
    public sealed class PlanetModificationListing
    {
        public List<PlanetModificationInfo> Modifications;
    }

    public sealed class PlanetModificationInfo
    {
        public string Id;
        public int Index;
        public string CustomData;
        public List<RuntimeProceduralLineOperation> Lines;
    }

    public sealed class RuntimeProceduralLineOperation
    {
        public string Layer;
        /// <summary>The planet material table value the line paints.</summary>
        public int Material;
        public double[] Geometry;
        public double RadiusMeters;
        public double WidthMeters;
        public bool AverageHeight;
        public double FillAngleDegrees;
        public double MaximumSlopeDegrees;
        public double SegmentDistanceMeters;
    }
}
