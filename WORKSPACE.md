# RoadCrafter workspace snapshot

This is the consolidated RoadCrafter MDK-style C# source workspace, including the existing Minimap-derived planetary screen, the Procedural Voxel Cubemap API client mirror, road planning/node interactions, tangent editor using `VRage.MyTuple`, and Push Road integration.

Open `RoadCrafter.sln`. The target is .NET Framework 4.8 and the project uses MDK2 and ADK shared sources. Game binaries and the .NET SDK are **not** included; configure MDK2/game installation paths on each machine.

## State and boundaries

- Default editor key: **U**.
- Road node placement, splitting, removal, dragging, and tangent handles are source-level prototype features.
- The game-side cubemap API must separately contain the planet metadata and `DrawLine` additions; this repository does not bundle the server mod.
- An offline compatibility build previously reported zero errors, but an actual native MDK2 / Space Engineers in-game validation is still required.
- `HighwayEngineer/Client/` retains some legacy internal Minimap dependencies for rendering; the old system-map/GPS interface is not the intended UI.

This is an importable Git snapshot for workspace synchronization, not a published remote repository.
