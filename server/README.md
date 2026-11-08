# cs1-mcp (server)

FastMCP server that exposes a running **Cities: Skylines (CS1)** game as MCP
tools. It's a thin client over the [CS1 MCP Bridge](../mod) mod — the mod must be
active in a loaded city for tools to work.

## Tools

| tool             | what it does                                            |
|------------------|---------------------------------------------------------|
| `ping`           | confirm the game/bridge is reachable                    |
| `set_sim_speed`  | speed 0–3 + pause (timelapses, holding a frame)         |
| `get_city_stats` | population + money                                       |
| `set_camera`     | frame a shot (position, angle, zoom)                    |
| `screenshot`     | capture a PNG, returns the path                         |
| `inspect_network` | read node height/flags, segment prefab/endpoints, and area status |

See [PROTOCOL.md](../PROTOCOL.md) for the full command set.

For the 0.2.0 bridge/tunnel release, use
`list_prefabs(kind="road_variants")` to discover loaded base road families and
their elevated/bridge/slope/tunnel variants. `place_path` accepts
`[x,z,height]` points with height in meters relative to raw terrain. Its
optional `modes` list requires the actual variant for each leg. Run
`python tests/network_profile.py --road "Basic Road" --x X --z Z` first to
check variants and purchased-area points without changing the city. Add
`--allow-test-writes` only on an isolated save with roughly 750m × 270m of
clear, purchased land; run again with a second loaded base road family to
cover road diversity. Straight Basic Road and connected Medium Road, Large Road
and Highway bridge/tunnel profiles passed in an isolated save with real node
heights and segment prefabs.
Use `--straight-only` with the write flags to test straight graded roads
without an existing endpoint; omit it to test a new ground approach connected
to the bridge/tunnel path. Both modes leave created roads in the test save and
never save or reload it automatically. Connecting to an existing straight,
moveable endpoint at a nearly collinear angle is refused because the native
road tool may replace that pre-existing node and segment.

`--profile tunnel` or `--profile bridge` runs only the selected corridor; the
default is `both`. Entrance ramps use automatic variant selection because the
native tool can split a ramp into ground and slope/elevated pieces. The pure
high bridge span and deep tunnel span require those exact variants.
Use `--row-gap METERS` (80..1000, default 220) to move the bridge corridor
relative to the tunnel corridor when the purchased land or waterways require
different spacing. The read-only area probes follow the same offset.

Version 0.2.1 additionally recognizes standalone `RoadBridgeAI` road networks
without inventing a ground or tunnel variant. Use
`list_prefabs(kind="road_variants")` to identify `standalone_bridge=true`, then
build a positive-height bridge from an elevated endpoint. For fixed-height
`RoadAI` bases, the server still accepts the base name; the mod selects a real
loaded child variant for each leg. The 2-way Massongex standalone bridge and
three loaded fixed/normal RoadAI bridge assets passed isolated-save build and
readback tests. Third-party model appearance and traffic still require visual
and operational checks; the tested Wiwi truss disappears at distant zoom.

## Run

```bash
cd server
uv run cs1-mcp          # or: pip install -e . && cs1-mcp
```

Speaks stdio. Point an MCP client at it. For Claude Code:

```bash
claude mcp add cs1 -- uv run --directory /path/to/cs1-mcp/server cs1-mcp
```

Or a raw client config entry:

```json
{
  "mcpServers": {
    "cs1": {
      "command": "uv",
      "args": ["run", "--directory", "/path/to/cs1-mcp/server", "cs1-mcp"]
    }
  }
}
```

## Config

| env var       | default     | meaning                          |
|---------------|-------------|----------------------------------|
| `CS1MCP_HOST` | `127.0.0.1` | bridge host                      |
| `CS1MCP_PORT` | `50545`     | bridge port (match the mod)      |

### Codex on Windows

Install `uv`, then register the server from PowerShell using the absolute path
to this checkout. `uv` will select a Python version supported by `pyproject.toml`
(3.10 or newer). Restart Codex after adding the server.

```powershell
codex mcp add cs1 -- uv run --directory 'G:\path\to\cities-skylines-mcp\server' cs1-mcp
codex mcp list
```

The bridge listens only on `127.0.0.1`. Start the game, enable **CS1 MCP Bridge**
in Content Manager, load a playable city, then call `ping`. If you change
`CS1MCP_PORT`, set the same value for the game and MCP server before launch.
Commands that alter the city act on the currently loaded save; test those on a
copy. A connection error during a write has an uncertain outcome, so inspect
the city before retrying.

## Smoke test

With CS1 running, the mod enabled, and a city loaded, call `ping` → `"pong"`.
If you get a connection error, the game isn't reachable — check the mod is
enabled and a city (not the main menu) is loaded.

To verify the actual stdio MCP handshake and read-only game calls, run
`python tests/stdio_smoke.py --output report.json` from `server/` after loading a
city. The report omits local paths and save names. Write tests require the
explicit `--allow-test-writes --test-x X --test-z Z --park-prefab NAME` flags and an isolated save;
inspect the source layout and confirm empty land before using them.
To test pause and day/night time separately, use `--allow-time-writes`; the test
leaves the isolated city paused. It checks the clocks and daylight hour after
two seconds each.
