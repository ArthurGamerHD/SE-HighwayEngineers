using Sandbox.Definitions;
using VRage.Game;

namespace HighwayEngineer.Common.Helpers
{
    /// <summary>Physical item names in this machine's language, from their definitions.</summary>
    public static class ItemNames
    {
        /// <summary>The item's display name, or <paramref name="fallback"/> (resolved, as it may be a message) when unknown.</summary>
        public static string Display(string type, string subtype, string fallback)
        {
            MyDefinitionId id;
            if (!string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(subtype) && MyDefinitionId.TryParse(type, subtype, out id))
            {
                MyPhysicalItemDefinition definition = MyDefinitionManager.Static.GetPhysicalItemDefinition(id);
                if (definition != null) return definition.DisplayNameText;
            }
            return LocHelper.Resolve(fallback ?? subtype);
        }
    }
}
