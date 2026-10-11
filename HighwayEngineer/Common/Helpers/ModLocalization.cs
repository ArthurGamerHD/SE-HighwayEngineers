using System.Collections.Generic;
using System.IO;
using Sandbox.ModAPI;
using VRage;

namespace HighwayEngineer.Common.Helpers
{
    /// <summary>
    /// The game reads a mod's MyTexts*.resx only into localization contexts that
    /// <see cref="MyTexts"/> never sees, so <see cref="LocHelper"/> would show the keys.
    /// As the legacy LCD mod does, this loads them into MyTexts for the game's language.
    /// </summary>
    internal static class ModLocalization
    {
        static string _path;
        static MyLanguagesEnum? _loaded;

        public static void Load(string modDataPath)
        {
            _path = Path.Combine(modDataPath, "Localization");
            // Linux-native runs get mixed separators in the mod path.
            if (_path.EndsWith("/Localization"))
                _path = _path.Replace("\\", "/");
            _loaded = null;
            Reload();
        }

        /// <summary>Loads the texts again when the game's language changed since the last load.</summary>
        public static void Reload()
        {
            if (_path == null)
                return;
            var supported = new HashSet<MyLanguagesEnum>();
            MyTexts.LoadSupportedLanguages(_path, supported);
            MyLanguagesEnum language = MyAPIGateway.Session != null && MyAPIGateway.Session.Config != null &&
                                       supported.Contains(MyAPIGateway.Session.Config.Language)
                ? MyAPIGateway.Session.Config.Language
                : MyLanguagesEnum.English;
            if (_loaded == language)
                return;
            MyTexts.MyLanguageDescription description;
            if (!MyTexts.Languages.TryGetValue(language, out description))
                return;
            _loaded = language;
            // Loads MyTexts.resx, then the culture's file and the culture-region file over it.
            MyTexts.LoadTexts(_path,
                string.IsNullOrWhiteSpace(description.CultureName) ? null : description.CultureName,
                string.IsNullOrWhiteSpace(description.SubcultureName) ? null : description.SubcultureName);
        }

        public static void Unload()
        {
            _path = null;
            _loaded = null;
        }
    }
}
