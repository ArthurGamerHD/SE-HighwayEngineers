using System.Collections.Generic;
using Acrux.Core;
using Acrux.Layout;
using VRageMath;

namespace HighwayEngineer.Client.Gui.UserControls
{
    /// <summary>
    /// Places children at explicit bounds (set from code, e.g. next to a map
    /// marker); unplaced children fill the panel. Re-layouts keep the placement.
    /// </summary>
    public sealed class AnchorPanel : Panel
    {
        readonly Dictionary<VisualElement, RectangleF> _placements =
            new Dictionary<VisualElement, RectangleF>();

        public void Place(VisualElement child, RectangleF bounds)
        {
            RectangleF current;
            if (_placements.TryGetValue(child, out current) && current == bounds && child.Bounds == bounds)
                return;
            _placements[child] = bounds;
            // Applied now as well: it may be drawn before the next layout pass.
            child.Arrange(bounds);
        }

        protected override void ArrangeChildren(RectangleF bounds)
        {
            IReadOnlyList<VisualElement> children = VisualChildren;
            for (int i = 0; i < children.Count; i++)
            {
                VisualElement child = children[i];
                if (!child.Visible)
                    continue;
                RectangleF placement;
                if (_placements.TryGetValue(child, out placement))
                    child.Arrange(placement);
                else
                    child.ArrangeInSlot(bounds);
            }
        }
    }
}
