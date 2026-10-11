using System;
using Sandbox.Definitions;

namespace HighwayEngineer.Common.Helpers
{
    /// <summary>What players see for a voxel material instead of its subtype.</summary>
    public static class VoxelMaterialNames
    {
        /// <summary>
        /// The material's localized display name from its definition (this mod's materials have one,
        /// <c>DisplayName_HighwayEngineer_*</c>); its subtype when it has none, as most game materials.
        /// </summary>
        public static string Get(string subtype)
        {
            return LocHelper.Resolve(Message(subtype));
        }

        /// <summary>
        /// <see cref="Get"/> for another machine to show in its own language (see <see cref="LocHelper.Message"/>):
        /// the definition's name key as a message, or the plain subtype.
        /// </summary>
        public static string Message(string subtype)
        {
            if (string.IsNullOrWhiteSpace(subtype) || MyDefinitionManager.Static == null) return subtype;
            foreach (var definition in MyDefinitionManager.Static.GetVoxelMaterialDefinitions())
            {
                if (definition == null ||
                    !string.Equals(definition.Id.SubtypeName, subtype, StringComparison.OrdinalIgnoreCase)) continue;
                if (definition.DisplayNameEnum.HasValue)
                    return LocHelper.Message(definition.DisplayNameEnum.Value.String);
                return string.IsNullOrWhiteSpace(definition.DisplayNameString) ? subtype : definition.DisplayNameString;
            }
            return subtype;
        }
    }
}
