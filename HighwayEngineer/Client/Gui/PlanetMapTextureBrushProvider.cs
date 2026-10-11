using System;
using Acrux.Rendering;
using Acrux.ScreenSpace;
using Acrux.Styling;
using VRage.Game.ModAPI;
using VRageMath;

namespace HighwayEngineer.Client.Gui
{
    /// <summary>
    /// Owns the live generated planet texture and exposes it as one stable brush.
    /// Any retained control with a BackgroundBrush property can consume the brush.
    /// </summary>
    public sealed class PlanetMapTextureBrushProvider : IDisposable
    {
        readonly DynamicMapTexture _texture;
        readonly ImageBrush _brush;

        public PlanetMapTextureBrushProvider(string textureName, Vector2I size)
        {
            _texture = new DynamicMapTexture(textureName, size);
            if (_texture.IsAvailable)
            {
                _brush = new ImageBrush(_texture.Texture.Name);
            }
        }

        public ImageBrush Brush => _brush;
        public IMyGeneratedTexture Texture => _texture.Texture;
        public bool IsAvailable => _texture.IsAvailable;

        public bool Update(Action<PixelCanvas> paint)
        {
            return _texture.Update(paint);
        }

        public void Dispose()
        {
            _texture.Dispose();
        }
    }
}
