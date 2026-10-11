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
    public sealed class TextureView : VisualElement
    {
        const int DoubleClickFrames = 21;
        const float DragThresholdSquared = 16f;
        const float DoubleClickDistanceSquared = 64f;

        Vector2 _pressPoint;
        Vector2 _lastClickPoint;
        int _lastClickTick;
        bool _dragging;

        public IMyGeneratedTexture Texture { get; set; }
        public IMyGeneratedTexture PreviousTexture { get; set; }
        public float TransitionBlend { get; set; } = 1f;
        public Action<Vector2> Dragged { get; set; }
        public Action<Vector2> DragStarted { get; set; }
        public Action<Vector2, bool> DragEnded { get; set; }
        public Action<Vector2> MiddleDragged { get; set; }
        /// <summary>Receives original press position, current cursor and delta.</summary>
        public Action<Vector2, Vector2, Vector2> DraggedAt { get; set; }
        public Action<Vector2> Clicked { get; set; }
        public Action<Vector2> DoubleClicked { get; set; }
        public Action<Vector2> RightClicked { get; set; }
        public Action<int> Scrolled { get; set; }
        public bool DrawBackground { get; set; } = true;
        public bool DrawBorder { get; set; } = true;
        /// <summary>Last pointer position over the map, pressed or not.</summary>
        public Vector2 HoverPosition { get; private set; }

        protected override bool HitTestSelf(Vector2 point)
        {
            return true;
        }

        protected override void DrawSelf(RenderContext context)
        {
            if (DrawBackground)
                context.Fill(Bounds, new Color(3, 8, 13, 255));

            float blend = MathHelper.Clamp(TransitionBlend, 0f, 1f);
            if (PreviousTexture != null && PreviousTexture.IsValid && blend < 1f)
            {
                byte alpha = (byte)Math.Round((1f - blend) * 255f);
                context.Texture(PreviousTexture, Bounds, new Color(255, 255, 255, alpha));
            }
            if (Texture != null && Texture.IsValid)
            {
                byte alpha = PreviousTexture != null && PreviousTexture.IsValid
                    ? (byte)Math.Round(blend * 255f)
                    : (byte)255;
                context.Texture(Texture, Bounds, new Color(255, 255, 255, alpha));
            }
            if (DrawBorder)
            {
                context.Border(Bounds, IsPointerOver
                    ? MinimapTheme.Accent
                    : MinimapTheme.OutlineVariant, IsPointerOver ? 2 : 1);
            }
        }

        internal override void PointerPressed(Vector2 point)
        {
            _pressPoint = point;
            _dragging = false;
            if (DragStarted != null) DragStarted(point);
        }

        internal override void PointerHovered(Vector2 point)
        {
            // Hover is reported every input frame; PointerMoved only while pressed.
            HoverPosition = point;
        }

        internal override void PointerMoved(Vector2 point, Vector2 delta)
        {
            HoverPosition = point;
            if (!IsPressed)
                return;

            if (!_dragging && Vector2.DistanceSquared(_pressPoint, point) >= DragThresholdSquared)
                _dragging = true;
            if (_dragging && delta != Vector2.Zero)
            {
                if (DraggedAt != null) DraggedAt(_pressPoint, point, delta);
                else if (Dragged != null) Dragged(delta);
            }
        }

        internal override void PointerReleased(Vector2 point, bool clicked)
        {
            if (clicked && !_dragging)
            {
                if (Clicked != null)
                    Clicked(point);

                if (DoubleClicked != null)
                {
                    int now = MyAPIGateway.Session.GameplayFrameCounter;
                    int elapsed = unchecked(now - _lastClickTick);
                    if (_lastClickTick != 0 && elapsed >= 0 && elapsed <= DoubleClickFrames &&
                        Vector2.DistanceSquared(_lastClickPoint, point) <= DoubleClickDistanceSquared)
                    {
                        _lastClickTick = 0;
                        DoubleClicked(point);
                        return;
                    }

                    _lastClickTick = now;
                    _lastClickPoint = point;
                }
            }
            if (DragEnded != null) DragEnded(point, _dragging);
            _dragging = false;
        }

        internal override void MiddlePointerMoved(Vector2 point, Vector2 delta)
        {
            if (MiddleDragged != null && delta != Vector2.Zero)
                MiddleDragged(delta);
        }

        internal override void SecondaryPointerReleased(Vector2 point, bool clicked)
        {
            if (clicked && RightClicked != null)
                RightClicked(point);
        }

        internal override bool PointerScrolled(Vector2 point, int delta)
        {
            if (Scrolled == null)
                return false;
            Scrolled(delta);
            return true;
        }
    }
}
