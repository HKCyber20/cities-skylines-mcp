# CS1 MCP Bridge — wire protocol

The in-game mod listens on `127.0.0.1:50545` (override with the `CS1MCP_PORT`
env var). It speaks **newline-delimited JSON**: one request object per line in,
one response object per line out. This is the contract — any client in any
language can drive the game by speaking it; the MCP server is just one such client.

## Request

```json
{ "id": 1, "cmd": "set_sim_speed", "args": { "speed": 3, "paused": false } }
```

- `id` — optional; echoed back on the matching response so clients can correlate.
- `cmd` — command name (see below).
- `args` — command-specific object; omitted args fall back to documented defaults.

## Response

Success:
```json
{ "id": 1, "ok": true, "result": { "speed": 3, "paused": false } }
```

Failure:
```json
{ "id": 1, "ok": false, "error": "unknown command: foo" }
```

## Commands (v0)

| cmd                | thread | args                                              | result                              |
|--------------------|--------|---------------------------------------------------|-------------------------------------|
| `ping`             | —      | —                                                 | `"pong"`                            |
| `set_sim_speed`    | sim    | `speed` 0–3, `paused` bool                        | `{ speed, paused, effective_speed }` |
| `set_time_of_day`  | sim    | `hour` 0–23, `minute` 0–59                        | `{ hour, minute }`; error if day/night is disabled |
| `add_money`        | sim    | `amount` (whole units; negative removes)          | `{ added }`                         |
| `get_city_stats`   | sim    | —                                                 | `{ population, money, paused, speed, selected_speed, game_time, simulation_time, day_time_hour, day_night_enabled }` |
| `set_weather`      | sim    | `rain` 0–1, `fog` 0–1 (both optional)             | `{ rain, fog }`                     |
| `set_camera`       | main   | `x`, `z`, `angle_x`, `angle_y`, `zoom`            | `{ x, z, zoom }`                    |
| `get_camera`       | main   | —                                                 | `{ x, z, angle_x, angle_y, zoom }`  |
| `fly_to`           | main   | `x`, `z`, `angle_x`, `angle_y`, `zoom`, `seconds` | `{ x, z, zoom, seconds }`           |
| `follow_instance`  | main   | `id` (0 clears), `kind` (vehicle/building/citizen)| `{ following, id, kind }`           |
| `hide_ui`          | main   | `hidden` (bool, default true)                     | `{ hidden }`                        |
| `screenshot`       | main   | `path` (optional)                                 | `{ path }`                          |
| `set_info_view`    | main   | `mode` (`None`/`Traffic`/`Pollution`/…)           | `{ mode }`                          |
| `find_buildings`   | sim    | `filter` (optional), `limit` (default 50)         | `{ count, buildings[] }`            |
| `bulldoze_building`| sim    | `id`                                              | `{ id, released }`                  |
| `place_building`   | sim    | `building`, `x`, `z`, `angle` (deg)               | `{ building, id, x, z }`            |
| `place_road`       | sim    | `start_x/z`, `end_x/z`, `road`, optional `start_height`, `end_height` (terrain-relative meters), `start_node`, `end_node`, `mode` (auto/ground/elevated/bridge/slope/tunnel), `middle_x/z` | `{ road, segment, segments[], segment_details[], start_node, end_node, nodes, created_nodes[], curved }` |
| `place_path`       | sim    | `points` (list of `[x,z]` or `[x,z,height]`, 2–64), `road`, optional `modes[]` (one required variant per leg), `start_node`, `end_node` | `{ road, nodes, segments[], segment_details[], created_nodes[], start_node, end_node }` |
| `inspect_network`  | sim    | `node_ids[]`, `segment_ids[]`, optional `x,z` for purchased-area check and `radius` (0..100m) for nearby endpoint discovery | `{ nodes[], segments[], point?, nearby_endpoints? }` |
| `list_prefabs`     | sim    | `kind` (road/road_variants/building), `filter`, `limit` | `{ count, prefabs[] }` |

**Thread** indicates which game thread the command runs on internally (sim state
vs. render/camera). Clients don't need to care — it's handled by the bridge — but
it explains why a few commands (camera, screenshot) only take effect on frame
boundaries.

`place_road` / `place_building` take a prefab name (substring-matched against the
loaded prefabs) — use `list_prefabs` to discover the names available in your game,
since DLC and mods change the set.

For bridges and tunnels, pass a **base RoadAI road** such as one returned by
`list_prefabs(kind="road_variants")`. Its returned variant map reflects what is
actually loaded; variant names are selected by the game's native NetTool from
the supplied terrain-relative heights. Positive heights create elevated/bridge
segments, and negative heights create slopes/tunnels. A full tunnel needs
surface → negative → negative → surface points; a bridge needs a raised span
long enough for that road's bridge variant. `mode`/`modes` require the actual
selected segment type and fail if it is unavailable. Existing connections
require explicit endpoint IDs and matching coordinates; only an endpoint with
one current segment can be used. Near-collinear joins to a moveable existing
endpoint are refused because CS1's native tool can replace the old road there;
the caller may use a slight-angle approach. The optional `inspect_network(x,z)` area check
reports whether a point lies outside the purchased area. Ground-only calls
without new fields remain supported, including non-RoadAI path prefabs.

Long entrance ramps may contain ground and elevated/slope subsegments after
the native tool splits them. Use `auto` for those legs and inspect every
returned segment; require `bridge` or `tunnel` on a pure high or deep span.

Version 0.2.1 also recognizes a loaded **standalone RoadBridgeAI road network**
that is not referenced as a variant by another loaded RoadAI base. In
`list_prefabs(kind="road_variants")` it has `standalone_bridge: true`, its own
name as `bridge`, and empty `ground`, `elevated`, `slope`, and `tunnel` fields.
Use the exact standalone name with `mode="bridge"` or `auto`, and ensure
**every** endpoint is above terrain. A newly created point needs a positive
height argument; an explicit existing node uses its actual stored positive
elevation, so the caller need not repeat that height. `inspect_network` reports `standalone_bridge` on queried nodes and
segments. A separate ground/elevated approach can be built from a normal road
and joined by explicit endpoint ID. These networks do not gain fabricated
ground or tunnel variants.

Some RoadAI bases set `m_useFixedHeight`. For those, CS1's NetTool skips the
parent `GetInfo` variant selection. The bridge now asks the **parent RoadAI**
to select a real loaded child for each leg and passes that child to the same
native test/build path. `road_variants` reports `fixed_height: true` and
`variant_selection: "parent_get_info_then_native_child"` for this case;
ordinary bases report `"native_get_info"`. The caller still supplies only
the parent road name. An explicit `mode="bridge"` succeeds only if the actual
created segment uses the parent's real bridge prefab. Geometry and collision
can still be rejected by the native tool.

## Quick manual test

```bash
# with a city loaded:
printf '{"id":1,"cmd":"ping"}\n' | nc 127.0.0.1 50545
# -> {"id":1,"ok":true,"result":"pong"}
```
