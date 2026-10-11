using HighwayEngineer.Client.Gui.Styling;
using System;
using System.Collections.Generic;
using Acrux.Core;
using Acrux.Core.Input;
using Acrux.Core.Invalidation;
using Acrux.Lcd;
using LcdEyeTracking.Api;
using HighwayEngineer.Client.Infrastructure;
using Sandbox.Game.Components;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace HighwayEngineer.Client.Lcd
{
    /// <summary>
    /// A text-surface script that shows one ADK control (a XAML UserControl) on an
    /// LCD, over the panel's own background and grid. Input comes from the LCD Eye
    /// Tracking API mod. The game runs the script every 10 ticks; rendering goes
    /// through <see cref="LcdSurfaceTicker"/> instead, every frame while the surface is
    /// looked at or changing, so the cursor and drags run at the game's frame rate.
    /// The panel's Font size, Text padding and the per-surface scale apply.
    /// </summary>
    public abstract class LcdControlSurfaceScript : MyTSSCommon
    {
        /// <summary>Layout units per surface pixel are 1 on a 512 px panel.</summary>
        const float REFERENCE_SURFACE_PIXELS = 512f;
        const float CURSOR_PIXELS = 32f;
        /// <summary>The game stops running scripts out of range; so does the per-frame render.</summary>
        const int RUN_TIMEOUT_FRAMES = 30;
        /// <summary>Frames after the last look the surface keeps rendering every frame.</summary>
        const int LOOK_TIMEOUT_FRAMES = 6;

        readonly IMyTextSurface _surface;
        readonly IMyCubeBlock _block;
        int _screenAreaIndex = -1;
        readonly List<MySprite> _sprites = new List<MySprite>();
        readonly List<MySprite> _background = new List<MySprite>();
        LcdGuiHost _host;
        VisualElement _content;
        LcdEyeTrackingApi _tracking;
        long _trackingHandle;
        Vector2 _pointer = new Vector2(float.NaN);
        bool _cursorDrawn;
        bool _disposed;
        bool _rendering;
        bool _errorLogged;
        bool _renderRequested = true;
        int _lastUpdateFrame = int.MinValue;
        int _lastRunFrame = int.MinValue;
        int _lastLookFrame = int.MinValue;
        long _drawnTextureVersion = -1;
        RectangleF _arrangedBounds;
        float _arrangedScaling;
        float _fontSize = -1f;
        Color _backgroundColor;
        Vector2 _backgroundSize;

        protected LcdControlSurfaceScript(Sandbox.ModAPI.Ingame.IMyTextSurface surface, IMyCubeBlock block,
            Vector2 size)
            : base(surface, block, size)
        {
            _surface = surface as IMyTextSurface;
            _block = block;
        }

        public override ScriptUpdate NeedsUpdate => ScriptUpdate.Update10;

        protected LcdGuiHost Host => _host;

        /// <summary>The player is looking at this surface (the pointer moved over it in the last few frames).</summary>
        protected bool IsLookedAt => Frame - _lastLookFrame <= LOOK_TIMEOUT_FRAMES;
        protected IMyCubeBlock OwnerBlock => _block;
        protected Sandbox.ModAPI.Ingame.IMyTextSurface OwnerSurface => _surface;

        /// <summary>Creates the hosted control; called once, on the first run.</summary>
        protected abstract VisualElement CreateContent();

        /// <summary>
        /// Runs at most once per frame before drawing: every script run, and every frame
        /// while the surface renders through the per-frame route.
        /// </summary>
        protected virtual void UpdateContent()
        {
        }

        /// <summary>True while the content changes on its own and should render every frame.</summary>
        protected virtual bool NeedsFrameUpdates => false;

        /// <summary>
        /// Layout units per surface pixel before the user's scale; by default 1 on a 512 px panel.
        /// </summary>
        protected virtual float BaseRenderScaling =>
            Math.Max(0.25f, Math.Min(_surface.SurfaceSize.X, _surface.SurfaceSize.Y) / REFERENCE_SURFACE_PIXELS);

        /// <summary>The user's scale for this surface (terminal "Planner scale").</summary>
        protected virtual float UserScale => 1f;

        /// <summary>Called before the content is arranged at new bounds, in layout units.</summary>
        protected virtual void OnLayout(RectangleF bounds)
        {
        }

        /// <summary>Called after the content was arranged.</summary>
        protected virtual void OnArranged()
        {
        }

        protected virtual void BeforeDraw()
        {
        }

        protected virtual void AfterDraw()
        {
        }

        float RenderScaling => Math.Max(0.1f, BaseRenderScaling * UserScale);

        /// <summary>The visible part of the surface texture after Text padding, in sprite coordinates.</summary>
        RectangleF ViewBounds
        {
            get
            {
                Vector2 size = _surface.SurfaceSize;
                Vector2 padding = size * (_surface.TextPadding / 100f);
                Vector2 offset = (_surface.TextureSize - size) * 0.5f + padding * 0.5f;
                return new RectangleF(offset, Vector2.Max(Vector2.One, size - padding));
            }
        }

        static int Frame => MyAPIGateway.Session == null ? 0 : MyAPIGateway.Session.GameplayFrameCounter;

        public override void Run()
        {
            base.Run();
            if (_disposed || _surface == null || _block == null)
                return;
            // On a multiplayer client the game runs scripts on a worker thread, while the ticker
            // draws on the game thread: the planner is not thread-safe, so this only asks the
            // ticker to run it there.
            LcdSurfaceTicker.RequestRun(this);
        }

        /// <summary>What a script run does, on the game thread (see <see cref="LcdSurfaceTicker"/>).</summary>
        internal void RunOnGameThread()
        {
            if (_disposed)
                return;
            try
            {
                // The planner is drawn by the Acrux UI mod; without it the panel says so, in plain sprites.
                if (_host == null && !HighwayEngineerClientComponent.UiAvailable)
                {
                    DrawUiMissing();
                    return;
                }
                if (_host == null && !CreateHost())
                    return;
                _lastRunFrame = Frame;
                ApplySurfaceSettings();
                EnsureTracking();
                if (_trackingHandle != 0L)
                    _tracking.Heartbeat(_trackingHandle);
                _renderRequested = true;
                Tick();
            }
            catch (Exception error)
            {
                LogError(error);
            }
        }

        /// <summary>The per-frame route: updates and renders when anything may have changed.</summary>
        internal void Tick()
        {
            if (_disposed || _host == null)
                return;
            int frame = Frame;
            if (frame - _lastRunFrame > RUN_TIMEOUT_FRAMES)
                return;
            bool looked = frame - _lastLookFrame <= LOOK_TIMEOUT_FRAMES;
            bool textureChanged = LcdDynamicTexture.GlobalContentVersion != _drawnTextureVersion;
            if (!looked && !_renderRequested && !_cursorDrawn && !textureChanged && !NeedsFrameUpdates &&
                !_host.NeedsRender)
                return;
            try
            {
                if (_lastUpdateFrame != frame)
                {
                    _lastUpdateFrame = frame;
                    UpdateContent();
                }
                Render(looked);
            }
            catch (Exception error)
            {
                LogError(error);
            }
        }

        bool CreateHost()
        {
            MinimapStyles.RegisterScreen(HighwayEngineerSessionComponent.ModItem, MinimapLocalConfig.Current.Theme);
            VisualElement content = CreateContent();
            if (content == null)
                return false;
            MinimapStyles.Attach(content);
            _content = content;
            _host = new LcdGuiHost(_surface, content, "Cross")
            {
                RenderScalingProvider = () => RenderScaling
            };
            ApplySurfaceSettings();
            LcdSurfaceTicker.Add(this);
            return true;
        }

        /// <summary>Follows the panel's Font size, Text padding and the user scale.</summary>
        void ApplySurfaceSettings()
        {
            float fontSize = Math.Max(0.1f, _surface.FontSize);
            if (fontSize != _fontSize)
            {
                _fontSize = fontSize;
                _host.Renderer.TextScaleMultiplier = fontSize;
                _host.Root.Invalidate(UiInvalidation.Measure | UiInvalidation.Arrange | UiInvalidation.Render);
                _host.InvalidateFrame();
            }
            RectangleF bounds = ViewBounds;
            float scaling = RenderScaling;
            if (bounds == _arrangedBounds && scaling == _arrangedScaling)
                return;
            _arrangedBounds = bounds;
            _arrangedScaling = scaling;
            _host.RenderScaling = scaling;
            OnLayout(new RectangleF(bounds.Position, bounds.Size / scaling));
            _host.Arrange(bounds);
            OnArranged();
            _renderRequested = true;
        }

        void EnsureTracking()
        {
            if (_trackingHandle != 0L)
                return;
            _tracking = LcdEyeTracking.Api;
            if (_tracking == null)
                return;
            _trackingHandle = _tracking.Register(_block, _surface,
                OnPointerMoved, OnPointerLeft, OnPointerButton, OnPointerScrolled, OnCaptureLost, 0L);
            if (_trackingHandle != 0L)
                _tracking.SetFlags(_trackingHandle, PointerFlags.RequiresHeartbeat);
        }

        void OnPointerMoved(Vector2 position)
        {
            Handle(() =>
            {
                _pointer = position;
                _lastLookFrame = Frame;
                _host.MovePointer(position);
            });
        }

        void OnPointerLeft()
        {
            Handle(() =>
            {
                _pointer = new Vector2(float.NaN);
                _host.ClearPointer();
            });
        }

        void OnPointerButton(int button, bool pressed)
        {
            Handle(() =>
            {
                switch ((PointerButton)button)
                {
                    case PointerButton.Primary:
                        _host.SetPrimaryButton(pressed);
                        break;
                    case PointerButton.Secondary:
                        _host.SetSecondaryButton(pressed);
                        break;
                    case PointerButton.Middle:
                        _host.SetMiddleButton(pressed);
                        break;
                }
            });
        }

        void OnPointerScrolled(int delta)
        {
            Handle(() => _host.Scroll(delta));
        }

        void OnCaptureLost()
        {
            Handle(() =>
            {
                // Cancel capture before releasing: losing the input owner is not a click.
                _host.ClearPointer();
                _host.SetPrimaryButton(false);
                _host.SetSecondaryButton(false);
                _host.SetMiddleButton(false);
            });
        }

        /// <summary>Input only updates state; the per-frame route renders once for the frame.</summary>
        void Handle(Action input)
        {
            if (_disposed || _host == null)
                return;
            try
            {
                input();
                _renderRequested = true;
            }
            catch (Exception error)
            {
                LogError(error);
            }
        }

        void DrawUiMissing()
        {
            _sprites.Clear();
            AddBackground(_sprites);
            RectangleF view = ViewBounds;
            _sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = HighwayEngineer.Common.Helpers.LocHelper.GetLoc("HighwayEngineer_Ui_Missing"),
                Position = view.Center - new Vector2(0f, view.Height * 0.05f),
                Color = _surface.FontColor,
                FontId = "White",
                Alignment = TextAlignment.CENTER,
                RotationOrScale = Math.Max(0.4f, view.Width / REFERENCE_SURFACE_PIXELS) * 0.7f
            });
            using (MySpriteDrawFrame frame = _surface.DrawFrame())
                frame.AddRange(_sprites);
        }

        void Render(bool looked)
        {
            // Something the draw calls can come back here (it happened once while a world loaded);
            // that frame is skipped and drawn on the next tick instead of opening a second LCD frame.
            if (_rendering)
                return;
            _rendering = true;
            try
            {
                RenderFrame(looked);
            }
            finally
            {
                _rendering = false;
            }
        }

        void RenderFrame(bool looked)
        {
            _renderRequested = false;
            _drawnTextureVersion = LcdDynamicTexture.GlobalContentVersion;
            _sprites.Clear();
            AddBackground(_sprites);
            BeforeDraw();
            try
            {
                _host.Draw(_sprites);
            }
            finally
            {
                AfterDraw();
            }
            bool cursor = looked && !float.IsNaN(_pointer.X);
            if (cursor)
            {
                PointerCursor pointerCursor = _host.PointerCursor;
                LcdCursor.Add(_sprites, pointerCursor == PointerCursor.Inherit ? PointerCursor.Arrow : pointerCursor,
                    _pointer, CURSOR_PIXELS * BaseRenderScaling);
            }
            _cursorDrawn = cursor;
            Upload(_sprites);
        }

        /// <summary>
        /// Renders the sprites into the panel's texture now, as the LCD mod does. A draw
        /// frame would only reach the panel on the game's own (slower) LCD update, so once a
        /// surface renders this way every frame must, or the two would fight.
        /// </summary>
        void Upload(List<MySprite> sprites)
        {
            int area = ScreenAreaIndex;
            // Looked up every time: on a multiplayer client the block's render component can be
            // replaced after the script starts, and a detached one throws (it has no entity).
            var renderComponent = _block.Render as MyRenderComponentScreenAreas;
            if (renderComponent == null || renderComponent.Entity == null || area < 0)
            {
                using (MySpriteDrawFrame frame = _surface.DrawFrame())
                    frame.AddRange(sprites);
                return;
            }
            Vector2 surfaceSize = _surface.SurfaceSize;
            Vector2 aspectRatio = surfaceSize.X > surfaceSize.Y
                ? new Vector2(1f, surfaceSize.Y / surfaceSize.X)
                : new Vector2(surfaceSize.X / surfaceSize.Y, 1f);
            renderComponent.RenderSpritesToTexture(area, sprites, (Vector2I)_surface.TextureSize, aspectRatio,
                _surface.ScriptBackgroundColor, _surface.BackgroundAlpha);
        }

        /// <summary>The block's screen area showing this surface: LCD panels use the selected rotation.</summary>
        int ScreenAreaIndex
        {
            get
            {
                if (_screenAreaIndex >= 0 || _block.CubeGrid == null || _block.CubeGrid.Physics == null)
                    return _screenAreaIndex;
                if (_block is IMyTextPanel)
                {
                    foreach (var component in _block.Components)
                    {
                        var lcd = component as IMyLcdSurfaceComponent;
                        if (lcd != null)
                            return _screenAreaIndex = lcd.SelectedRotationIndex;
                    }
                    return -1;
                }
                return _screenAreaIndex = PlannerSettings.GetSurfaceIndex(_block, _surface);
            }
        }

        /// <summary>The grid every native script draws, tinted by the panel's background color.</summary>
        void AddBackground(List<MySprite> sprites)
        {
            Color color = new Color(_surface.ScriptBackgroundColor, 0.66f);
            Vector2 textureSize = _surface.TextureSize;
            if (_background.Count == 0 || color != _backgroundColor || textureSize != _backgroundSize)
            {
                _backgroundColor = color;
                _backgroundSize = textureSize;
                _background.Clear();
                MySprite tile = MyTextSurfaceHelper.DEFAULT_BACKGROUND;
                tile.Color = color;
                Vector2 tileSize = tile.Size ?? new Vector2(MyTextSurfaceHelper.BACKGROUND_SIZE);
                if (tileSize.X <= 0f || tileSize.Y <= 0f)
                    tileSize = new Vector2(MyTextSurfaceHelper.BACKGROUND_SIZE);
                Vector2 shift = MyTextSurfaceHelper.BACKGROUND_SHIFT;
                float stepX = Math.Abs(shift.X) > 0.001f ? Math.Abs(shift.X) : tileSize.X;
                float stepY = Math.Abs(shift.Y) > 0.001f ? Math.Abs(shift.Y) : tileSize.Y;
                // Tiles are laid out from the game's own tile so the grid matches native screens.
                Vector2 center = (tile.Position ?? Vector2.Zero) + textureSize / 2f;
                Vector2 half = tileSize * 0.5f;
                float startX = center.X + (float)Math.Floor((0f - (center.X - half.X)) / stepX) * stepX;
                float startY = center.Y + (float)Math.Floor((0f - (center.Y - half.Y)) / stepY) * stepY;
                for (float y = startY; y - half.Y < textureSize.Y; y += stepY)
                    for (float x = startX; x - half.X < textureSize.X; x += stepX)
                    {
                        tile.Position = new Vector2(x, y);
                        _background.Add(tile);
                    }
            }
            sprites.AddRange(_background);
        }

        void LogError(Exception error)
        {
            if (_errorLogged)
                return;
            _errorLogged = true;
            MyLog.Default.WriteLine("[HighwayEngineer] " + GetType().Name + ": " + error);
        }

        public override void Dispose()
        {
            // May come from a worker thread too: the host is torn down on the game thread.
            _disposed = true;
            LcdSurfaceTicker.RequestDispose(this);
            base.Dispose();
        }

        /// <summary>Tears down what the subclass built, on the game thread, before the host goes.</summary>
        protected virtual void OnDisposing()
        {
        }

        internal void DisposeOnGameThread()
        {
            OnDisposing();
            LcdSurfaceTicker.Remove(this);
            if (_trackingHandle != 0L && _tracking != null)
                _tracking.Unregister(_trackingHandle);
            _trackingHandle = 0L;
            if (_content != null)
                MinimapStyles.Detach(_content);
            _content = null;
            if (_host != null)
                _host.Dispose();
            _host = null;
        }
    }

    /// <summary>
    /// Runs every <see cref="LcdControlSurfaceScript"/> on the game thread: the script runs and
    /// disposals the game asks for (from any thread), then the per-frame render route.
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public sealed class LcdSurfaceTicker : MySessionComponentBase
    {
        static readonly List<LcdControlSurfaceScript> Surfaces = new List<LcdControlSurfaceScript>();
        static readonly List<LcdControlSurfaceScript> Snapshot = new List<LcdControlSurfaceScript>();
        static readonly object PendingLock = new object();
        static readonly List<LcdControlSurfaceScript> PendingRuns = new List<LcdControlSurfaceScript>();
        static readonly List<LcdControlSurfaceScript> PendingDisposals = new List<LcdControlSurfaceScript>();

        /// <summary>Any thread: the script runs on the game thread's next update.</summary>
        internal static void RequestRun(LcdControlSurfaceScript surface)
        {
            lock (PendingLock)
                if (!PendingRuns.Contains(surface))
                    PendingRuns.Add(surface);
        }

        /// <summary>Any thread: the script is torn down on the game thread's next update.</summary>
        internal static void RequestDispose(LcdControlSurfaceScript surface)
        {
            lock (PendingLock)
            {
                PendingRuns.Remove(surface);
                if (!PendingDisposals.Contains(surface))
                    PendingDisposals.Add(surface);
            }
        }

        internal static void Add(LcdControlSurfaceScript surface)
        {
            if (!Surfaces.Contains(surface))
                Surfaces.Add(surface);
        }

        internal static void Remove(LcdControlSurfaceScript surface)
        {
            Surfaces.Remove(surface);
        }

        public override void UpdateAfterSimulation()
        {
            RunPending();
            if (Surfaces.Count == 0)
                return;
            Snapshot.Clear();
            Snapshot.AddRange(Surfaces);
            for (int i = 0; i < Snapshot.Count; i++)
                Snapshot[i].Tick();
            Snapshot.Clear();
        }

        static void RunPending()
        {
            Snapshot.Clear();
            lock (PendingLock)
            {
                if (PendingDisposals.Count == 0 && PendingRuns.Count == 0)
                    return;
                Snapshot.AddRange(PendingDisposals);
                PendingDisposals.Clear();
            }
            for (int i = 0; i < Snapshot.Count; i++)
                Snapshot[i].DisposeOnGameThread();
            Snapshot.Clear();
            lock (PendingLock)
            {
                Snapshot.AddRange(PendingRuns);
                PendingRuns.Clear();
            }
            for (int i = 0; i < Snapshot.Count; i++)
                Snapshot[i].RunOnGameThread();
            Snapshot.Clear();
        }

        protected override void UnloadData()
        {
            RunPending();
            Surfaces.Clear();
            Snapshot.Clear();
            lock (PendingLock)
            {
                PendingRuns.Clear();
                PendingDisposals.Clear();
            }
        }
    }
}
