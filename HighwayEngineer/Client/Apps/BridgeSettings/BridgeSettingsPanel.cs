using System;
using System.Collections.Generic;
using Acrux.Controls;
using Acrux.Core;
using Acrux.Layout;
using Acrux.Styling;
using HighwayEngineer.Client.Apps.RoadSettings;
using HighwayEngineer.Client.Gui;
using HighwayEngineer.Common.Helpers;
using HighwayEngineer.Common.Roads;
using VRageMath;

namespace HighwayEngineer.Client.Apps.BridgeSettings
{
    /// <summary>
    /// The bridge parameters, shown under the road settings behind the settings button; their
    /// state lives in <see cref="BridgeSettingsViewModel"/>. Only the ComboBox&lt;T&gt;s (not
    /// creatable from XAML) are built and synchronised here.
    /// </summary>
    internal sealed partial class BridgeSettingsPanel : UserControl
    {
        const int Rows = 4;

        readonly BridgeSettingsViewModel _viewModel;
        readonly ComboBox _structure;
        readonly ComboBox _pavement;
        readonly ComboBox _design;

        public BridgeSettingsPanel(BridgeSettingsViewModel viewModel)
        {
            _viewModel = viewModel;
            DataContext = viewModel;
            InitializeComponent();

            List<string> materials = RoadSettingsPanel.GetRoadMaterialOptions();
            _structure = _structureSlot.Add(MaterialBox(materials, material => _viewModel.StructureMaterial = material));
            _pavement = _pavementSlot.Add(MaterialBox(materials, material => _viewModel.PavementMaterial = material));
            // A saved material another session's mods provided may be missing here.
            _structure.SelectedItem = Find(materials, viewModel.StructureMaterial);
            _viewModel.StructureMaterial = (string)_structure.SelectedItem;
            _pavement.SelectedItem = Find(materials, viewModel.PavementMaterial);
            _viewModel.PavementMaterial = (string)_pavement.SelectedItem;

            _design = _designSlot.Add(new ComboBox(
                new[] { BridgeDesign.Auto, BridgeDesign.Beam, BridgeDesign.Arch },
                item => BridgeSettingsViewModel.DesignName((BridgeDesign)item), item => _viewModel.Design = (BridgeDesign)item)
            {
                OpenDirection = ComboBoxOpenDirection.Down
            });
            _design.SelectedItem = viewModel.Design;

            _viewModel.PropertyChanged += (sender, args) =>
            {
                if (args == nameof(BridgeSettingsViewModel.StructureMaterial))
                    _structure.SelectedItem = _viewModel.StructureMaterial;
                else if (args == nameof(BridgeSettingsViewModel.PavementMaterial))
                    _pavement.SelectedItem = _viewModel.PavementMaterial;
                else if (args == nameof(BridgeSettingsViewModel.Design))
                    _design.SelectedItem = _viewModel.Design;
            };
        }

        public void ApplyScale(float width, float height, float scale)
        {
            _viewModel.ApplyScale(width, height, scale);
            float boxWidth = Math.Max(1f, width * 0.54f - 16f * scale);
            foreach (ComboBox box in new ComboBox[] { _structure, _pavement, _design })
            {
                box.Width = boxWidth;
                box.Height = height / Rows;
            }
            _structure.MaxDropDownHeight = _pavement.MaxDropDownHeight = 320f * scale;
        }

        internal static ComboBox MaterialBox(List<string> materials, Action<string> selected)
        {
            ComboBox box = null;
            box = new ComboBox(materials, null, item => selected((string)item))
            {
                OpenDirection = ComboBoxOpenDirection.Down,
                MaxDropDownHeight = 320f,
                PlaceholderText = LocHelper.GetLoc("HighwayEngineer_Settings_MaterialPlaceholder"),
                ItemTemplate = new DataTemplate<string>(material => BuildMaterialItem(box, material))
            };
            return box;
        }

        static string Find(List<string> materials, string material)
        {
            return materials.Find(candidate => string.Equals(candidate, material, StringComparison.OrdinalIgnoreCase)) ??
                   materials[0];
        }

        /// <summary>[thumbnail] Material name, for the list and the selection box.</summary>
        static VisualElement BuildMaterialItem(ComboBox box, string material)
        {
            float rowHeight = box != null ? box.OptionHeight : 24f;
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
                FontSize = box != null ? box.TextScale : 0.68f,
                Foreground = box != null ? box.Foreground : Color.White,
                VerticalAlignment = VerticalAlignment.Center
            });
            return row;
        }
    }
}
