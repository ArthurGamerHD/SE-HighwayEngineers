using Acrux.Cartography.Maps;
using Acrux.Cartography;
using HighwayEngineer.Client.Gui.Styling;
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
    /// <summary>
    /// A screen-space popup that owns the input surface while open. The panel itself
    /// is a Border -> StackPanel -> Button retained tree. Item appearance comes from
    /// the context-menu-item XML style class.
    /// </summary>
    public sealed class MapContextMenu : VisualElement
    {
        const float MenuWidth = 184f;
        const float MenuItemHeight = 34f;
        const float EdgePadding = 4f;

        readonly Border _outerBorder;
        readonly StackPanel _itemsPanel;
        readonly Button[] _items;
        Vector2 _anchor;
        RectangleF _panelBounds;

        public MapContextMenu(params string[] items)
        {
            string[] labels = items ?? new string[0];
            _items = new Button[labels.Length];

            _outerBorder = Add(new Border
            {
                CornerRadius = new Vector4(5f),
                UseGeneratedTexture = true,
                BackgroundColorProvider = () => MinimapTheme.AccentContainer,
                ShadowColor = MinimapTheme.Shadow,
                ShadowOffset = new Vector2(3f)
            });
            _itemsPanel = new StackPanel
            {
                Spacing = 0f
            };
            _outerBorder.Child = _itemsPanel;

            for (int i = 0; i < labels.Length; i++)
            {
                int itemIndex = i;
                Button item = new Button
                {
                    Text = labels[i],
                    StyleClass = "context-menu-item",
                    ShowTopSeparator = i > 0,
                    Height = MenuItemHeight
                };
                item.Clicked = () => ActivateItem(itemIndex);
                item.SecondaryClicked = Close;
                _items[i] = _itemsPanel.Add(item);
            }

            Visible = false;
        }

        public Action<int> ItemClicked { get; set; }

        public void SetItemText(int index, string text)
        {
            if (index < 0 || index >= _items.Length)
                return;
            _items[index].Text = text ?? string.Empty;
        }

        public void SetItemVisible(int index, bool visible)
        {
            if (index < 0 || index >= _items.Length)
                return;
            _items[index].Visible = visible;
            UpdateSeparators();
        }

        public void SetItemForeground(int index, Color color)
        {
            if (index < 0 || index >= _items.Length)
                return;
            _items[index].Foreground = color;
        }

        public void Open(Vector2 anchor)
        {
            _anchor = anchor;
            Visible = true;
            PositionPanel();
            _outerBorder.Arrange(_panelBounds);
        }

        public void Close()
        {
            Visible = false;
        }

        protected override void ArrangeChildren(RectangleF bounds)
        {
            PositionPanel();
            if (Visible)
                _outerBorder.Arrange(_panelBounds);
        }

        protected override bool HitTestSelf(Vector2 point)
        {
            return true;
        }

        internal override void PointerReleased(Vector2 point, bool clicked)
        {
            Close();
        }

        internal override void SecondaryPointerReleased(Vector2 point, bool clicked)
        {
            Close();
        }

        void ActivateItem(int index)
        {
            Close();
            if (ItemClicked != null)
                ItemClicked(index);
        }

        void PositionPanel()
        {
            int visibleItems = 0;
            for (int i = 0; i < _items.Length; i++)
            {
                if (_items[i].Visible)
                    visibleItems++;
            }
            float height = visibleItems * MenuItemHeight;
            float minX = Bounds.X + EdgePadding;
            float minY = Bounds.Y + EdgePadding;
            float maxX = Math.Max(minX, Bounds.Right - MenuWidth - EdgePadding);
            float maxY = Math.Max(minY, Bounds.Bottom - height - EdgePadding);
            _panelBounds = new RectangleF(
                MathHelper.Clamp(_anchor.X, minX, maxX),
                MathHelper.Clamp(_anchor.Y, minY, maxY),
                MenuWidth,
                height);
        }

        void UpdateSeparators()
        {
            bool hasVisibleItem = false;
            for (int i = 0; i < _items.Length; i++)
            {
                _items[i].ShowTopSeparator =
                    _items[i].Visible && hasVisibleItem;
                hasVisibleItem |= _items[i].Visible;
            }
        }
    }
}
