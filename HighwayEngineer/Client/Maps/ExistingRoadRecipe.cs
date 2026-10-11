using System.Collections.Generic;
using HighwayEngineer.Common.Roads;

namespace HighwayEngineer.Client.Maps
{
    // Read-only XML shape of the API's procedural recipe. XmlSerializer ignores
    // the other operation types, so the editor only imports line geometry.
    public sealed class RuntimeProceduralPlanetRecipe
    {
        public List<RuntimeProceduralRevision> Revisions;
    }

    public sealed class RuntimeProceduralRevision
    {
        public List<RuntimeProceduralLineOperation> Lines;
    }
}
