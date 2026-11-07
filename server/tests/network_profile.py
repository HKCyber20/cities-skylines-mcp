"""Real MCP bridge/tunnel profile on a caller-confirmed isolated test copy.

Default: read-only variant and purchased-area probe. Writes require
--allow-test-writes --x X --z Z --road BASE. Both profiles need roughly
750m east-west by (row-gap + 50m) north-south of clear, purchased land.
"""

import argparse
import asyncio
import json
import os
import sys
from datetime import timedelta
from pathlib import Path

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

from stdio_smoke import unpack


async def run(options):
    server_dir = Path(__file__).resolve().parents[1]
    params = StdioServerParameters(command=sys.executable, args=["-m", "cs1_mcp.server"],
                                   cwd=server_dir, env=dict(os.environ))
    report = {"scope": "caller-confirmed isolated test copy",
              "mode": "test-writes" if options.allow_test_writes else "read-only",
              "road": options.road, "selected_profile": options.profile,
              "row_gap": options.row_gap,
              "area_probes": [], "profiles": []}
    failure = None
    async with stdio_client(params) as (read, write):
        async with ClientSession(read, write, read_timeout_seconds=timedelta(seconds=30)) as session:
            initialized = await session.initialize()
            listed = await session.list_tools()
            names = {t.name for t in listed.tools}
            if not {"list_prefabs", "inspect_network", "place_road", "place_path"} <= names:
                raise RuntimeError("required network MCP tools are missing")
            report["server"] = {"name": initialized.serverInfo.name,
                                "version": initialized.serverInfo.version, "tool_count": len(names)}

            async def call(name, **args):
                return unpack(await asyncio.wait_for(session.call_tool(name, args), 45))

            try:
                variants = await call("list_prefabs", kind="road_variants", filter=options.road, limit=50)
                matches = [item for item in variants["prefabs"] if item["road"] == options.road]
                if not matches:
                    raise RuntimeError("base RoadAI prefab not loaded: " + options.road)
                report["variants"] = matches[0]
                if options.x is not None and options.z is not None:
                    # Probe both corridor endpoints and high/deep spans before
                    # any write. Callers must also inspect the view for obstacles.
                    gap = options.row_gap
                    tunnel_probes = [(0, 0), (40, 0), (150, 0), (190, 16),
                                     (270, 0), (310, 8), (420, 0), (460, 16)]
                    bridge_probes = [(0, gap), (40, gap), (200, gap),
                                     (240, gap + 16), (280, gap), (320, gap + 8),
                                     (400, gap), (440, gap + 16), (480, gap),
                                     (520, gap + 8), (680, gap), (720, gap + 16)]
                    probes = ((tunnel_probes if options.profile in ("both", "tunnel") else []) +
                              (bridge_probes if options.profile in ("both", "bridge") else []))
                    for dx, dz in probes:
                        point = (await call("inspect_network", x=options.x + dx,
                                            z=options.z + dz))["point"]
                        report["area_probes"].append(point)
                    if options.allow_test_writes and any(p["out_of_area"] for p in report["area_probes"]):
                        raise RuntimeError("profile contains points outside purchased area")

                if options.allow_test_writes:
                    if not all(report["variants"].get(key) for key in
                               ("ground", "elevated", "bridge", "slope", "tunnel")):
                        raise RuntimeError("selected road lacks one or more required loaded variants")

                    async def profile(name, base_z, waypoints, modes, expected, connected=True):
                        x, z = options.x, options.z + base_z
                        approach, entry_id = None, None
                        if connected:
                            approach = await call("place_road", road=options.road,
                                                  start_x=x, start_z=z, end_x=x + 40,
                                                  end_z=z, mode="ground")
                            entry_id = int(approach["end_node"])
                            entry = await call("inspect_network", node_ids=[entry_id])
                            entry_node = entry["nodes"][0]
                            first = [entry_node["x"], entry_node["z"], 0]
                        else:
                            first = [x, z, 0]
                        points = ([first] + [[x + dx, z + dz, h] for dx, dz, h in waypoints])
                        arguments = {"road": options.road, "points": points, "modes": modes}
                        if connected:
                            arguments["start_node"] = entry_id
                        built = await call("place_path", **arguments)
                        details = await call("inspect_network",
                                             node_ids=built["created_nodes"] + ([entry_id] if connected else []),
                                             segment_ids=built["segments"] +
                                                         (approach["segments"] if connected else []))
                        actual = {item["variant"] for item in built["segment_details"]}
                        if not expected <= actual:
                            raise RuntimeError(name + " did not create expected variants: " + repr(actual))
                        joined = any(int(s["start_node"]) == entry_id or
                                     int(s["end_node"]) == entry_id for s in details["segments"]
                                     if int(s["id"]) in built["segments"]) if connected else True
                        if not joined:
                            raise RuntimeError(name + " did not connect to existing approach endpoint")
                        if "tunnel" in name and not any(n["height_above_terrain"] < -8 and
                                                       "Underground" in n["flags"] for n in details["nodes"]):
                            raise RuntimeError("no underground node with real negative terrain offset")
                        if "bridge" in name and not any(n["height_above_terrain"] > 25 and
                                                       int(n["elevation"]) > 0 for n in details["nodes"]):
                            raise RuntimeError("no elevated node above 25m")
                        report["profiles"].append({"name": name, "approach": approach,
                                                   "path": built, "inspection": details})

                    # Tunnel: 8% entrance → deep tunnel → 8% exit.
                    # Bridge: 10% elevated ramps, 28m bridge transition/span.
                    if options.straight_only:
                        if options.profile in ("both", "tunnel"):
                            await profile("straight_tunnel", 0,
                                          [(150, 0, -12), (270, 0, -12), (420, 0, 0)],
                                          ["auto", "tunnel", "auto"], {"slope", "tunnel"}, False)
                        if options.profile in ("both", "bridge"):
                            await profile("straight_bridge", options.row_gap,
                                          [(200, 0, 20), (280, 0, 28), (400, 0, 28),
                                           (480, 0, 20), (680, 0, 0)],
                                          ["auto", "auto", "bridge", "auto", "auto"],
                                          {"elevated", "bridge"}, False)
                    else:
                        if options.profile in ("both", "tunnel"):
                            await profile("tunnel", 0,
                                          [(190, 16, -12), (310, 8, -12), (460, 16, 0)],
                                          ["auto", "tunnel", "auto"], {"slope", "tunnel"})
                        if options.profile in ("both", "bridge"):
                            await profile("bridge", options.row_gap,
                                          [(240, 16, 20), (320, 8, 28), (440, 16, 28),
                                           (520, 8, 20), (720, 16, 0)],
                                          ["auto", "auto", "bridge", "auto", "auto"],
                                          {"elevated", "bridge"})
            except (RuntimeError, ValueError, TimeoutError, KeyError, IndexError) as exc:
                failure = str(exc)
    if failure:
        report["error"] = failure
    if options.output:
        path = Path(options.output)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 1 if failure else 0


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--road", default="Basic Road", help="Exact loaded base RoadAI prefab")
    parser.add_argument("--x", type=float, help="West edge of clear test area")
    parser.add_argument("--z", type=float, help="South edge of clear test area")
    parser.add_argument("--output", help="Sanitized JSON result file")
    parser.add_argument("--allow-test-writes", action="store_true")
    parser.add_argument("--straight-only", action="store_true", help="Test straight bridge/tunnel without existing-road joins")
    parser.add_argument("--profile", choices=("both", "tunnel", "bridge"), default="both",
                        help="Select one or both construction profiles")
    parser.add_argument("--row-gap", type=float, default=220.0,
                        help="Z distance from tunnel row to bridge row, 80..1000m (default 220)")
    options = parser.parse_args()
    if (options.x is None) != (options.z is None):
        parser.error("provide both --x and --z")
    if options.allow_test_writes and options.x is None:
        parser.error("writes require --x and --z")
    if not 80 <= options.row_gap <= 1000:
        parser.error("--row-gap must be within 80..1000m")
    sys.exit(asyncio.run(run(options)))
