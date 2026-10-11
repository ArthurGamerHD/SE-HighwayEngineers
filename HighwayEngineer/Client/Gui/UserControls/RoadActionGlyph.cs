using System;
using Acrux.Core;
using Acrux.Rendering;
using VRageMath;

namespace HighwayEngineer.Client.Gui.UserControls
{
    // Normalized stroked paths, not font glyphs. They stay crisp at any UI scale.
    internal sealed class RoadActionGlyph : VisualElement
    {
        public bool Cancel;
        public bool Auto;
        public Func<Color> ColorProvider;

        protected override bool HitTestSelf(Vector2 point) { return false; }

        protected override void DrawSelf(RenderContext context)
        {
            Color color = ColorProvider == null ? Color.White : ColorProvider();
            if (!IsEffectivelyEnabled) color.A = 100;
            if (Auto)
            {
                // A winding route between two end points.
                Stroke(context, new Vector2(.18f, .80f), new Vector2(.52f, .64f), color);
                Stroke(context, new Vector2(.52f, .64f), new Vector2(.40f, .38f), color);
                Stroke(context, new Vector2(.40f, .38f), new Vector2(.82f, .20f), color);
            }
            else if (Cancel)
            {
                Stroke(context, new Vector2(.22f, .22f), new Vector2(.78f, .78f), color);
                Stroke(context, new Vector2(.22f, .78f), new Vector2(.78f, .22f), color);
            }
            else
            {
                Stroke(context, new Vector2(.16f, .52f), new Vector2(.40f, .76f), color);
                Stroke(context, new Vector2(.40f, .76f), new Vector2(.84f, .24f), color);
            }
        }

        void Stroke(RenderContext context, Vector2 from, Vector2 to, Color color)
        {
            from = Bounds.Position + from * Bounds.Size;
            to = Bounds.Position + to * Bounds.Size;
            Vector2 delta = to - from;
            float thickness = Math.Max(1.5f, Math.Min(Bounds.Width, Bounds.Height) * .10f);
            var size = new Vector2(delta.Length(), thickness);
            context.Texture("SquareSimple", new RectangleF((from + to - size) * .5f, size),
                color, rotation: (float)Math.Atan2(delta.Y, delta.X));
        }
    }
}
