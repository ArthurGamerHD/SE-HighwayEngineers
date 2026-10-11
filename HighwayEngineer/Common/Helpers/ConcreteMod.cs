using System;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Utils;

namespace HighwayEngineer.Common.Helpers
{
    /// <summary>
    /// Soft dependency on Digi's Concrete Tool mod (https://steamcommunity.com/sharedfiles/filedetails/?id=396679430).
    /// When the world loads it, bridges are built of its "Concrete" voxel material and their
    /// concrete is paid in its Concrete Mix instead of Space Credits; our own copy of the material
    /// (HighwayEngineer_Concrete) is disabled so only one concrete is offered.
    /// </summary>
    public static class ConcreteMod
    {
        public const ulong WorkshopId = 396679430;
        /// <summary>Our standalone copy of the mod's material (Data/VoxelMaterials_HighwayEngineer.sbc).</summary>
        public const string OwnMaterial = "HighwayEngineer_Concrete";
        /// <summary>The mod's voxel material.</summary>
        public const string ModMaterial = "Concrete";
        /// <summary>The mod's concrete item (Data/Ammo.sbc): 10 Gravel each, one per m³ its tool places.</summary>
        public const string MixType = "MyObjectBuilder_AmmoMagazine";
        public const string MixSubtype = "ConcreteMix";
        public const string MixName = "Concrete Mix";

        /// <summary>The Concrete Tool mod is loaded in this world (and its material exists).</summary>
        public static bool Present { get; private set; }

        /// <summary>The bridge structure's default voxel material.</summary>
        public static string StructureMaterial => Present ? ModMaterial : OwnMaterial;

        /// <summary>
        /// Detects the mod. Call first in LoadData: definitions are loaded by then (voxel materials
        /// already have their render indices, so ours keeps its slot), but nothing has read them yet.
        /// </summary>
        public static void Detect()
        {
            Present = false;
            var mods = MyAPIGateway.Session == null ? null : MyAPIGateway.Session.Mods;
            if (mods != null)
                foreach (MyObjectBuilder_Checkpoint.ModItem mod in mods)
                    if (mod.PublishedFileId == WorkshopId)
                        Present = true;
            if (!Present) return;
            if (MyDefinitionManager.Static.GetVoxelMaterialDefinition(ModMaterial) == null)
            {
                Present = false;
                MyLog.Default.WriteLineAndConsole("[HighwayEngineer] Concrete Tool mod is loaded but has no '" +
                                                  ModMaterial + "' voxel material; keeping " + OwnMaterial + ".");
                return;
            }
            MyVoxelMaterialDefinition own = MyDefinitionManager.Static.GetVoxelMaterialDefinition(OwnMaterial);
            if (own != null)
            {
                own.Enabled = false;
                own.Public = false;
            }
            MyLog.Default.WriteLineAndConsole("[HighwayEngineer] Concrete Tool mod detected: bridges use " +
                                              ModMaterial + " and " + MixName + "; " + OwnMaterial + " disabled.");
        }

        /// <summary>The Concrete Mix item's name in this machine's language (the mod's own text when loaded).</summary>
        public static string MixDisplayName()
        {
            return ItemNames.Display(MixType, MixSubtype, MixName);
        }

        public static void Unload()
        {
            Present = false;
        }

        /// <summary>A voxel material the planner and quotes may offer (ours is disabled beside the mod's).</summary>
        public static bool IsOffered(MyVoxelMaterialDefinition definition)
        {
            return definition != null &&
                   !(Present && string.Equals(definition.Id.SubtypeName, OwnMaterial, StringComparison.OrdinalIgnoreCase));
        }
    }
}
