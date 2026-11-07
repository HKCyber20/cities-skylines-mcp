# cs1-mcp

Drive **Cities: Skylines (CS1)** from AI tools over the Model Context Protocol:
AI-directed city-building (roads, buildings), cinematic tours, timelapses, and
other short-form content.

Two halves, one protocol:

| dir                  | what                                                                |
|----------------------|---------------------------------------------------------------------|
| [`mod/`](mod)        | the in-game C# bridge mod; opens a loopback socket inside the game  |
| [`server/`](server)  | a FastMCP (Python) server that exposes the bridge as MCP tools      |
| [`PROTOCOL.md`](PROTOCOL.md) | the newline-delimited JSON contract between them; any client can speak it |

```
MCP client (Claude, etc.)
        │  MCP (stdio)
        ▼
   server/  (FastMCP, Python)
        │  newline-delimited JSON over TCP  (PROTOCOL.md)
        ▼
    mod/    (C# bridge, in-game)  ──►  Cities: Skylines managers
```

> **Status:** working, verified end-to-end against a live base-game CS1 (macOS,
> monolithic Unity build). This repo is the open bridge and server only;
> content/orchestration logic lives elsewhere.

This fork also has a [Windows verification record](README.zh-CN.md) using a
local CS1 1.21.1-f9 installation and an isolated test save.

The full command set (camera, weather, economy, info-views, road/building
placement, screenshots, and more) is documented in [PROTOCOL.md](PROTOCOL.md).

Version 0.2.0 adds terrain-relative road heights, native CS1
bridge/tunnel variants, explicit endpoint-node connections, and
`inspect_network` for checking actual constructed nodes and segments. See
[PROTOCOL.md](PROTOCOL.md) and [README.zh-CN.md](README.zh-CN.md). Straight
Basic Road straight profiles and connected Medium Road, Large Road and Highway
profiles passed on an isolated test save, including actual segment prefabs,
node elevations and shared endpoint IDs.

## Quick start

1. **Mod:** build `mod/` against your local game, install, enable it, load a city
   (see [Build the mod](#build-the-mod)).
2. **Server:** `cd server && uv run cs1-mcp`, then point your MCP client at it
   (see [server/README.md](server/README.md)).
3. **Verify:** call the `ping` tool; expect `"pong"`.

## Build the mod

Requires the .NET SDK and a local CS1 install. No external packages or online
NuGet downloads: an offline restore runs before compilation against the game's framework and Unity
assemblies (game DLLs are referenced, never vendored, per Paradox terms).

```bash
dotnet build mod/CS1McpBridge.csproj -c Release
# override the install path if the default for your OS is wrong:
dotnet build mod/CS1McpBridge.csproj -c Release -p:ManagedPath="/path/to/Cities_Data/Managed"
```

Copy `CS1McpBridge.dll` into the game's `Addons/Mods/CS1McpBridge/` folder and
enable it in Content Manager, Mods. No other mods required.

### Windows (Steam)

Use PowerShell 7 and the .NET SDK. The helper searches Steam's registered install
and `libraryfolders.vdf` for CS1, including libraries on other drives. If Steam
cannot be detected, pass `-GamePath` (the directory containing `Cities.exe`) or
set `CS1_GAME_PATH`. You can instead pass `-ManagedPath` directly.

```powershell
pwsh -File scripts/windows-mod.ps1 -Action Build -GamePath 'X:\Steam\steamapps\common\Cities_Skylines'
# Close the game before installing or uninstalling:
pwsh -File scripts/windows-mod.ps1 -Action Install -GamePath 'X:\Steam\steamapps\common\Cities_Skylines'
pwsh -File scripts/windows-mod.ps1 -Action Uninstall
```

The helper installs only the mod DLL under
`%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\CS1McpBridge`.
If a DLL already exists, it preserves a `.dll.bak` copy in
`Addons\.cs1mcp-backups` (outside the scanned Mods directory) and restores
it on uninstall. Pass `-ModsPath` to target a different Mods directory. A build
uses the local game assemblies and never copies them into the repository.

For the MCP server and Codex setup, see [server/README.md](server/README.md).

## License

MIT, see [LICENSE](LICENSE).
