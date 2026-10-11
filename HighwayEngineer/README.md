# HighwayEngineer

Road planning and construction for Space Engineers planets. It grew out of the RoadCrafter proof of concept; its namespaces, content subtypes and file names now all use the HighwayEngineer name.

## UI and map: Acrux UI

The planner's UI is drawn by the **Acrux UI** mod, shared by every mod that draws UI (the LCD mod, Minimap). HighwayEngineer compiles only its views (`*.acrux.xaml` + code-behind), view models and controls, against the thin generated wrappers the signed `Acrux.Sdk` package (`../packages/`, updated with `../update-acrux.sh` or `update-acrux.bat`) emits into the build. A world needs both mods.

The planner has a 3D view of the terrain (default key **V**, or drag with the middle mouse button): the draft, its nodes and handles are drawn on the displaced ground, and each bridge of the draft is shown as the real shape the server would build. The client plans it with the same `Common/Structures/BridgePlanner` the server prices and builds from, on its own terrain, and meshes the result (`Client/Roads/BridgePreview*.cs`) into an Acrux `SceneModel`.

* Startup: `HighwayEngineerSessionComponent.LoadData` connects through `AcruxUiToolkitClient.Connect`. The planner screen starts, and LCD planners build their views, only once Acrux UI has connected (`HighwayEngineerClientComponent.UiAvailable`); without it, LCD planners say so and the client's non-UI work (road markings, repairs, contracts) runs as usual.
* Map: the planet renderer and cartography (`Acrux.Cartography`, `CartographyModule.Shared`) come from Acrux UI and are shared with the other mods. The planner sets its view's `MaximumZoom` (no upper limit), draws roads itself as overlays, and projects them through `Client/Maps/PlanetSurfaceProjector`, which takes the frame's projection from the renderer once.
* Styles live under `Content/Data/Gui/Styles/HighwayEngineer/` (style paths are shared by every Acrux UI client, so they need a mod-unique folder) and are set on HighwayEngineer's own roots (`MinimapStyles.Attach`), never on the toolkit-wide `StyleSheet.Application`. The Fluent and Space Engineers base themes come from Acrux UI (`/Data/Gui/Styles/Acrux/...`).
* Anything the planner needs from Acrux UI that other mods could use too belongs in Acrux UI; mod-specific code stays here.

## Layout

Same split as Arthur's LCD Mod; the ADK `client-server-boundary` check warns when one side imports the other.

* `HighwayEngineerSessionComponent.cs` (`HighwayEngineer`): entry point. Owns the `NetworkManager` and creates the server component (`IsServer`) and the client component (not a dedicated server); a listen host or single player runs both.
* `Client/` (`HighwayEngineer.Client.*`): the planner screen, map rendering, LCD planners and other UI. Namespaces follow folders.
  * `Apps/<Feature>/`: one folder per screen or dialog with its `*View`/`*Panel`/`*Dialog` (`.acrux.xaml` + code-behind) and `*ViewModel` (`RoadPlanner`, `PushRoad`, `Repair`, `RoadSettings`, `BridgeSettings`, `AutoRoute`, `Settings`); `Apps/Views/` holds shared converters and the `loc` markup extension.
  * `Gui/UserControls/`: reusable controls, one per file; `Gui/Styling/`: themes and style loading.
  * `Helpers/`: client-only helpers such as `RoadChat` (chat lines and notifications).
* `Server/` (`HighwayEngineer.Server.*`): authoritative road quotes, contracts and pushes.
* `Common/` (`HighwayEngineer.Common.*`): code both sides use, such as road geometry and earthwork (`Roads/`), prices (`Config/`), constants, the voxel API helper and `LocHelper` (`Helpers/`), and network packets (`Networking/`, one `Packet*` per file).

## Localization

Every player-facing string lives in `Content/Data/Localization/MyTexts*.resx` under a `HighwayEngineer_` key: English in `MyTexts.resx` and a file for every language the game offers, named like the game's own (`MyTexts.de.resx`, `MyTexts.pt-BR.resx`, `MyTexts.es-419.resx`, ...). The game loads `MyTexts.<culture>.resx` and then `MyTexts.<culture>-<region>.resx` over it. Chinese is the exception: Rider's resx editor does not know the game's `zh-CN` culture, so the source is `MyTexts.zh.resx` and the build copies it to `MyTexts.zh-CN.resx` for packing (and deletes the copy afterwards), as in the LCD mod.

* XAML: `Text="{ext:loc HighwayEngineer_Common_Cancel}"` with `xmlns:ext="using:HighwayEngineer.Client.Apps.Views.Extensions"` (Acrux resolves it when the view is built).
* C#: `LocHelper.GetLoc(key)` / `LocHelper.Format(key, args)`; chat command help uses a `/// <loc>Key</loc>` doc tag.
* Server to player: build the text with `LocHelper.Message(key, args)` and show it with `LocHelper.Resolve(text)` (or `RoadRejectReasonText.Describe` / `RoadChat`), so each client reads it in its own language; numeric arguments are formatted with the reader's culture. Text the game stores itself (contract titles and descriptions, GPS names) uses the server's language.
* When adding or changing a key, update every `MyTexts.*.resx` with the same key and placeholders (native phrasing, not word-for-word), then run `python3 tools/check_localization.py`.

* Press **U** to open the planetary editor; Escape closes it.
* Click the visible planet to add a road node. No GPS records are created.
* Node circles draw as a UI overlay. Road strokes are painted over the globe image.
* Terrain is **not** modified; the Build action, precise Hermite geometry, node dragging and persistence are follow-up tasks.

HighwayEngineer's `HighwayEngineerConfig.xml` and intermod reply channel are independent of the Minimap mod, so both can be installed simultaneously.

Build: Visual Studio + MDK2 for Space Engineers (`netframework48`); the independent .NET 10 source-compatibility build is a non-runtime check only.
