using System;
using System.Collections.Generic;
using Acrux.Addons;
using Acrux.Controls;
using Acrux.Core;
using Acrux.Core.Input;
using Acrux.Core.Invalidation;
using Acrux.Layout;
using Acrux.Rendering;
using Acrux.ScreenSpace;
using HighwayEngineer.Client.Apps.RoadPlanner;
using HighwayEngineer.Client.Gui.Styling;
using HighwayEngineer.Client.Infrastructure;
using HighwayEngineer.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Input;
using VRage.Utils;
using VRageMath;
using GuiHost = Acrux.Gui;

namespace HighwayEngineer.Client.Apps.Settings
{
    /// <summary>Runtime configuration window for the map/minimap UI.</summary>
    public sealed class MinimapSettingsScreen : MyModScreen
    {
        const float FluentWindowWidthPixels = 640f;
        const float FluentWindowMaxHeightPixels = 760f;
        const float FluentWindowPaddingPixels = 20f;
        const float FluentRowGapPixels = 6f;
        // ADK Window's default title bar height.
        const float FluentTitleBarHeightPixels = 32f;

        // Keen's MyGuiScreenOptionsGame uses 0.6535714 x 0.9379771 of the
        // 1600x1200 GUI reference, with controls inset ~90 px from either side.
        const float KeenSettingsWidthPixels = 0.6535714f * 1600f;
        const float KeenSettingsHeightPixels = 0.9379771f * 1200f;
        const float KeenOuterPaddingPixels = 20f;
        const float KeenSideInsetPixels = 90f;
        const float KeenHeaderHeightPixels = 70f;
        const float KeenRowGapPixels = 0f;

        static readonly MinimapStyleTheme[] ThemeOptions =
        {
            MinimapStyleTheme.Fluent,
            MinimapStyleTheme.SpaceEngineers
        };

        readonly RoadPlannerViewModel _viewModel;
        readonly MinimapLocalConfig _config;
        readonly GuiHost _gui = GuiHost.PrepareRender(
            new ScreenSpaceRenderer());
        readonly List<KeySelector> _keySelectors = new List<KeySelector>();
        VisualTree _visualTree;
        ScreenSpaceInputSource _screenInputSource;
        // The tree root is an ADK Window: Fluent shows ADK's own window chrome, while the
        // Space Engineers theme styles it frameless and shows the Keen-style header below.
        Window _root;
        bool _closingScreen;
        Grid _windowContent;
        Panel _header;
        ScrollViewer _scroller;
        Button _closeButton;
        StackPanel _content;
        ComboBox _themeCombo;
        RectangleF _contentBounds;
        bool _themeLayoutPending;

        public bool HasKeyboardFocus =>
            (_visualTree != null && _visualTree.HasKeyboardFocus) || IsInputCaptureActive;

        bool IsInputCaptureActive
        {
            get
            {
                for (int i = 0; i < _keySelectors.Count; i++)
                {
                    if (_keySelectors[i].IsOpen)
                        return true;
                }
                return false;
            }
        }

        internal MinimapSettingsScreen(RoadPlannerViewModel viewModel)
            : base(
                "Minimap Settings",
                new Vector2(0.5f, 0.5f),
                new Vector2(1f, 1f),
                Vector4.Zero)
        {
            _viewModel = viewModel;
            _config = MinimapLocalConfig.Current;
            CloseButtonEnabled = false;
            CloseOnEscape = false;
            EnabledBackgroundFade = false;
            PausesGameInSinglePlayer = false;
        }

        protected override void OnOpened()
        {
            BuildVisualTree();
            LayoutVisualTree();
        }

        protected override void OnLayoutChanged()
        {
            LayoutVisualTree();
        }

        protected override void OnHandleInput()
        {
            var input = MyAPIGateway.Input;
            if (input == null)
                return;

            if (input.IsNewKeyPressed(MyKeys.Escape))
            {
                if (CloseTransientControls())
                    return;
                Close();
                return;
            }

            if (_visualTree != null)
            {
                if (_screenInputSource == null)
                    _screenInputSource = new ScreenSpaceInputSource(input);
                _screenInputSource.AdvanceFrame();
                _visualTree.HandleInput(_screenInputSource);
            }
        }

        protected override void OnDraw(float transitionAlpha)
        {
            ApplyPendingThemeLayout();
            EnsureLayout();
            if (_visualTree != null)
                _visualTree.Draw(transitionAlpha);
        }

        protected override void OnClosed(bool isUnloading)
        {
            _closingScreen = true;
            if (_visualTree != null)
                _visualTree.ResetInput();
            if (_root != null)
            {
                MinimapStyles.Detach(_root);
                _root.Dispose();
            }

            _keySelectors.Clear();
            _visualTree = null;
            _screenInputSource = null;
            _root = null;
            _windowContent = null;
            _header = null;
            _scroller = null;
            _closeButton = null;
            _content = null;
            _themeCombo = null;
        }

        void BuildVisualTree()
        {
            _closingScreen = false;
            _root = new Window
            {
                StyleClass = "settings-window",
                Title = LocHelper.GetLoc("HighwayEngineer_Settings_WindowTitle"),
                CanMaximize = false,
                CanMinimize = false,
                CloseOnEscape = false,
                Platform = HostedWindowPlatform.Instance
            };
            // The caption close button closes the Window; the screen follows it.
            _root.Closed += () =>
            {
                if (!_closingScreen)
                    Close();
            };

            _windowContent = new Grid(1, 2);
            _root.Child = _windowContent;

            _header = new Panel();
            _windowContent.Set(_header, 0, 0);

            _header.Add(new TextBlock
            {
                StyleClass = "settings-header",
                Text = LocHelper.GetLoc("HighwayEngineer_Settings_Header"),
                FontSize = 0.94f,
                ColorProvider = () => MinimapTheme.Accent,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            _header.Add(new FixedSpacer());

            _closeButton = _header.Add(new Button
            {
                StyleClass = "close-button",
                Icon = "HighwayEngineer_Icon_Cross",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Clicked = Close
            });

            _scroller = new ScrollViewer
            {
                StyleClass = "settings-scroll",
                VerticalScrollEnabled = true,
                HorizontalScrollEnabled = false,
                InputEnabled = true,
                WheelStep = 44f
            };
            _windowContent.Set(_scroller, 0, 1);

            _content = new StackPanel();
            _scroller.Child = _content;

            _themeCombo = new ComboBox(
                ThemeOptions,
                item => FormatTheme((MinimapStyleTheme)item),
                item =>
                {
                    var theme = (MinimapStyleTheme)item;
                    _config.Theme = theme;
                    MinimapStyles.SetTheme(theme);
                    SaveAndApply();
                    _themeLayoutPending = true;
                },
                OnThemeComboStateChanged)
            {
                StyleClass = "settings-anchor"
            };
            _themeCombo.SelectedItem = _config.Theme;
            AddRow(LocHelper.GetLoc("HighwayEngineer_Settings_Theme"), _themeCombo);

            AddToggleRow(LocHelper.GetLoc("HighwayEngineer_Settings_ShowGps"), () => _config.FullScreen.DisplayMyGps, value =>
            {
                _config.FullScreen.DisplayMyGps = value;
                SaveAndApply();
            });
            AddSeparator();
            AddHeading(LocHelper.GetLoc("HighwayEngineer_Planner_Title"), false);
            AddToggleRow(LocHelper.GetLoc("HighwayEngineer_Settings_ShowGps"), () => _config.PlanetaryMap.DisplayMyGps, value =>
            {
                _config.PlanetaryMap.DisplayMyGps = value;
                SaveAndApply();
            });
            AddToggleRow(LocHelper.GetLoc("HighwayEngineer_Settings_ShowRadio"), () => _config.PlanetaryMap.IncludeRadioSignals, value =>
            {
                _config.PlanetaryMap.IncludeRadioSignals = value;
                SaveAndApply();
            });

            AddSeparator();
            AddHeading(LocHelper.GetLoc("HighwayEngineer_Settings_RoadsHeading"), false);
            AddToggleRow(LocHelper.GetLoc("HighwayEngineer_Settings_RoadMarkings"), () => _config.RoadMarkings, value =>
            {
                _config.RoadMarkings = value;
                SaveAndApply();
            });

            AddSeparator();
            AddHeading(LocHelper.GetLoc("HighwayEngineer_Settings_KeybindHeading"), false);
            AddKeyRow(LocHelper.GetLoc("HighwayEngineer_Settings_Key_ZoomIn"), () => _config.ZoomInKey, key => _config.ZoomInKey = key);
            AddKeyRow(LocHelper.GetLoc("HighwayEngineer_Settings_Key_ZoomOut"), () => _config.ZoomOutKey, key => _config.ZoomOutKey = key);
            AddKeyRow(LocHelper.GetLoc("HighwayEngineer_Settings_Key_RollLeft"), () => _config.RollLeftKey, key => _config.RollLeftKey = key);
            AddKeyRow(LocHelper.GetLoc("HighwayEngineer_Settings_Key_RollRight"), () => _config.RollRightKey, key => _config.RollRightKey = key);
            AddKeyRow(LocHelper.GetLoc("HighwayEngineer_Settings_Key_OpenMap"), () => _config.OpenMapKey, key => _config.OpenMapKey = key);
            AddKeyRow(LocHelper.GetLoc("HighwayEngineer_Settings_Key_CenterMap"), () => _config.CenterMapKey, key => _config.CenterMapKey = key);
            AddKeyRow(LocHelper.GetLoc("HighwayEngineer_Settings_Key_RaiseNode"), () => _config.BridgeRaiseKey, key => _config.BridgeRaiseKey = key);
            AddKeyRow(LocHelper.GetLoc("HighwayEngineer_Settings_Key_LowerNode"), () => _config.BridgeLowerKey, key => _config.BridgeLowerKey = key);
            AddKeyRow(LocHelper.GetLoc("HighwayEngineer_Settings_Key_SurfaceView"), () => _config.SurfaceViewKey, key => _config.SurfaceViewKey = key);

            MinimapStyles.Attach(_root);
            _visualTree = _gui.CreateVisualTree(_root);
            _root.Open();
        }

        void AddHeading(string text, bool title)
        {
            _content.Add(new TextBlock
            {
                StyleClass = title ? "settings-header" : "settings-section-heading",
                Height = title ? 46f : 34f,
                Text = text,
                FontSize = title ? 0.94f : 0.76f,
                ColorProvider = () => MinimapTheme.Accent,
                TextAlignment = title
                    ? HorizontalAlignment.Center
                    : HorizontalAlignment.Left
            });
        }

        void AddSeparator()
        {
            var separator = _content.Add(new Border
            {
                CornerRadius = Vector4.Zero,
                BorderThickness = Vector4.Zero,
                BackgroundColorProvider = () => MinimapTheme.OutlineVariant,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            });
            separator.Child = new FixedSpacer { Height = 1f };
        }

        void AddToggleRow(string label, Func<bool> provider, Action<bool> changed)
        {
            var toggle = new ToggleSwitch(provider, changed)
            {
                StyleClass = "settings-toggle"
            };
            AddRow(label, toggle);
        }

        void AddKeyRow(string label, Func<Key> provider, Action<Key> setter)
        {
            KeySelector selector = null;
            selector = new KeySelector(provider(), key =>
            {
                setter(key);
                _config.Save();
            }, () => OnKeySelectorStateChanged(selector))
            {
                KeyProvider = provider,
                StyleClass = "settings-key-selector"
            };
            _keySelectors.Add(selector);
            AddRow(label, selector);
        }

        void AddRow(string label, VisualElement control)
        {
            var row = new SettingsRow(label, control)
            {
                StyleClass = "settings-row"
            };
            _content.Add(row);
        }

        void OnKeySelectorStateChanged(KeySelector active)
        {
            if (active != null && active.IsOpen)
            {
                if (_themeCombo != null)
                    _themeCombo.Close();
                for (int i = 0; i < _keySelectors.Count; i++)
                {
                    if (!ReferenceEquals(_keySelectors[i], active))
                        _keySelectors[i].Close();
                }
            }
            RefreshContentLayout();
        }

        bool CloseTransientControls()
        {
            bool closed = false;
            if (_themeCombo != null && _themeCombo.IsOpen)
            {
                _themeCombo.Close();
                closed = true;
            }
            for (int i = 0; i < _keySelectors.Count; i++)
            {
                if (!_keySelectors[i].IsOpen)
                    continue;
                _keySelectors[i].Close();
                closed = true;
            }
            return closed;
        }

        void RefreshContentLayout()
        {
            if (_root != null)
                _root.Invalidate(UiInvalidation.Measure);
        }

        void OnThemeComboStateChanged()
        {
            RefreshContentLayout();
        }

        void ApplyPendingThemeLayout()
        {
            if (!_themeLayoutPending || _root == null)
                return;
            _themeLayoutPending = false;
            _root.Invalidate(UiInvalidation.Style | UiInvalidation.Measure |
                             UiInvalidation.Arrange | UiInvalidation.Render);
            LayoutVisualTree();
        }

        void SaveAndApply()
        {
            _config.Save();
            if (_viewModel != null)
                _viewModel.ApplyLocalConfig();
        }


        void EnsureLayout()
        {
            if (_root == null)
                return;
            UiInvalidation pending = _root.PendingInvalidation;
            if ((pending & (UiInvalidation.Measure | UiInvalidation.Arrange)) != 0)
                LayoutVisualTree();
        }

        void LayoutVisualTree()
        {
            if (_root == null)
                return;

            Vector2 viewportOffset = Vector2.Zero;
            Vector2 viewportSize = Vector2.Zero;
            if (MyAPIGateway.Session != null && MyAPIGateway.Session.Camera != null)
            {
                viewportOffset = MyAPIGateway.Session.Camera.ViewportOffset;
                viewportSize = MyAPIGateway.Session.Camera.ViewportSize;
            }
            if (viewportSize.X <= 0f || viewportSize.Y <= 0f)
            {
                RectangleF safeArea = MyScreenSpace.GetSafeGuiArea();
                viewportOffset = new Vector2(safeArea.X, safeArea.Y);
                viewportSize = new Vector2(safeArea.Width, safeArea.Height);
            }

            _contentBounds = new RectangleF(viewportOffset, viewportSize);
            if (MinimapStyles.IsSpaceEngineersStyle)
                LayoutSpaceEngineers();
            else
                LayoutFluent();

            _root.Arrange(_contentBounds);
        }

        void LayoutFluent()
        {
            float shortSide = Math.Min(_contentBounds.Width, _contentBounds.Height);
            float scale = Math.Max(0.76f, Math.Min(1.1f, shortSide / 820f));
            float width = Math.Min(
                FluentWindowWidthPixels * scale,
                Math.Max(320f, _contentBounds.Width - 64f));
            float height = Math.Min(
                FluentWindowMaxHeightPixels * scale,
                Math.Max(320f, _contentBounds.Height - 64f));
            float padding = FluentWindowPaddingPixels * scale;

            _content.Width = Math.Max(260f, width - padding * 2f);
            _content.Spacing = FluentRowGapPixels * scale;
            _root.Width = width;
            _root.Height = height;
            _root.Padding = new Vector4(padding);

            if (_scroller != null)
                _scroller.Margin = Vector4.Zero;

            // ADK's title bar carries the title and close button, so the custom header collapses.
            _header.Visible = false;
            float innerHeight = Math.Max(
                1f,
                height - FluentTitleBarHeightPixels - padding * 2f);
            _windowContent.SetRows(0f, innerHeight);
        }

        void LayoutSpaceEngineers()
        {
            // Space Engineers authors its GUI against a 1600x1200 safe area. Match
            // MyGuiScreenOptionsGame's normalized screen dimensions first, then only
            // shrink uniformly if the current safe area cannot contain them.
            float safeGuiScale = Math.Max(0.01f, _contentBounds.Height / 1200f);
            float desiredWidth = KeenSettingsWidthPixels * safeGuiScale;
            float desiredHeight = KeenSettingsHeightPixels * safeGuiScale;
            float maxWidth = Math.Max(320f, _contentBounds.Width - 32f);
            float maxHeight = Math.Max(320f, _contentBounds.Height - 32f);
            float fitScale = Math.Min(1f, Math.Min(maxWidth / desiredWidth, maxHeight / desiredHeight));
            float keenGuiScale = safeGuiScale * Math.Max(0.01f, fitScale);
            float width = KeenSettingsWidthPixels * keenGuiScale;
            float height = KeenSettingsHeightPixels * keenGuiScale;
            float outerPadding = KeenOuterPaddingPixels * keenGuiScale;
            float sideInset = KeenSideInsetPixels * keenGuiScale;

            _content.Width = Math.Max(260f, width - sideInset * 2f);
            _content.Spacing = KeenRowGapPixels * keenGuiScale;
            _root.Width = width;
            _root.Height = height;
            _root.Padding = new Vector4(outerPadding);
            _header.Visible = true;

            // Keep the title/close control near the screen edge, while the scrolling
            // option rows use Keen's ~90 px left/right control inset.
            if (_scroller != null)
            {
                float extraSideInset = Math.Max(0f, sideInset - outerPadding);
                _scroller.Margin = new Vector4(extraSideInset, 0f, extraSideInset, 0f);
            }

            float headerHeight = KeenHeaderHeightPixels * keenGuiScale;
            float innerHeight = Math.Max(
                1f,
                height - outerPadding * 2f);
            _windowContent.SetRows(
                headerHeight,
                Math.Max(1f, innerHeight - headerHeight));
        }

        static string FormatTheme(MinimapStyleTheme theme)
        {
            return theme == MinimapStyleTheme.SpaceEngineers
                ? "Space Engineers"
                : "Fluent";
        }

        /// <summary>
        /// The settings Window is drawn by this screen's own visual tree, so its
        /// platform only tracks the open state; closing it closes the screen.
        /// </summary>
        sealed class HostedWindowPlatform : IWindowPlatform
        {
            public static readonly HostedWindowPlatform Instance = new HostedWindowPlatform();

            public IWindowImpl CreateWindowImpl(Window window)
            {
                return new HostedWindowImpl();
            }

            sealed class HostedWindowImpl : IWindowImpl
            {
                public bool IsOpen { get; private set; }
                public void Open() { IsOpen = true; }
                public void Close() { IsOpen = false; }
                public void BindOpen(Key key) { }
                public bool UnbindOpen(Key key) { return false; }
                public void Dispose() { IsOpen = false; }
            }
        }

        sealed class SettingsRow : VisualElement
        {
            public static readonly Acrux.Core.PropertySystem.StyledProperty<float> MinimumHeightProperty =
                Acrux.Core.PropertySystem.StyledProperty.Register<SettingsRow, float>(
                    nameof(MinimumHeight), 42f, UiInvalidation.Measure);
            public static readonly Acrux.Core.PropertySystem.StyledProperty<float> LabelWidthFractionProperty =
                Acrux.Core.PropertySystem.StyledProperty.Register<SettingsRow, float>(
                    nameof(LabelWidthFraction), 0.40f, UiInvalidation.Arrange);

            readonly TextBlock _label;
            readonly VisualElement _control;

            public SettingsRow(string label, VisualElement control)
            {
                _label = Add(new TextBlock
                {
                    Text = label,
                    StyleClass = "settings-label",
                    FontSize = 0.70f,
                    Foreground = Color.White,
                    VerticalAlignment = VerticalAlignment.Center
                });
                _control = Add(control);
            }

            public float MinimumHeight
            {
                get { return GetValue(MinimumHeightProperty); }
                set { SetValue(MinimumHeightProperty, Math.Max(0f, value)); }
            }

            public float LabelWidthFraction
            {
                get { return GetValue(LabelWidthFractionProperty); }
                set { SetValue(LabelWidthFractionProperty, MathHelper.Clamp(value, 0f, 1f)); }
            }

            protected override Vector2 MeasureOverride(Vector2 availableSize)
            {
                float controlWidth = Math.Max(100f, availableSize.X * 0.58f);
                Vector2 desired = _control.Measure(new Vector2(controlWidth, float.MaxValue));
                return new Vector2(availableSize.X, Math.Max(MinimumHeight, desired.Y));
            }

            protected override void ArrangeChildren(RectangleF bounds)
            {
                float labelWidth = bounds.Width * LabelWidthFraction;
                _label.Arrange(new RectangleF(bounds.X, bounds.Y, labelWidth, Math.Min(MinimumHeight, bounds.Height)));
                float controlX = bounds.X + labelWidth;
                float controlWidth = Math.Max(1f, bounds.Right - controlX);
                Vector2 desired = _control.Measure(new Vector2(controlWidth, bounds.Height));
                _control.ArrangeInSlot(
                    new RectangleF(controlX, bounds.Y, controlWidth, bounds.Height),
                    new Vector2(Math.Min(controlWidth, desired.X), desired.Y));
            }
        }

        sealed class FixedSpacer : VisualElement
        {
        }
    }
}
