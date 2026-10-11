using Acrux.Cartography.Maps;
using Acrux.Cartography;
using System;
using System.Collections.Generic;
using HighwayEngineer.Client.Maps;
using Sandbox.ModAPI;
using VRageMath;

namespace HighwayEngineer.Client.Infrastructure
{
    public sealed class SavedRoadSegment
    {
        public long PlanetId;
        public double CenterX;
        public double CenterY;
        public double CenterZ;
        public double[] Geometry;
        // Zero for segments saved before widths were recorded.
        public double WidthMeters;
    }

    public sealed class RoadLibrary
    {
        const string SessionVariable = "HighwayEngineer.RoadLibrary.Xml.v1";
        public List<SavedRoadSegment> Segments = new List<SavedRoadSegment>();

        public static RoadLibrary Load()
        {
            // Never fall back to the old LocalStorage file: it is shared across
            // worlds and may contain roads from a later version of this save.
            try
            {
                return ParseXml(ReadSessionXml());
            }
            catch (Exception error)
            {
                ErrorHandlerHelper.LogError(error, typeof(RoadLibrary));
            }
            return new RoadLibrary();
        }

        public static string ReadSessionXml()
        {
            if (MyAPIGateway.Utilities == null) return null;
            string xml;
            return MyAPIGateway.Utilities.GetVariable(SessionVariable, out xml) ? xml : null;
        }

        public static RoadLibrary ParseXml(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml)) return new RoadLibrary();
            RoadLibrary library = MyAPIGateway.Utilities.SerializeFromXML<RoadLibrary>(xml);
            if (library == null) return new RoadLibrary();
            if (library.Segments == null)
                library.Segments = new List<SavedRoadSegment>();
            return library;
        }

        public void Merge(RoadLibrary loaded)
        {
            if (loaded == null || loaded.Segments == null) return;
            bool hadNewSegments = Segments.Count != 0;
            bool merged = false;
            foreach (SavedRoadSegment segment in loaded.Segments)
            {
                if (segment == null || segment.Geometry == null || segment.Geometry.Length != 12)
                    continue;
                bool duplicate = false;
                foreach (SavedRoadSegment existing in Segments)
                {
                    if (existing == null || existing.Geometry == null ||
                        existing.Geometry.Length != 12 || existing.PlanetId != segment.PlanetId ||
                        existing.CenterX != segment.CenterX || existing.CenterY != segment.CenterY ||
                        existing.CenterZ != segment.CenterZ) continue;
                    duplicate = true;
                    for (int i = 0; i < 12; i++)
                        if (existing.Geometry[i] != segment.Geometry[i])
                        { duplicate = false; break; }
                    if (duplicate) break;
                }
                if (!duplicate)
                {
                    Segments.Add(segment);
                    merged = true;
                }
            }
            // A push may have happened while the saved snapshot was parsing.
            if (hadNewSegments && merged) Save();
        }

        public void Add(PlanetSnapshot planet, IEnumerable<double[]> geometry, double widthMeters)
        {
            foreach (double[] values in geometry)
            {
                if (values == null || values.Length != 12) continue;
                bool duplicate = false;
                foreach (SavedRoadSegment existing in GetSegments(planet))
                {
                    duplicate = true;
                    for (int i = 0; i < 12; i++)
                        if (Math.Abs(existing.Geometry[i] - values[i]) > 1e-12)
                        { duplicate = false; break; }
                    if (duplicate) break;
                }
                if (duplicate) continue;
                Segments.Add(new SavedRoadSegment
                {
                    PlanetId = planet.EntityId,
                    CenterX = planet.Position.X,
                    CenterY = planet.Position.Y,
                    CenterZ = planet.Position.Z,
                    Geometry = (double[])values.Clone(),
                    WidthMeters = widthMeters
                });
            }
            Save();
        }

        /// <summary>Removes this planet's segments matching <paramref name="match"/>.</summary>
        public void RemoveSegments(PlanetSnapshot planet, Func<SavedRoadSegment, bool> match)
        {
            var onPlanet = new HashSet<SavedRoadSegment>(GetSegments(planet));
            if (Segments.RemoveAll(segment => onPlanet.Contains(segment) && match(segment)) > 0)
                Save();
        }

        void Save()
        {
            if (MyAPIGateway.Utilities == null) return;
            try
            {
                MyAPIGateway.Utilities.SetVariable(SessionVariable,
                    MyAPIGateway.Utilities.SerializeToXML(this));
            }
            catch (Exception error)
            {
                ErrorHandlerHelper.LogError(error, typeof(RoadLibrary));
            }
        }

        public IEnumerable<SavedRoadSegment> GetSegments(PlanetSnapshot planet)
        {
            if (planet == null) yield break;
            foreach (SavedRoadSegment segment in Segments)
            {
                if (segment == null || segment.PlanetId != planet.EntityId ||
                    segment.Geometry == null || segment.Geometry.Length != 12) continue;
                Vector3D center = new Vector3D(segment.CenterX, segment.CenterY,
                    segment.CenterZ);
                if (Vector3D.DistanceSquared(center, planet.Position) <= 1.0)
                    yield return segment;
            }
        }
    }
}
