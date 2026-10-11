using Acrux.Cartography;
using HighwayEngineer.Client.Gui.Styling;
using System;
using Acrux.Core;
using Acrux.Core.Invalidation;
using Acrux.Core.PropertySystem;
using Acrux.Rendering;
using Acrux.ScreenSpace;
using HighwayEngineer.Client.Maps;
using VRageMath;

namespace HighwayEngineer.Client.Gui.UserControls
{
    /// <summary>
    /// The cubemap face under the player, for a layer button (bound to the planner's MapLayerItem). It owns only
    /// the layer's pixel texture, painted by <see cref="UpdatePreviewTexture"/>; selection, pointer input and chrome
    /// belong to the Button around it. Without a cubemap (still loading, or in the IDE preview) it is a plain fill.
    /// </summary>
    public sealed class LayerPreview : VisualElement
    {
        public static readonly StyledProperty<PlanetColorCubemap> CubemapProperty =
            StyledProperty.Register<LayerPreview, PlanetColorCubemap>(nameof(Cubemap), null, UiInvalidation.Render);
        public static readonly StyledProperty<PlanetCubeFace> FaceProperty =
            StyledProperty.Register<LayerPreview, PlanetCubeFace>(nameof(Face), PlanetCubeFace.Front, UiInvalidation.Render);

        DynamicMapTexture _previewTexture;
        PlanetColorCubemap _renderedCubemap;
        PlanetCubeFace _renderedFace;

        public PlanetColorCubemap Cubemap
        {
            get { return GetValue(CubemapProperty); }
            set { SetValue(CubemapProperty, value); }
        }

        public PlanetCubeFace Face
        {
            get { return GetValue(FaceProperty); }
            set { SetValue(FaceProperty, value); }
        }

        protected override void DrawSelf(RenderContext context)
        {
            if (_previewTexture == null || !_previewTexture.IsAvailable || _renderedCubemap == null)
                context.Fill(Bounds, MinimapTheme.SurfaceHighest);
            else
                context.Texture(_previewTexture.Texture, Bounds, Color.White);
        }

        /// <summary>Paints the texture when the cubemap, face or size changed; the view calls it every update.</summary>
        internal void UpdatePreviewTexture()
        {
            PlanetColorCubemap cubemap = Cubemap;
            PlanetCubeFace face = Face;
            if (cubemap == null || Bounds.Width <= 0f || Bounds.Height <= 0f)
            {
                if (_renderedCubemap != null)
                    Invalidate(UiInvalidation.Render);
                _renderedCubemap = null;
                return;
            }

            var requestedSize = new Vector2I(
                Math.Max(1, (int)Math.Round(Bounds.Width)),
                Math.Max(1, (int)Math.Round(Bounds.Height)));
            Vector2I supportedSize = DynamicMapTexture.FitToRenderer(requestedSize);
            if (supportedSize.X <= 0 || supportedSize.Y <= 0)
                return;

            if (_previewTexture == null || _previewTexture.Size != supportedSize)
            {
                if (_previewTexture != null)
                    _previewTexture.Dispose();
                _previewTexture = new DynamicMapTexture(GeneratedTextureNames.Next("MinimapLayerPreview"), requestedSize);
                _renderedCubemap = null;
            }

            if (_previewTexture == null || !_previewTexture.IsAvailable ||
                IsSameCubemap(_renderedCubemap, cubemap) && _renderedFace == face)
            {
                return;
            }

            if (_previewTexture.Update(canvas => PaintPreview(canvas, cubemap, face)))
            {
                GeneratedTextureNames.NotifyChanged(_previewTexture.Texture);
                _renderedCubemap = cubemap;
                _renderedFace = face;
                Invalidate(UiInvalidation.Render);
            }
        }

        // The bridge hands out a new wrapper per read: wrapper identity alone would repaint the preview every frame.
        internal static bool IsSameCubemap(PlanetColorCubemap a, PlanetColorCubemap b)
        {
            if (ReferenceEquals(a, b))
                return true;
            return a != null && b != null && ReferenceEquals(a.Native, b.Native);
        }

        static void PaintPreview(
            PixelCanvas canvas,
            PlanetColorCubemap cubemap,
            PlanetCubeFace face)
        {
            int mip = cubemap.SelectMipLevel(Math.Max(canvas.Width, canvas.Height));
            for (int y = 0; y < canvas.Height; y++)
            {
                float v = (y + 0.5f) / canvas.Height;
                for (int x = 0; x < canvas.Width; x++)
                {
                    float u = (x + 0.5f) / canvas.Width;
                    canvas.SetPixel(x, y, cubemap.SampleFace(face, u, v, mip));
                }
            }
        }

        public override void Dispose()
        {
            if (_previewTexture != null)
                _previewTexture.Dispose();
            _previewTexture = null;
            _renderedCubemap = null;
            base.Dispose();
        }
    }
}
