using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using VRage.Game;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace HighwayEngineer.Client.Gui
{
    /// <summary>
    /// Lets the planner draw game textures (e.g. faction logos) by path everywhere. The screen
    /// renderer takes the path as is; LCDs need an LCD texture definition named after it, which
    /// is added at runtime (as the LCD mod's TextureHelper does).
    /// </summary>
    internal static class GameIconSprites
    {
        static readonly HashSet<string> Registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void Ensure(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || Registered.Contains(path) || MyDefinitionManager.Static == null)
                return;
            if (!IsDefined(path))
            {
                MyDefinitionManager.Static.Definitions.AddOrReplaceDefinition(new MyLCDTextureDefinition
                {
                    Id = new MyDefinitionId((MyObjectBuilderType)typeof(MyObjectBuilder_LCDTextureDefinition), path),
                    Public = false,
                    LocalizationId = path,
                    SpritePath = path,
                    TexturePath = path,
                    Selectable = false
                });
            }
            Registered.Add(path);
        }

        /// <summary>For LcdTextureResolver.IsTextureDefined: textures defined after a surface was created.</summary>
        public static bool IsDefined(string name)
        {
            return MyDefinitionManager.Static != null &&
                   MyDefinitionManager.Static.GetDefinition<MyLCDTextureDefinition>(MyStringHash.GetOrCompute(name)) != null;
        }

        public static void Clear()
        {
            Registered.Clear();
        }
    }
}
