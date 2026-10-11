using Acrux.Lcd;
using VRage.Game.ModAPI;

namespace HighwayEngineer.Client.Gui
{
    /// <summary>
    /// Generated textures are global by name, and the planner can be open on the
    /// screen and on several LCDs at once, so every texture gets its own name.
    /// </summary>
    internal static class GeneratedTextureNames
    {
        static int _next;

        public static string Next(string prefix)
        {
            return prefix + "_" + ++_next;
        }

        /// <summary>
        /// LCDs only redraw when their sprites change; this marks the texture's
        /// pixels as new so panels showing it upload again.
        /// </summary>
        public static void NotifyChanged(IMyGeneratedTexture texture)
        {
            if (texture != null && texture.IsValid)
                LcdDynamicTexture.NotifyContentChanged(texture.Name);
        }
    }
}
