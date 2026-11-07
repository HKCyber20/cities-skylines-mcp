# CS1 MCP Bridge 0.1.1 — Windows support

This fork builds against a local Cities: Skylines 1 installation on Windows.
It adds Steam library discovery, an install/uninstall helper with backup and
restore, Codex setup guidance, and a read-only stdio MCP smoke test.

The bridge no longer retries uncertain writes. Pending game-thread work is
cancelled when its city unloads. Pause and day/night controls use the fields
verified against the local game's `SimulationManager` implementation.

Verified on CS1 1.21.1-f9 with an isolated test save: MCP initialization and
19 tools, read-only city queries, road/path placement, park placement and
removal, a two-second pause check, and setting the day/night hour. Final
control tests used `-noWorkshop`; earlier placement tests also passed in a
normal launch. See the source repository's `README.zh-CN.md` for the validation table.

Roads are created at ground height. There is no bridge/tunnel height input,
automatic connection to existing roads, zoning tool, or save tool in this
release.

The ZIP contains only this project's mod DLL, its portable install guide, and
the upstream MIT license. The Python MCP server is in the source repository.
Original project and author: [jacklenzotti/cities-skylines-mcp](https://github.com/jacklenzotti/cities-skylines-mcp), Jack Lenzotti.
