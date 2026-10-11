using Acrux.Cartography.Maps;
using Acrux.Cartography;
using HighwayEngineer.Client.Gui.Styling;
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
    public sealed class StreetViewControl : VisualElement
    {
        Vector2 _pressPoint;
        Vector2 _pointer;
        bool _dragging;

        public Action<Vector2> DragCompleted { get; set; }

        protected override bool HitTestSelf(Vector2 point)
        {
            return true;
        }

        protected override void DrawSelf(RenderContext context)
        {
            float side = Math.Min(Bounds.Width, Bounds.Height) * 0.75f;
            Color color = Enabled ? MinimapTheme.Accent : MinimapTheme.Disabled;
            context.Texture("HighwayEngineer_Icon_StreetView", new RectangleF(
                Bounds.Center - new Vector2(side * 0.5f),
                new Vector2(side)), color, true);

            if (!_dragging)
                return;

            Color ghost = new Color(color.R, color.G, color.B, 209);
            context.Texture("CircleHollow", new RectangleF(
                _pointer - new Vector2(side / 6f),
                new Vector2(side / 3f)), MinimapTheme.Accent);
            context.Texture("HighwayEngineer_Icon_StreetView", new RectangleF(
                _pointer + new Vector2(side / 3f, -side / 3f) - new Vector2(side * 0.5f),
                new Vector2(side)), ghost, true);
        }

        internal override void PointerPressed(Vector2 point)
        {
            _pressPoint = point;
            _pointer = point;
            _dragging = false;
        }

        internal override void PointerMoved(Vector2 point, Vector2 delta)
        {
            _pointer = point;
            if (!_dragging && Vector2.DistanceSquared(_pressPoint, point) >= 16f)
                _dragging = true;
        }

        internal override void PointerReleased(Vector2 point, bool clicked)
        {
            _pointer = point;
            if (_dragging && DragCompleted != null)
                DragCompleted(point);
            _dragging = false;
        }
    }
}
