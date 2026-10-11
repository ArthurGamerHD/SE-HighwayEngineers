using HighwayEngineer.Client.Apps.Views;
using System;
using System.Collections.Generic;
using System.IO;
using Acrux.Core;
using Acrux.Styling;
using HighwayEngineer.Client.Infrastructure;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace HighwayEngineer.Client.Gui.Styling
{
    /// <summary>
    /// The planner's control styles. The Acrux UI mod is shared by every mod that draws UI, so these style only
    /// HighwayEngineer's own roots (<see cref="Attach"/>), never the toolkit-wide StyleSheet.Application.
    /// </summary>
    public static class MinimapStyles
    {
        // Under a HighwayEngineer folder: every client's style loader is chained in the toolkit, so a path another
        // mod also ships (e.g. Styles/Fluent/FluentStyle.xaml) would be served by whichever mod registered first.
        const string SPACE_ENGINEERS_STYLE_SOURCE = "Data/Gui/Styles/HighwayEngineer/SpaceEngineers/SpaceEngineersStyle.xaml";
        const string FLUENT_STYLE_SOURCE = "Data/Gui/Styles/HighwayEngineer/Fluent/FluentStyle.xaml";
        static MinimapStyleTheme _activeTheme = MinimapStyleTheme.Fluent;
        static MyObjectBuilder_Checkpoint.ModItem _modItem;
        static bool _registered;
        static StyleSheet _current;
        static readonly List<Control> Roots = new List<Control>();

        public static MinimapStyleTheme ActiveTheme => _activeTheme;

        public static bool IsSpaceEngineersStyle =>
            _activeTheme == MinimapStyleTheme.SpaceEngineers;

        /// <summary>Loads the styles on first use (the planner screen or an LCD planner); later calls only switch the theme.</summary>
        public static void RegisterScreen(
            MyObjectBuilder_Checkpoint.ModItem modItem,
            MinimapStyleTheme theme)
        {
            _modItem = modItem;
            if (_registered)
            {
                SetTheme(theme);
                return;
            }
            _activeTheme = NormalizeTheme(theme);
            StyleSheet.SourceLoader = path =>
            {
                using (TextReader reader =
                       MyAPIGateway.Utilities.ReadFileInModLocation(path.TrimStart('/'), _modItem))
                {
                    return reader.ReadToEnd();
                }
            };
            _registered = true;
            Apply(Load(_activeTheme));
        }

        /// <summary>Styles <paramref name="root"/> (and its subtree) with the active theme, now and on theme changes.</summary>
        public static void Attach(VisualElement root)
        {
            var control = root as Control;
            if (control == null)
                return;
            if (!Roots.Contains(control))
                Roots.Add(control);
            control.Styles = _current ?? StyleSheet.Empty;
        }

        public static void Detach(VisualElement root)
        {
            var control = root as Control;
            if (control != null)
                Roots.Remove(control);
        }

        public static void SetTheme(MinimapStyleTheme theme)
        {
            MinimapStyleTheme next = NormalizeTheme(theme);
            if (_activeTheme == next)
                return;

            _activeTheme = next;
            if (_registered)
                Apply(Load(_activeTheme));
        }

        /// <summary>At session end: forgets the roots and the style loader.</summary>
        public static void Unload()
        {
            if (_registered)
                StyleSheet.SourceLoader = null;
            _registered = false;
            Roots.Clear();
            _current = null;
            _modItem = default(MyObjectBuilder_Checkpoint.ModItem);
        }

        static void Apply(StyleSheet sheet)
        {
            _current = sheet ?? StyleSheet.Empty;
            foreach (Control root in Roots)
                root.Styles = _current;
        }

        static MinimapStyleTheme NormalizeTheme(MinimapStyleTheme theme)
        {
            return theme == MinimapStyleTheme.SpaceEngineers
                ? MinimapStyleTheme.SpaceEngineers
                : MinimapStyleTheme.Fluent;
        }

        // Every theme color, read when a {DynamicResource} resolves (the palette follows the faction and the theme).
        // The design-time resources (DesignThemeResources.debug.cs) serve the same table to the IDE preview.
        internal static readonly KeyValuePair<string, Func<Color>>[] ColorResources =
        {
            new KeyValuePair<string, Func<Color>>("surfaceColor", () => MinimapTheme.Surface),
            new KeyValuePair<string, Func<Color>>("surfaceContainerHighColor", () => MinimapTheme.SurfaceHigh),
            new KeyValuePair<string, Func<Color>>("surfaceContainerHighestColor", () => MinimapTheme.SurfaceHighest),
            new KeyValuePair<string, Func<Color>>("surfaceContainerLowColor", () => MinimapTheme.SurfaceLow),
            new KeyValuePair<string, Func<Color>>("onSurfaceColor", () => Color.White),
            new KeyValuePair<string, Func<Color>>("onSurfaceVariantColor", () => MinimapTheme.OnSurfaceVariant),
            new KeyValuePair<string, Func<Color>>("fontColor", () => Color.White),
            new KeyValuePair<string, Func<Color>>("controlBackgroundColor", () => new Color(MinimapTheme.Surface, 0.75f)),
            new KeyValuePair<string, Func<Color>>("popupBackgroundColor", () => new Color(MinimapTheme.Surface, 0.9f)),
            new KeyValuePair<string, Func<Color>>("borderColor", () => MinimapTheme.Outline),
            new KeyValuePair<string, Func<Color>>("borderVariantColor", () => MinimapTheme.OutlineVariant),
            new KeyValuePair<string, Func<Color>>("errorColor", () => MinimapTheme.Error),
            new KeyValuePair<string, Func<Color>>("disabledColor", () => MinimapTheme.Disabled),
            new KeyValuePair<string, Func<Color>>("shadowColor", () => MinimapTheme.Shadow),
            new KeyValuePair<string, Func<Color>>("accentColor", () => MinimapTheme.Accent),
            new KeyValuePair<string, Func<Color>>("accentContainerColor", () => MinimapTheme.AccentContainer),
            new KeyValuePair<string, Func<Color>>("secondaryContainerColor", () => MinimapTheme.SecondaryContainer),
            new KeyValuePair<string, Func<Color>>("onAccentColor", () => MinimapTheme.OnAccent),
            new KeyValuePair<string, Func<Color>>("scrollBarTrackColor", () => new Color(MinimapTheme.OutlineVariant, 127)),
            new KeyValuePair<string, Func<Color>>("scrollBarThumbColor", () => MinimapTheme.SurfaceHighest),
            new KeyValuePair<string, Func<Color>>("scrollBarThumbHoverColor", () => MinimapTheme.AccentHover),
            new KeyValuePair<string, Func<Color>>("scrollBarThumbPressedColor", () => MinimapTheme.AccentContainer),
            new KeyValuePair<string, Func<Color>>("menuSeparatorColor", () => { Color color = MinimapTheme.OnAccent; color.A = 72; return color; })
        };

        internal const string TEXT_FONT = "White";

        /// <summary>The value converters the views use as {StaticResource}; also served to the IDE preview.</summary>
        internal static KeyValuePair<string, object>[] CreateConverters()
        {
            return new[]
            {
                new KeyValuePair<string, object>("iconMask", new IconMaskConverter()),
                new KeyValuePair<string, object>("iconImage", new IconImageConverter()),
                new KeyValuePair<string, object>("gameDateTime", new GameDateTimeConverter()),
                new KeyValuePair<string, object>("iconRotation", new RadiansRotationConverter())
            };
        }

        static StyleSheet Load(MinimapStyleTheme theme)
        {
            bool spaceEngineers = theme == MinimapStyleTheme.SpaceEngineers;
            MinimapTheme.UseSpaceEngineersPalette(spaceEngineers);

            var resources = new ResourceDictionary();
            foreach (KeyValuePair<string, Func<Color>> entry in ColorResources)
                resources.Add(entry.Key, entry.Value);
            resources.Add("textFont", TEXT_FONT);
            foreach (KeyValuePair<string, object> entry in CreateConverters())
                resources.Add(entry.Key, entry.Value);

            try
            {
                // The toolkit resolves includes through every client's SourceLoader, then its own content
                // (/Data/Gui/Styles/Acrux/...).
                return StyleSheet.LoadFromSource(
                    spaceEngineers ? SPACE_ENGINEERS_STYLE_SOURCE : FLUENT_STYLE_SOURCE,
                    resources);
            }
            catch (Exception exception)
            {
                MyLog.Default.WriteLineAndConsole(
                    "[HighwayEngineer] Could not parse control styles: " + exception.Message);
                return StyleSheet.Empty;
            }
        }
    }
}
