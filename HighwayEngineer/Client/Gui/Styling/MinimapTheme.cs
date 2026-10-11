using Acrux.Cartography.Maps;
using Acrux.Cartography;
using System;
using System.Collections.Generic;
using Acrux.Controls;
using Acrux.Core;
using Acrux.Core.Invalidation;
using Acrux.Core.PropertySystem;
using Acrux.Layout;
using Acrux.Rendering;
using Acrux.ScreenSpace;
using Sandbox.ModAPI;
using HighwayEngineer.Client.Maps;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRage;
using VRageMath;

namespace HighwayEngineer.Client.Gui.Styling
{
    public static class MinimapTheme
    {
        public static readonly Color DefaultFactionAccent = new Color(58, 32, 63);
        public static readonly Color Surface = new Color(20, 24, 28, 242);
        public static readonly Color SurfaceLow = new Color(30, 35, 40, 244);
        public static readonly Color SurfaceHigh = new Color(40, 47, 52, 245);
        public static readonly Color SurfaceHighest = new Color(56, 64, 70, 248);
        public static readonly Color Outline = new Color(137, 149, 156);
        public static readonly Color OutlineVariant = new Color(82, 98, 107);
        public static readonly Color OnSurfaceVariant = new Color(196, 204, 209);
        public static readonly Color Disabled = new Color(105, 113, 118);
        public static readonly Color Error = new Color(255, 107, 107);
        static readonly Color FluentShadow = new Color(0, 0, 0, 160);
        static bool _useSpaceEngineersPalette;

        public static Color Shadow => _useSpaceEngineersPalette ? Color.Transparent : FluentShadow;

        static Color _accent = DefaultFactionAccent;
        static Color _accentContainer = DefaultFactionAccent;
        static Color _accentHover = new Color(98, 73, 102);
        static Color _secondaryContainer = new Color(78, 53, 82);
        static Color _onAccent = Color.White;

        static MinimapTheme()
        {
            SetAccent(DefaultFactionAccent);
        }

        public static Color Accent => _accent;
        public static Color AccentContainer => _accentContainer;
        public static Color AccentHover => _accentHover;
        public static Color SecondaryContainer => _secondaryContainer;
        public static Color OnAccent => _onAccent;
        public static Color OnAccentContainer => _onAccent;

        public static Color FactionAccent
        {
            get
            {
                var session = MyAPIGateway.Session;
                var player = session == null ? null : session.LocalHumanPlayer;
                var faction = player == null || session.Factions == null ? null
                    : session.Factions.TryGetPlayerFaction(player.IdentityId);
                if (faction == null) return DefaultFactionAccent;
                Color icon = FromFactionColor(faction.IconColor);
                return IsFullWhiteOrBlack(icon) ? FromFactionColor(faction.CustomColor) : icon;
            }
        }

        public static bool UsesLightTextShade(Color source)
        {
            return RelativeLuminance(source) < 0.42f;
        }

        public static Color DeriveTextShade(Color source)
        {
            Vector3 hsv = source.ColorToHSV();
            if (UsesLightTextShade(source))
            {
                // Preserve the faction hue while lifting dark colors far enough
                // to remain visible as an embossed edge around the source text.
                hsv.Y = MathHelper.Clamp(hsv.Y * 0.82f, 0f, 1f);
                hsv.Z = Math.Max(hsv.Z, 0.92f);
            }
            else
            {
                // Match the LCD sprite-shadow convention for already bright text.
                hsv.Z = Math.Min(hsv.Z, 0.12f);
            }

            Color shade = hsv.HSVtoColor();
            shade.A = (byte)Math.Min((int)source.A, 220);
            return shade;
        }

        public static void UseSpaceEngineersPalette(bool enabled)
        {
            if (_useSpaceEngineersPalette == enabled)
            {
                RefreshFactionAccent();
                return;
            }

            _useSpaceEngineersPalette = enabled;
            // Re-evaluate the accent immediately because DynamicResource providers
            // read this palette after the stylesheet is selected.
            _accent = new Color(0, 0, 0, 0);
            RefreshFactionAccent();
        }

        public static bool RefreshFactionAccent()
        {
            Color accent = _useSpaceEngineersPalette ? Color.White : DefaultFactionAccent;
            if (!_useSpaceEngineersPalette &&
                MyAPIGateway.Session != null &&
                MyAPIGateway.Session.Factions != null &&
                MyAPIGateway.Session.LocalHumanPlayer != null)
            {
                IMyFaction faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(
                    MyAPIGateway.Session.LocalHumanPlayer.IdentityId);
                if (faction != null)
                {
                    Color iconColor = FromFactionColor(faction.IconColor);
                    // Pure white/black icon colors are masks; use the custom faction
                    // color as Fluent's accent in that case, as the archived theme did.
                    accent = IsFullWhiteOrBlack(iconColor)
                        ? FromFactionColor(faction.CustomColor)
                        : iconColor;
                }
            }

            if (_accent.R == accent.R && _accent.G == accent.G && _accent.B == accent.B)
                return false;

            SetAccent(accent);
            return true;
        }

        static Color FromFactionColor(Vector3 hsvOffset)
        {
            return MyColorPickerConstants
                .HSVOffsetToHSV(hsvOffset)
                .HSVtoColor();
        }

        static bool IsFullWhiteOrBlack(Color color)
        {
            bool black = color.R <= 1 && color.G <= 1 && color.B <= 1;
            bool white = color.R >= 254 && color.G >= 254 && color.B >= 254;
            return black || white;
        }

        static void SetAccent(Color accent)
        {
            _accent = new Color(accent.R, accent.G, accent.B);
            _accentContainer = new Color(accent.R, accent.G, accent.B, 248);

            bool dark = RelativeLuminance(accent) < 0.48f;
            Color contrast = dark ? Color.White : new Color(8, 10, 12);
            _onAccent = contrast;
            _accentHover = Mix(accent, contrast, 0.20f, 250);
            _secondaryContainer = Mix(accent, contrast, 0.12f, 250);
        }

        static Color Mix(Color from, Color to, float amount, byte alpha)
        {
            amount = MathHelper.Clamp(amount, 0f, 1f);
            return new Color(
                (byte)Math.Round(MathHelper.Lerp(from.R, to.R, amount)),
                (byte)Math.Round(MathHelper.Lerp(from.G, to.G, amount)),
                (byte)Math.Round(MathHelper.Lerp(from.B, to.B, amount)),
                alpha);
        }

        static float RelativeLuminance(Color color)
        {
            return (0.2126f * color.R + 0.7152f * color.G + 0.0722f * color.B) / 255f;
        }
    }
}
