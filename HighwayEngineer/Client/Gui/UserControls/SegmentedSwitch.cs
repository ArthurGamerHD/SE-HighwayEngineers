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
    public sealed class SegmentedSwitch : VisualElement
    {
        const int SelectionTransitionFrames = 12;
        const float InnerPadding = 2f;
        const float CornerRadius = 5f;

        readonly Border _backgroundBorder;
        readonly Grid _grid;
        readonly Border _animationBorder;
        readonly Border _leftBorder;
        readonly Border _rightBorder;
        readonly TextBlock _leftTextBlock;
        readonly TextBlock _rightTextBlock;

        int _targetIndex;
        int _animationStartFrame;
        float _animationStartPosition;
        bool _animationInitialized;

        public SegmentedSwitch()
        {
            _backgroundBorder = Add(new Border
            {
                Padding = new Vector4(InnerPadding, InnerPadding, InnerPadding, InnerPadding),
                CornerRadius = new Vector4(CornerRadius),
                UseGeneratedTexture = true,
                BackgroundColorProvider = () => MinimapTheme.SurfaceHigh,
                ShadowColor = MinimapTheme.Shadow,
                ShadowOffset = new Vector2(2f)
            });

            _grid = new Grid(2, 1);
            _backgroundBorder.Child = _grid;

            // This border is visual-only and exists only while the thumb is moving.
            // It is added first so segment text and pointer states render above it.
            _animationBorder = _grid.Set(new Border
            {
                CornerRadius = new Vector4(CornerRadius),
                UseGeneratedTexture = true,
                BackgroundColorProvider = () => MinimapTheme.AccentContainer,
                Interactive = false,
                Visible = false
            }, 0, 0, 2, 1);

            _leftBorder = CreateSegmentBorder(0);
            _rightBorder = CreateSegmentBorder(1);
            _grid.Set(_leftBorder, 0, 0);
            _grid.Set(_rightBorder, 1, 0);

            _leftTextBlock = new TextBlock
            {
                FontSize = 0.68f,
                TextAlignment = HorizontalAlignment.Center,
                VerticalTextAlignment = VerticalAlignment.Center
            };
            _rightTextBlock = new TextBlock
            {
                FontSize = 0.68f,
                TextAlignment = HorizontalAlignment.Center,
                VerticalTextAlignment = VerticalAlignment.Center
            };
            _leftBorder.Child = _leftTextBlock;
            _rightBorder.Child = _rightTextBlock;
        }

        public string LeftText { get; set; }
        public string RightText { get; set; }
        public int SelectedIndex { get; set; }
        public Func<int> SelectedIndexProvider { get; set; }
        public Action<int> SelectionChanged { get; set; }

        protected override void ArrangeChildren(RectangleF bounds)
        {
            _backgroundBorder.Arrange(bounds);
            ArrangeAnimationBorder(GetSelectedIndex());
        }

        protected override void DrawSelf(RenderContext context)
        {
            int frame = GetCurrentFrame();
            float selectionPosition = GetAnimatedSelectionPosition(frame);
            bool animating = IsAnimating(frame);
            int selectedIndex = GetSelectedIndex();

            _animationBorder.Visible = animating;
            if (animating)
                ArrangeAnimationBorder(selectionPosition);

            Color hover = MinimapTheme.AccentHover;
            hover.A = 120;
            ConfigureSegmentBorder(_leftBorder, selectedIndex == 0, animating, hover);
            ConfigureSegmentBorder(_rightBorder, selectedIndex == 1, animating, hover);

            _leftTextBlock.Text = LeftText;
            _rightTextBlock.Text = RightText;
            _leftTextBlock.Foreground = LerpColor(
                Color.White,
                MinimapTheme.OnAccent,
                1f - selectionPosition);
            _rightTextBlock.Foreground = LerpColor(
                Color.White,
                MinimapTheme.OnAccent,
                selectionPosition);
        }

        Border CreateSegmentBorder(int index)
        {
            var border = new Border
            {
                CornerRadius = new Vector4(CornerRadius),
                UseGeneratedTexture = true,
                Interactive = true,
                BackgroundColor = Color.Transparent
            };
            border.Clicked = () => SelectSegment(index);
            return border;
        }

        static void ConfigureSegmentBorder(
            Border border,
            bool selected,
            bool animating,
            Color hover)
        {
            Color normal = selected && !animating
                ? MinimapTheme.AccentContainer
                : Color.Transparent;
            if (border.IsPressed)
                border.BackgroundColor = MinimapTheme.SecondaryContainer;
            else if (border.IsPointerOver && !selected)
                border.BackgroundColor = hover;
            else
                border.BackgroundColor = normal;
        }

        void SelectSegment(int index)
        {
            if (index < 0 || index > 1)
                return;
            SelectedIndex = index;
            if (SelectionChanged != null)
                SelectionChanged(index);
        }

        void ArrangeAnimationBorder(float position)
        {
            RectangleF left = _leftBorder.Bounds;
            RectangleF right = _rightBorder.Bounds;
            if (left.Width <= 0f || right.Width <= 0f)
                return;

            position = MathHelper.Clamp(position, 0f, 1f);
            _animationBorder.Arrange(new RectangleF(
                MathHelper.Lerp(left.X, right.X, position),
                MathHelper.Lerp(left.Y, right.Y, position),
                MathHelper.Lerp(left.Width, right.Width, position),
                MathHelper.Lerp(left.Height, right.Height, position)));
        }

        static Color LerpColor(Color from, Color to, float amount)
        {
            amount = MathHelper.Clamp(amount, 0f, 1f);
            return new Color(
                (byte)Math.Round(MathHelper.Lerp(from.R, to.R, amount)),
                (byte)Math.Round(MathHelper.Lerp(from.G, to.G, amount)),
                (byte)Math.Round(MathHelper.Lerp(from.B, to.B, amount)),
                (byte)Math.Round(MathHelper.Lerp(from.A, to.A, amount)));
        }

        float GetAnimatedSelectionPosition(int frame)
        {
            int selectedIndex = GetSelectedIndex();
            if (!_animationInitialized)
            {
                _animationInitialized = true;
                _targetIndex = selectedIndex;
                _animationStartPosition = selectedIndex;
                _animationStartFrame = frame;
                return selectedIndex;
            }

            float current = EvaluateSelectionPosition(frame);
            if (selectedIndex != _targetIndex)
            {
                _animationStartPosition = current;
                _targetIndex = selectedIndex;
                _animationStartFrame = frame;
            }
            return EvaluateSelectionPosition(frame);
        }

        bool IsAnimating(int frame)
        {
            if (!_animationInitialized ||
                Math.Abs(_animationStartPosition - _targetIndex) < 0.001f)
            {
                return false;
            }
            int elapsed = Math.Max(0, unchecked(frame - _animationStartFrame));
            return elapsed < SelectionTransitionFrames;
        }

        float EvaluateSelectionPosition(int frame)
        {
            int elapsed = Math.Max(0, unchecked(frame - _animationStartFrame));
            float amount = MathHelper.Clamp(
                elapsed / (float)SelectionTransitionFrames,
                0f,
                1f);
            float eased = amount * amount * (3f - 2f * amount);
            return MathHelper.Lerp(_animationStartPosition, _targetIndex, eased);
        }

        int GetSelectedIndex()
        {
            int index = SelectedIndexProvider == null
                ? SelectedIndex
                : SelectedIndexProvider();
            return index <= 0 ? 0 : 1;
        }

        static int GetCurrentFrame()
        {
            return MyAPIGateway.Session == null
                ? 0
                : MyAPIGateway.Session.GameplayFrameCounter;
        }
    }
}
