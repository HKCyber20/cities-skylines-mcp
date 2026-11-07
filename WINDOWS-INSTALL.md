# Windows release installation

This archive contains only the CS1 MCP Bridge mod DLL, this guide, and the
upstream MIT license. It does not contain game assemblies, saves, or the Python
MCP server.

1. Close Cities: Skylines 1.
2. Copy `CS1McpBridge.dll` to
   `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\CS1McpBridge\`.
   Back up an existing DLL first.
3. Enable **CS1 MCP Bridge** in Content Manager, then load a city.
4. Get the matching source repository for the Python server. In `server/`, run
   `uv sync --locked`, then `uv run cs1-mcp` from an MCP client. See the source
   `server/README.md` for Codex setup.
5. Call `ping` and expect `pong`. Use a copy of a save for write commands.

To uninstall, close the game, remove this mod DLL, and restore any DLL you
backed up in step 2.
