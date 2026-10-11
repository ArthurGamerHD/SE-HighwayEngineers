using System;
using System.Globalization;
using System.Text;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace HighwayEngineer.Client.Lcd
{
    /// <summary>
    /// Planner settings the text surface itself has no property for (UI scale), kept
    /// per surface in the block's mod storage. Font size and padding are the surface's
    /// own FontSize and TextPadding.
    /// </summary>
    internal static class PlannerSettings
    {
        public const float MIN_SCALE = 0.5f;
        public const float MAX_SCALE = 3f;
        static readonly Guid StorageKey = new Guid("8f3b6c2e-4a71-4d0e-9b52-6e1f0a7c3d94");

        public static float GetScale(IMyCubeBlock block, int surfaceIndex)
        {
            string stored;
            if (block == null || block.Storage == null || !block.Storage.TryGetValue(StorageKey, out stored))
                return 1f;
            foreach (string entry in stored.Split(';'))
            {
                int separator = entry.IndexOf('=');
                int index;
                float scale;
                if (separator > 0 &&
                    int.TryParse(entry.Substring(0, separator), NumberStyles.Integer, CultureInfo.InvariantCulture, out index) &&
                    index == surfaceIndex &&
                    float.TryParse(entry.Substring(separator + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out scale))
                    return Clamp(scale);
            }
            return 1f;
        }

        public static void SetScale(IMyCubeBlock block, int surfaceIndex, float scale)
        {
            if (block == null)
                return;
            if (block.Storage == null)
                block.Storage = new MyModStorageComponent();
            string stored;
            block.Storage.TryGetValue(StorageKey, out stored);
            var builder = new StringBuilder();
            if (!string.IsNullOrEmpty(stored))
                foreach (string entry in stored.Split(';'))
                    if (entry.Length > 0 && !entry.StartsWith(surfaceIndex.ToString(CultureInfo.InvariantCulture) + "=",
                            StringComparison.Ordinal))
                        builder.Append(entry).Append(';');
            builder.Append(surfaceIndex.ToString(CultureInfo.InvariantCulture)).Append('=')
                .Append(Clamp(scale).ToString("0.###", CultureInfo.InvariantCulture));
            block.Storage[StorageKey] = builder.ToString();
        }

        static float Clamp(float scale)
        {
            return float.IsNaN(scale) ? 1f : Math.Max(MIN_SCALE, Math.Min(MAX_SCALE, scale));
        }

        /// <summary>The surface's index on its block (text panels have one surface).</summary>
        public static int GetSurfaceIndex(IMyCubeBlock block, Sandbox.ModAPI.Ingame.IMyTextSurface surface)
        {
            var provider = block as Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider;
            if (provider == null || surface == null || block is IMyTextPanel)
                return 0;
            for (int i = 0; i < provider.SurfaceCount; i++)
                if (provider.GetSurface(i).Name == surface.Name)
                    return i;
            return 0;
        }
    }
}
