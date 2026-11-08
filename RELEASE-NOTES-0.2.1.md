# CS1 MCP Bridge 0.2.1 — Standalone road bridges

This update extends the 0.2.0 native road builder to standalone road networks
whose actual AI type is `RoadBridgeAI`. It requires a loaded road-service
prefab that is not already a child variant of a regular `RoadAI` base. Such a
bridge is placed only with positive terrain-relative heights and `auto` or
`bridge` mode. A normal road can provide an elevated approach; the existing
node's actual height is used when joining by node ID.

For fixed-height `RoadAI` bases, the bridge now asks the parent AI to select
one of its real, loaded variants for each leg before calling the native road
tool. Explicit `bridge` mode still requires the built segments to use the
parent's bridge prefab. `list_prefabs(kind="road_variants")` exposes standalone
bridge status and fixed-height variant selection; `inspect_network` identifies
standalone bridge nodes and segments.

The existing 0.2.0 limits remain: no automatic snapping to an existing road,
near-collinear joins to a moveable old endpoint are refused, and native
geometry/collision checks can reject a build. This release does not add zoning
or save tools.

Verified on CS1 1.21.1-f9 in an isolated copy through the stdio MCP server:
Ponte Suebia stone arch, Four-Lane Elevated Brick Road v2 brick arch, Wiwi
steel truss and Massongex 2-way steel tied arch all produced active bridge
segments joined to new elevated approaches. `inspect_network` reported real
bridge prefabs, continuous endpoint IDs and deck nodes 28 m above terrain.
Massongex used its standalone RoadBridgeAI network; the other assets used
their RoadAI parent and real bridge child.

The original Basic Road bridge and tunnel profiles still passed on this DLL:
the tunnel path produced ground/slope/tunnel segments and the bridge path
produced ground/elevated/bridge segments, each joined to a new approach.
Five invalid requests were refused in an isolated test: standalone ground,
standalone tunnel, zero-height standalone bridge, direct use of a parent's
bridge child, and a fixed-height parent asked for ground where its AI selected
bridge. Selected pre-existing nodes were unchanged before and after these
refusals; this is not a claim of full-city rollback.

The tested Wiwi asset shows its truss at close range but not at distant zoom;
the roadway remains visible. This is a third-party model LOD limitation, not
fixed by the MCP bridge. The Massongex Workshop package contains two specs
with a shared pillar prefab; only the 2-way spec was kept enabled and tested
after a duplicate-prefab warning. This release does not automatically repair
or enable both specs. Construction/readback does not prove vehicle traffic.

The Windows ZIP contains only this project's DLL, portable installation guide
and the upstream MIT license. No third-party Workshop assets, game assemblies,
saves, screenshots or account files are included. Third-party bridge assets
must be obtained through their own creators' Workshop pages.

Original project: [jacklenzotti/cities-skylines-mcp](https://github.com/jacklenzotti/cities-skylines-mcp)
by Jack Lenzotti.
