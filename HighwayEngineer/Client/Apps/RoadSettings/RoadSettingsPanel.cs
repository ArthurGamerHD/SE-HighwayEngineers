using System;
using System.Collections.Generic;
using Acrux.Controls;
using Acrux.Core;
using Acrux.Layout;
using Acrux.Styling;
using HighwayEngineer.Client.Gui;
using HighwayEngineer.Common.Helpers;
using Sandbox.Definitions;
using VRageMath;

namespace HighwayEngineer.Client.Apps.RoadSettings
{
    /// <summary>
    /// The road parameters flyout behind the settings button; its state lives in
    /// <see cref="RoadSettingsViewModel"/>. Only the material ComboBox&lt;T&gt; (not
    /// creatable from XAML) is built and synchronised here.
    /// </summary>
    internal sealed partial class RoadSettingsPanel : UserControl
    {
        readonly RoadSettingsViewModel _viewModel;
        readonly ComboBox _material;

        public RoadSettingsPanel(RoadSettingsViewModel viewModel)
        {
            _viewModel = viewModel;
            DataContext = viewModel;
            InitializeComponent();

            List<string> materials = GetRoadMaterialOptions();
            _material = _materialSlot.Add(new ComboBox(
                materials, null, item => _viewModel.Material = (string)item)
            {
                OpenDirection = ComboBoxOpenDirection.Down,
                MaxDropDownHeight = 320f,
                PlaceholderText = LocHelper.GetLoc("HighwayEngineer_Settings_MaterialPlaceholder"),
                ItemTemplate = new DataTemplate<string>(BuildMaterialItem)
            });
            string saved = materials.Find(material => string.Equals(material,
                viewModel.Material, StringComparison.OrdinalIgnoreCase));
            _material.SelectedItem = saved ?? materials[0];
            // A saved material another session's mods provided may be missing here.
            _viewModel.Material = (string)_material.SelectedItem;
            _viewModel.PropertyChanged += (sender, args) =>
            {
                if (args == nameof(RoadSettingsViewModel.Material))
                    _material.SelectedItem = _viewModel.Material;
            };
        }

        public void ApplyScale(float width, float height, float scale)
        {
            _viewModel.ApplyScale(width, height, scale);
            _material.Width = Math.Max(1f, width * 0.54f - 16f * scale);
            _material.Height = Math.Max(1f, height / 9f - 1f);
            _material.MaxDropDownHeight = 320f * scale;
        }

        /// <summary>[thumbnail] Material name, for the list and the selection box.</summary>
        VisualElement BuildMaterialItem(string material)
        {
            float rowHeight = _material != null ? _material.OptionHeight : 24f;
            float size = Math.Max(12f, rowHeight * 0.75f);
            var row = new StackPanel
            {
                Orientation = StackOrientation.Horizontal,
                Spacing = size * 0.35f,
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Add(VoxelMaterialThumbnails.CreateView(material, size));
            row.Add(new TextBlock
            {
                Text = VoxelMaterialNames.Get(material),
                FontSize = _material != null ? _material.TextScale : 0.68f,
                Foreground = _material != null ? _material.Foreground : Color.White,
                VerticalAlignment = VerticalAlignment.Center
            });
            return row;
        }

        internal static List<string> GetRoadMaterialOptions()
        {
            var materials = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (MyDefinitionManager.Static != null)
                foreach (var definition in MyDefinitionManager.Static.GetVoxelMaterialDefinitions())
                {
                    string subtype = ConcreteMod.IsOffered(definition) ? definition.Id.SubtypeName : null;
                    if (!string.IsNullOrWhiteSpace(subtype) && seen.Add(subtype))
                        materials.Add(subtype);
                }
            // In the order the list shows them, by display name.
            materials.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(
                VoxelMaterialNames.Get(a), VoxelMaterialNames.Get(b)));
            if (materials.Count == 0)
                materials.Add("Soil");
            return materials;
        }
    }
}
