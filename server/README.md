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

See [PROTOCOL.md](../PROTOCOL.md) for the full command set.

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
