using System;
using Acrux.Cartography;
using Acrux.Core.Input;
using Adk.Mvvm;
using HighwayEngineer.Client.Gui.UserControls;
using VRageMath;

namespace HighwayEngineer.Client.Apps.RoadPlanner
{
    /// <summary>
    /// One map layer button, as on the Minimap: the selected layer is always shown, the others only while the
    /// selector is open. Its size follows the planner's scale (<see cref="SetSize"/>).
    /// </summary>
    internal sealed class MapLayerItem : ObservableObject
    {
        bool _isSelected;
        bool _isShown;
        PlanetColorCubemap _cubemap;
        PlanetCubeFace _face;
        float _size = 44f;
        Vector4 _margin = new Vector4(0f, 0f, 8f, 0f);

        public MapLayerItem(CartographyLayer layer, string name, Action<CartographyLayer> select)
        {
            Layer = layer;
            Name = name;
            Command = new DelegateCommand(_ => select(layer));
        }

        [PreviewWith("Satellite", "Terrain", "Materials", "Biomes")]
        public CartographyLayer Layer { get; }
        [PreviewWith("Satellite", "Terrain", "Materials", "Biomes")]
        public string Name { get; }
        public ICommand Command { get; }
        [PreviewWith(true, false, false, false)]
        public bool IsSelected => _isSelected;
        [PreviewWith(true, false, false, false)]
        public bool IsShown => _isShown;
        public PlanetColorCubemap Cubemap => _cubemap;
        [PreviewWith("Front")]
        public PlanetCubeFace Face => _face;
        [PreviewWith(44f)]
        public float Size => _size;
        /// <summary>The gap after the button (the items panel has no spacing of its own).</summary>
        [PreviewWith("0 0 8 0")]
        public Vector4 Margin => _margin;

        internal void Update(bool selected, bool shown, PlanetColorCubemap cubemap, PlanetCubeFace face)
        {
            RaiseAndSetIfChanged(ref _isSelected, selected, nameof(IsSelected));
            RaiseAndSetIfChanged(ref _isShown, shown, nameof(IsShown));
            if (!LayerPreview.IsSameCubemap(_cubemap, cubemap))
            {
                _cubemap = cubemap;
                RaisePropertyChanged<PlanetColorCubemap>(nameof(Cubemap));
            }
            RaiseAndSetIfChanged(ref _face, face, nameof(Face));
        }

        internal void SetSize(float size, float gap)
        {
            RaiseAndSetIfChanged(ref _size, size, nameof(Size));
            RaiseAndSetIfChanged(ref _margin, new Vector4(0f, 0f, gap, 0f), nameof(Margin));
        }
    }
}
