"""Read-only CS1 MCP smoke test, with opt-in writes for an isolated test save.

Run from server/: python tests/stdio_smoke.py --output report.json
Writes require --allow-test-writes --test-x X --test-z Z after inspecting empty land.
"""

import argparse
import asyncio
import json
import os
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client


def unpack(result):
    """Extract the value returned by FastMCP without storing protocol noise."""
    if result.isError:
        raise RuntimeError("MCP tool error: " + "; ".join(getattr(c, "text", "") for c in result.content))
    if result.structuredContent is not None:
        value = result.structuredContent
        if isinstance(value, dict) and set(value) == {"result"}:
            return value["result"]
        return value
    texts = [c.text for c in result.content if hasattr(c, "text")]
    if len(texts) == 1:
        try:
            return json.loads(texts[0])
        except ValueError:
            return texts[0]
    return texts


async def run(options):
    server_dir = Path(__file__).resolve().parents[1]
    params = StdioServerParameters(
        command=sys.executable,
        args=["-m", "cs1_mcp.server"],
        cwd=server_dir,
        env=dict(os.environ),
    )
    report = {
        "scope": "caller-confirmed test copy",
        "timestamp_utc": datetime.now(timezone.utc).isoformat(),
        "mode": "test-writes" if options.allow_test_writes else
                "time-writes" if options.allow_time_writes else "read-only",
        "checks": {},
        "writes": [],
    }
    failure = None
    async with stdio_client(params) as (read, write):
        async with ClientSession(read, write, read_timeout_seconds=timedelta(seconds=20)) as session:
            initialized = await asyncio.wait_for(session.initialize(), 25)
            tools = await asyncio.wait_for(session.list_tools(), 25)
            names = {tool.name for tool in tools.tools}
            required = {"ping", "get_city_stats", "get_camera", "list_prefabs", "find_buildings",
                        "place_road", "place_path", "place_building", "bulldoze_building"}
            missing = required - names
            if missing:
                raise RuntimeError(f"Missing MCP tools: {sorted(missing)}")
            report["server"] = {"name": initialized.serverInfo.name,
                                "version": initialized.serverInfo.version, "tool_count": len(names)}

            async def call(name, **arguments):
                result = await asyncio.wait_for(session.call_tool(name, arguments), 30)
                try:
                    return unpack(result)
                except RuntimeError as exc:
                    raise RuntimeError(f"{name}: {exc}") from exc

            try:
                report["checks"]["ping"] = await call("ping")
                report["checks"]["city_stats"] = await call("get_city_stats")
                report["checks"]["camera"] = await call("get_camera")
                roads = await call("list_prefabs", kind="road", filter="Basic Road", limit=10)
                parks = await call("list_prefabs", kind="building", filter="Park", limit=20)
                report["checks"]["road_prefabs"] = roads
                report["checks"]["park_prefabs"] = parks
                buildings = await call("find_buildings", limit=5)
                report["checks"]["buildings"] = {
                    "count": buildings.get("count"),
                    "ids": [item.get("id") for item in buildings.get("buildings", [])],
                }

                if options.allow_time_writes:
                    initial = report["checks"]["city_stats"]
                    selected_speed = int(initial["selected_speed"])
                    report["time_control"] = {"target_hour": options.target_hour}
                    report["time_control"]["pause_response"] = await call(
                        "set_sim_speed", speed=selected_speed, paused=True)
                    await asyncio.sleep(0.5)  # let any already queued frame finish
                    before = await call("get_city_stats")
                    await asyncio.sleep(2)
                    after = await call("get_city_stats")
                    game_delta = (datetime.fromisoformat(after["game_time"]) -
                                  datetime.fromisoformat(before["game_time"])).total_seconds()
                    report["time_control"]["pause_check"] = {
                        "game_time_delta_seconds": game_delta,
                        "simulation_time_unchanged": before["simulation_time"] == after["simulation_time"],
                        "effective_speed": after["speed"],
                    }
                    if after["speed"] != 0 or abs(game_delta) > 1 or before["simulation_time"] != after["simulation_time"]:
                        raise RuntimeError("pause did not hold both game clocks for two seconds")
                    if not after["day_night_enabled"]:
                        raise RuntimeError("time-of-day test unsupported: day/night cycle is disabled")
                    report["time_control"]["time_response"] = await call(
                        "set_time_of_day", hour=options.target_hour, minute=0)
                    await asyncio.sleep(2)
                    observed = await call("get_city_stats")
                    observed_hour = float(observed["day_time_hour"])
                    hour_error = abs((observed_hour - options.target_hour + 12) % 24 - 12)
                    report["time_control"]["day_time_check"] = {
                        "observed_hour": observed_hour, "hour_error": hour_error,
                    }
                    if hour_error > 0.25:
                        raise RuntimeError("day/night hour did not remain near the requested target")

                if options.allow_test_writes:
                    x, z = options.test_x, options.test_z
                    road = options.road_prefab
                    park = options.park_prefab
                    if road not in roads.get("prefabs", []):
                        raise ValueError(f"Road prefab {road!r} not listed by the game")
                    if park not in parks.get("prefabs", []):
                        raise ValueError(f"Park prefab {park!r} not listed by the game")

                    # Separate short test features within the caller-confirmed empty area.
                    result = await call("place_road", start_x=x, start_z=z, end_x=x + 48,
                                        end_z=z, road=road)
                    report["writes"].append({"tool": "place_road", "result": result})
                    if int(result.get("segment", 0)) <= 0:
                        raise RuntimeError("place_road returned no segment ID")
                    result = await call("place_path", points=[[x, z + 80], [x + 32, z + 80],
                                                               [x + 64, z + 80]], road=road)
                    report["writes"].append({"tool": "place_path", "result": result})
                    if len(result.get("segments", [])) != 2 or any(int(s) <= 0 for s in result["segments"]):
                        raise RuntimeError("place_path did not return two valid segment IDs")
                    result = await call("place_building", building=park, x=x + 112, z=z + 40)
                    report["writes"].append({"tool": "place_building", "result": result})
                    park_id = int(result.get("id", 0))
                    if park_id <= 0:
                        raise RuntimeError("place_building returned no building ID")
                    observed = await call("find_buildings", limit=65535)
                    matched = [item for item in observed.get("buildings", []) if int(item.get("id", 0)) == park_id]
                    report["checks"]["new_park_found"] = bool(matched)
                    if not matched:
                        raise RuntimeError("new park ID was not found by find_buildings")
                    if options.cleanup_park:
                        removed = await call("bulldoze_building", id=park_id)
                        report["writes"].append({"tool": "bulldoze_building", "result": removed})
                        after_cleanup = await call("find_buildings", limit=65535)
                        still_present = any(int(item.get("id", 0)) == park_id for item in after_cleanup.get("buildings", []))
                        report["checks"]["new_park_removed"] = not still_present
                        if still_present:
                            raise RuntimeError("new park ID still exists after bulldoze_building")
            except (RuntimeError, ValueError, TimeoutError) as exc:
                failure = str(exc)

    if failure:
        report["error"] = failure
    if options.output:
        output = Path(options.output)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 1 if failure else 0


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", help="Write a sanitized JSON report to this file")
    parser.add_argument("--allow-test-writes", action="store_true", help="Enable writes to an isolated test copy")
    parser.add_argument("--allow-time-writes", action="store_true", help="Pause the test copy and set its day/night hour; leaves it paused")
    parser.add_argument("--target-hour", type=int, default=12, choices=range(24), metavar="0..23")
    parser.add_argument("--test-x", type=float, help="Confirmed empty-land origin X for writes")
    parser.add_argument("--test-z", type=float, help="Confirmed empty-land origin Z for writes")
    parser.add_argument("--road-prefab", default="Basic Road")
    parser.add_argument("--park-prefab", help="Exact park prefab name confirmed by read-only discovery")
    parser.add_argument("--cleanup-park", action="store_true", help="Demolish only the park created by this run")
    options = parser.parse_args()
    if options.allow_test_writes and (options.test_x is None or options.test_z is None or not options.park_prefab):
        parser.error("--allow-test-writes requires --test-x, --test-z, and --park-prefab")
    if options.cleanup_park and not options.allow_test_writes:
        parser.error("--cleanup-park requires --allow-test-writes")
    sys.exit(asyncio.run(run(options)))
