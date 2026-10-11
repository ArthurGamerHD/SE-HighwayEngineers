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
    public struct RoadTangentHandles
    {
        public Vector2 Center;
        public Vector2 Entrance;
        public Vector2 Exit;
        public float Scale;
        public bool HasEntrance;
        public bool HasExit;
    }

    public sealed class RoadTangentOverlay : Acrux.Core.VisualElement
    {
        public Func<RoadTangentHandles?> TangentProvider { get; set; }
        protected override bool HitTestSelf(Vector2 point) { return false; }
        protected override void DrawSelf(Acrux.Rendering.RenderContext context)
        {
            RoadTangentHandles? handles = TangentProvider == null ? null : TangentProvider();
            if (!handles.HasValue) return;
            RoadTangentHandles value = handles.Value;
            if (value.HasEntrance)
                DrawHandle(context, value.Center, value.Entrance,
                    new Color(80, 220, 255), "IN", value.Scale);
            if (value.HasExit)
                DrawHandle(context, value.Center, value.Exit,
                    new Color(126, 236, 126), "OUT", value.Scale);
            context.Texture("CircleHollow", new RectangleF(
                value.Center.X - 6f * value.Scale,
                value.Center.Y - 6f * value.Scale,
                12f * value.Scale, 12f * value.Scale), Color.White);
        }

        static void DrawHandle(RenderContext context, Vector2 from, Vector2 to,
            Color color, string label, float scale)
        {
            int parts = Math.Max(1, (int)Math.Ceiling(Vector2.Distance(from, to) / (3f * scale)));
            for (int i = 0; i <= parts; ++i)
            {
                Vector2 p = Vector2.Lerp(from, to, i / (float)parts);
                context.Fill(new RectangleF(p.X - scale, p.Y - scale,
                    2f * scale, 2f * scale), color);
            }
            context.Texture("Circle", new RectangleF(to.X - 8f * scale,
                to.Y - 8f * scale, 16f * scale, 16f * scale), color);
            context.Text(label, to + new Vector2(10f * scale, 0f), 0.5f * scale, color,
                HorizontalAlignment.Left, VerticalAlignment.Center, false);
        }
    }
}
