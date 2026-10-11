using System.Collections.Generic;
using System.Text;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.GUI.TextPanel;
using VRage.Utils;

namespace HighwayEngineer.Client.Lcd
{
    /// <summary>
    /// Terminal controls for a surface running the road planner. The game hides its
    /// own Font size and Text padding sliders in script mode, so (as the LCD mod does)
    /// copies of them are shown under the script list, with a planner Scale slider.
    /// </summary>
    internal static class PlannerTerminalControls
    {
        const string PREFIX = "HighwayEngineer_Planner";

        static readonly Dictionary<string, IMyTerminalControlSlider> Copies =
            new Dictionary<string, IMyTerminalControlSlider>();
        static IMyTerminalControlSlider _scale;
        static bool _registered;

        public static void Register()
        {
            if (_registered || MyAPIGateway.TerminalControls == null)
                return;
            _registered = true;
            _scale = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyTerminalBlock>(
                PREFIX + "Scale");
            _scale.Title = MyStringId.GetOrCompute("HighwayEngineer_Lcd_ScaleTitle");
            _scale.Tooltip = MyStringId.GetOrCompute("HighwayEngineer_Lcd_ScaleTooltip");
            _scale.SetLimits(PlannerSettings.MIN_SCALE, PlannerSettings.MAX_SCALE);
            _scale.Getter = block => PlannerSettings.GetScale(block, SelectedSurfaceIndex(block));
            _scale.Setter = (block, value) => PlannerSettings.SetScale(block, SelectedSurfaceIndex(block), value);
            _scale.Writer = (block, text) => text.Append(PlannerSettings.GetScale(block, SelectedSurfaceIndex(block))
                .ToString("0.00")).Append('x');
            _scale.Visible = RunsPlanner;
            MyAPIGateway.TerminalControls.CustomControlGetter += OnCustomControlGetter;
        }

        public static void Unregister()
        {
            if (!_registered)
                return;
            _registered = false;
            if (MyAPIGateway.TerminalControls != null)
                MyAPIGateway.TerminalControls.CustomControlGetter -= OnCustomControlGetter;
            Copies.Clear();
            _scale = null;
        }

        static void OnCustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if (controls == null || !RunsPlanner(block))
                return;
            var added = new List<IMyTerminalControl>(3);
            for (int i = 0; i < controls.Count; i++)
            {
                var slider = controls[i] as IMyTerminalControlSlider;
                if (slider == null)
                    continue;
                string id = controls[i].Id;
                if (id == "FontSize")
                    added.Add(GetCopy(slider, id, 0.1f, 10f));
                else if (id == "TextPadding" || id == "TextPaddingSlider")
                    added.Add(GetCopy(slider, id, 0f, 50f));
            }
            added.Add(_scale);

            int insertAt = controls.FindIndex(control => control.Id == "Script");
            insertAt = insertAt < 0 ? controls.Count : insertAt + 1;
            foreach (IMyTerminalControl control in added)
                if (!controls.Contains(control))
                    controls.Insert(insertAt++, control);
        }

        /// <summary>A copy of a game slider that is shown in script mode; it edits the same value.</summary>
        static IMyTerminalControlSlider GetCopy(IMyTerminalControlSlider original, string id, float min, float max)
        {
            IMyTerminalControlSlider copy;
            if (Copies.TryGetValue(id, out copy))
                return copy;
            copy = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyTerminalBlock>(
                PREFIX + "_" + id);
            copy.Title = original.Title;
            copy.Tooltip = original.Tooltip;
            copy.Getter = original.Getter;
            copy.Setter = original.Setter;
            copy.Writer = original.Writer;
            copy.SetLimits(min, max);
            copy.Visible = RunsPlanner;
            Copies[id] = copy;
            return copy;
        }

        /// <summary>Whether the surface selected in the terminal shows the road planner.</summary>
        static bool RunsPlanner(IMyTerminalBlock block)
        {
            var provider = block as IMyTextSurfaceProvider;
            if (provider == null || provider.SurfaceCount == 0)
                return false;
            var surface = provider.GetSurface(SelectedSurfaceIndex(block));
            return surface != null && surface.ContentType == ContentType.SCRIPT &&
                   surface.Script == RoadPlannerSurfaceScript.ID;
        }

        static int SelectedSurfaceIndex(IMyTerminalBlock block)
        {
            var multiTextPanel = block == null ? null : block.Components.Get<MyMultiTextPanelComponent>();
            return multiTextPanel == null ? 0 : multiTextPanel.SelectedPanelIndex;
        }
    }
}
