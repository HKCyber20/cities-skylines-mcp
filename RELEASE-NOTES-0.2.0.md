# CS1 MCP Bridge 0.2.0 — Bridges and tunnels

This update adds terrain-relative heights to `place_road` and `place_path`.
The in-game bridge uses Cities: Skylines 1's native road tool to select loaded
ground, elevated, bridge, slope and tunnel variants, including intermediate
nodes and attached structures. Existing endpoint IDs can be connected when
the native tool can preserve the prior road; nearly straight joins that could
replace an old endpoint are refused.

`list_prefabs(kind="road_variants")` reports each loaded base road's available
variants. `inspect_network` reads actual node height, terrain offset, flags,
attached structure IDs, segment prefab and endpoint IDs. It can also check
whether a proposed point is outside the purchased area.

Verified in an isolated CS1 1.21.1-f9 test save through the stdio MCP server:

- Basic Road: straight tunnel with `Underground` nodes 12 m below terrain;
  straight bridge with deck nodes 28 m above terrain.
- Medium Road, Large Road and Highway: ground approaches share endpoint IDs
  with the new bridge/tunnel paths. Actual slope/tunnel/elevated/bridge segment
  prefabs and a continuous chain were read back. Large Road and Highway bridge
  spans also produced `Double` nodes.
- Legacy ground calls also passed: a loaded `Pedestrian Pavement` prefab made
  one ground segment, and a two-coordinate Basic Road path made two. A nearly
  straight join to an existing endpoint was refused with the old node and
  adjacent segment unchanged. An induced invalid later leg returned an error;
  the queried pre-existing endpoint and adjacent segments remained unchanged.

These checks verify construction and network metadata. They do not establish
vehicle traffic behavior or full rollback of arbitrary terrain, trees, modded
assets or city state. Use a copy of a save for writes. The release does not add
zoning or save tools.

The Windows ZIP contains only this project's mod DLL, a portable install
guide and the upstream MIT license. The Python MCP server is available in the
source repository. The original project is
[jacklenzotti/cities-skylines-mcp](https://github.com/jacklenzotti/cities-skylines-mcp)
by Jack Lenzotti.
