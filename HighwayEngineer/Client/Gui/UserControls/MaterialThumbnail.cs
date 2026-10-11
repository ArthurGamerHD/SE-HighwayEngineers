using HighwayEngineer.Client.Gui;
using System;
using System.Collections.Generic;
using System.IO;
using Acrux.Controls;
using Acrux.Core;
using Acrux.Layout;
using Acrux.Rendering;
using Acrux.ScreenSpace;
using Acrux.Styling;
using Adk.Image;
using Adk.Image.Dds;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace HighwayEngineer.Client.Gui.UserControls
{
    /// <summary>Draws a generated texture by handle, which both the screen and LCD renderers resolve.</summary>
    internal sealed class GeneratedTextureImage : VisualElement
    {
        public IMyGeneratedTexture Texture { get; set; }

        protected override void DrawSelf(RenderContext context)
        {
            if (Texture != null && Texture.IsValid)
                context.Texture(Texture, Bounds, Color.White);
        }
    }

    /// <summary>
    /// A voxel material's rounded thumbnail inside a 2 px rounded frame. Bind
    /// <see cref="Material"/> and <see cref="Size"/>; without a thumbnail only the frame shows.
    /// </summary>
    internal sealed class MaterialThumbnail : Panel
    {
        const float FrameThickness = 2f;

        readonly GeneratedTextureImage _image = new GeneratedTextureImage();
        readonly Border _frame;
        string _material;
        float _size = 22f;

        public MaterialThumbnail()
        {
            VerticalAlignment = VerticalAlignment.Center;
            Add(_image);
            _frame = Add(new Border
            {
                StyleClass = "material-thumb-frame",
                BorderThickness = new Vector4(FrameThickness),
                UseGeneratedTexture = true
            });
            ApplySize();
        }

        /// <summary>Voxel material subtype whose thumbnail is shown.</summary>
        public string Material
        {
            get { return _material; }
            set
            {
                if (string.Equals(_material, value, StringComparison.OrdinalIgnoreCase)) return;
                _material = value;
                _image.Texture = VoxelMaterialThumbnails.GetTexture(value);
                Invalidate(Acrux.Core.Invalidation.UiInvalidation.Render);
            }
        }

        /// <summary>Side length in pixels.</summary>
        public float Size
        {
            get { return _size; }
            set
            {
                if (Math.Abs(_size - value) < 0.001f) return;
                _size = value;
                ApplySize();
            }
        }

        void ApplySize()
        {
            Width = Height = _size;
            _frame.CornerRadius = new Vector4(_size * VoxelMaterialThumbnails.CornerFraction);
        }
    }
}
