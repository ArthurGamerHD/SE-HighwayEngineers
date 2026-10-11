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

namespace HighwayEngineer.Client.Gui.UserControls
{
    /// <summary>
    /// A tool icon to the top-right of the cursor, like Street View's drag
    /// ghost. Purely visual: it never takes input.
    /// </summary>
    public sealed class CursorToolBadge : VisualElement
    {
        public Func<string> IconProvider { get; set; }
        public Func<Vector2?> PointerProvider { get; set; }
        public Color Color { get; set; } = Color.White;

        string _drawnIcon;
        Vector2? _drawnPointer;

        protected override bool HitTestSelf(Vector2 point)
        {
            return false;
        }

        /// <summary>
        /// Call every frame. The retained tree only redraws invalidated content,
        /// so invalidate when the badge would look different (moved, shown, hidden).
        /// </summary>
        public void Refresh()
        {
            string icon = IconProvider == null ? null : IconProvider();
            Vector2? pointer = string.IsNullOrEmpty(icon) || PointerProvider == null
                ? null : PointerProvider();
            if (icon == _drawnIcon && pointer == _drawnPointer)
                return;
            _drawnIcon = icon;
            _drawnPointer = pointer;
            Invalidate(UiInvalidation.Render);
        }

        protected override void DrawSelf(RenderContext context)
        {
            string icon = _drawnIcon;
            Vector2? pointer = _drawnPointer;
            if (string.IsNullOrEmpty(icon) || !pointer.HasValue)
                return;

            float scale = Math.Max(0.75f, Math.Min(Bounds.Width, Bounds.Height) / 700f);
            float size = 26f * scale;
            Vector2 center = pointer.Value + new Vector2(size * 0.75f, -size * 0.75f);
            var bounds = new RectangleF(center - new Vector2(size * 0.5f), new Vector2(size));
            context.Texture(icon, new RectangleF(
                bounds.Position + new Vector2(Math.Max(1f, scale)), bounds.Size),
                new Color(0, 0, 0, 190), true);
            context.Texture(icon, bounds, Color, true);
        }
    }
}
