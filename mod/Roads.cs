using System;
using System.Collections.Generic;
using ColossalFramework;
using UnityEngine;

namespace CS1McpBridge
{
    // Uses the game's own NetTool path so elevation, transition prefabs and
    // underground node flags follow the same rules as the in-game road tool.
    internal static class Roads
    {
        sealed class Snapshot
        {
            public bool[] Nodes;
            public bool[] Segments;
            public bool[] Buildings;
        }

        sealed class ExistingEndpoint
        {
            public ushort Id, Segment, SegmentStart, SegmentEnd;
            public Vector3 Position;
            public Vector3 SegmentStartDirection, SegmentEndDirection;
            public byte Elevation;
            public NetNode.Flags Flags;
        }

        static JSONNode Obj(params object[] values)
        {
            var result = new JSONObject();
            for (int i = 0; i < values.Length; i += 2)
            {
                object value = values[i + 1];
                string key = (string)values[i];
                if (value is int) result[key] = (int)value;
                else if (value is float) result[key] = (float)value;
                else if (value is bool) result[key] = (bool)value;
                else if (value is JSONNode) result[key] = (JSONNode)value;
                else result[key] = value == null ? "" : value.ToString();
            }
            return result;
        }

        // The Vector3 overload converts world coordinates into terrain-grid
        // coordinates and raw height units into world meters. The float,float
        // overload expects grid coordinates and returns unscaled raw values.
        static float Terrain(float x, float z) => Singleton<TerrainManager>.instance.SampleRawHeightSmooth(new Vector3(x, 0f, z));

        static void CheckFinite(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException(name + " must be finite");
        }

        static NetInfo BaseRoad(string name)
        {
            NetInfo info = Commands.FindNet(name);
            if (info == null) throw new ArgumentException("road prefab not found: " + name);
            if (info.m_netAI is RoadBridgeAI)
            {
                if (!IsStandaloneBridge(info, ReferencedRoadVariants()))
                    throw new NotSupportedException("this bridge is a variant of another road or is not a road; " +
                        "choose its RoadAI base from list_prefabs kind=road_variants");
            }
            return info;
        }

        static HashSet<NetInfo> ReferencedRoadVariants()
        {
            var referenced = new HashSet<NetInfo>();
            int count = PrefabCollection<NetInfo>.LoadedCount();
            for (int i = 0; i < count; i++)
            {
                var info = PrefabCollection<NetInfo>.GetLoaded((uint)i);
                var ai = info == null ? null : info.m_netAI as RoadAI;
                if (ai == null) continue;
                if (ai.m_elevatedInfo != null) referenced.Add(ai.m_elevatedInfo);
                if (ai.m_bridgeInfo != null) referenced.Add(ai.m_bridgeInfo);
                if (ai.m_slopeInfo != null) referenced.Add(ai.m_slopeInfo);
                if (ai.m_tunnelInfo != null) referenced.Add(ai.m_tunnelInfo);
                if (ai.m_connectedInfo != null) referenced.Add(ai.m_connectedInfo);
                if (ai.m_connectedElevatedInfo != null) referenced.Add(ai.m_connectedElevatedInfo);
            }
            return referenced;
        }

        static bool IsStandaloneBridge(NetInfo info, HashSet<NetInfo> referenced)
        {
            return info != null && info.m_netAI is RoadBridgeAI &&
                info.m_class != null && info.m_class.m_service == ItemClass.Service.Road &&
                !referenced.Contains(info);
        }

        static NetTool.ControlPoint Point(float x, float z, float height, int existingId)
        {
            CheckFinite(x, "x"); CheckFinite(z, "z"); CheckFinite(height, "height");
            var nm = Singleton<NetManager>.instance;
            var point = new NetTool.ControlPoint();
            if (existingId != 0)
            {
                if (existingId < 1 || existingId >= nm.m_nodes.m_buffer.Length)
                    throw new ArgumentException("existing node id out of range");
                ref NetNode node = ref nm.m_nodes.m_buffer[existingId];
                if ((node.m_flags & NetNode.Flags.Created) == 0 || (node.m_flags & NetNode.Flags.Deleted) != 0)
                    throw new ArgumentException("existing node is not active: " + existingId);
                if (node.Info == null) throw new ArgumentException("existing node has no prefab: " + existingId);
                if ((node.m_position - new Vector3(x, node.m_position.y, z)).sqrMagnitude > 0.0025f)
                    throw new ArgumentException("existing node x/z must match within 0.05m: " + existingId);
                if (node.CountSegments() != 1)
                    throw new ArgumentException("only an existing road endpoint with one segment can be connected: " + existingId);
                float actualElevation = node.Info.m_netAI.IsUnderground() ? -node.m_elevation : node.m_elevation;
                if (Mathf.Abs(height) > 0.1f && Mathf.Abs(height - actualElevation) > 0.5f)
                    throw new ArgumentException("supplied height disagrees with existing node elevation: " + existingId);
                point.m_node = (ushort)existingId;
                point.m_position = node.m_position;
                point.m_elevation = actualElevation;
            }
            else
            {
                if (height < -255f || height > 255f) throw new ArgumentException("height must be within -255..255m");
                point.m_position = new Vector3(x, Terrain(x, z) + height, z);
                point.m_elevation = height;
            }
            return point;
        }

        static Snapshot Capture(NetManager nm)
        {
            var snap = new Snapshot {
                Nodes = new bool[nm.m_nodes.m_buffer.Length],
                Segments = new bool[nm.m_segments.m_buffer.Length],
                Buildings = new bool[Singleton<BuildingManager>.instance.m_buildings.m_buffer.Length]
            };
            for (int i = 1; i < snap.Nodes.Length; i++)
                snap.Nodes[i] = (nm.m_nodes.m_buffer[i].m_flags & NetNode.Flags.Created) != 0;
            for (int i = 1; i < snap.Segments.Length; i++)
                snap.Segments[i] = (nm.m_segments.m_buffer[i].m_flags & NetSegment.Flags.Created) != 0;
            var buildings = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
            for (int i = 1; i < snap.Buildings.Length; i++)
                snap.Buildings[i] = (buildings[i].m_flags & Building.Flags.Created) != 0;
            return snap;
        }

        static List<ushort> NewNodes(NetManager nm, Snapshot snap)
        {
            var result = new List<ushort>();
            for (int i = 1; i < snap.Nodes.Length; i++)
                if (!snap.Nodes[i] && (nm.m_nodes.m_buffer[i].m_flags & NetNode.Flags.Created) != 0)
                    result.Add((ushort)i);
            return result;
        }

        static List<ushort> NewSegments(NetManager nm, Snapshot snap)
        {
            var result = new List<ushort>();
            for (int i = 1; i < snap.Segments.Length; i++)
                if (!snap.Segments[i] && (nm.m_segments.m_buffer[i].m_flags & NetSegment.Flags.Created) != 0)
                    result.Add((ushort)i);
            return result;
        }

        static void RollBack(NetManager nm, Snapshot snap)
        {
            foreach (ushort id in NewSegments(nm, snap)) nm.ReleaseSegment(id, true);
            foreach (ushort id in NewNodes(nm, snap)) nm.ReleaseNode(id);
            var buildings = Singleton<BuildingManager>.instance;
            for (int i = 1; i < snap.Buildings.Length; i++)
                if (!snap.Buildings[i] && (buildings.m_buildings.m_buffer[i].m_flags & Building.Flags.Created) != 0)
                    buildings.ReleaseBuilding((ushort)i);
        }

        static ExistingEndpoint Remember(NetManager nm, NetTool.ControlPoint point)
        {
            if (point.m_node == 0) return null;
            ushort id = point.m_node;
            var node = nm.m_nodes.m_buffer[id];
            ushort segmentId = 0;
            for (int i = 0; i < 8; i++)
                if (node.GetSegment(i) != 0) { segmentId = node.GetSegment(i); break; }
            if (segmentId == 0) throw new ArgumentException("existing endpoint has no adjacent segment");
            var segment = nm.m_segments.m_buffer[segmentId];
            return new ExistingEndpoint { Id = id, Position = node.m_position,
                Elevation = node.m_elevation, Flags = node.m_flags, Segment = segmentId,
                SegmentStart = segment.m_startNode, SegmentEnd = segment.m_endNode,
                SegmentStartDirection = segment.m_startDirection,
                SegmentEndDirection = segment.m_endDirection };
        }

        static void CheckExisting(NetManager nm, ExistingEndpoint state)
        {
            if (state == null) return;
            var node = nm.m_nodes.m_buffer[state.Id];
            var segment = nm.m_segments.m_buffer[state.Segment];
            if ((node.m_flags & NetNode.Flags.Created) == 0 ||
                (node.m_position - state.Position).sqrMagnitude > 0.0001f ||
                node.m_elevation != state.Elevation ||
                (segment.m_flags & NetSegment.Flags.Created) == 0 ||
                segment.m_startNode != state.SegmentStart || segment.m_endNode != state.SegmentEnd ||
                (segment.m_startDirection - state.SegmentStartDirection).sqrMagnitude > 0.0001f ||
                (segment.m_endDirection - state.SegmentEndDirection).sqrMagnitude > 0.0001f)
                throw new InvalidOperationException("native road tool changed an existing endpoint or adjacent segment");
        }

        static void RefuseUnsafeStraightMerge(NetManager nm, ExistingEndpoint state, Vector3 newDirection)
        {
            if (state == null) return;
            // NetTool.TryMoveNode calls MoveMiddleNode (which releases the old
            // segment/node) for a moveable, straight endpoint when the new
            // direction is almost collinear. Keep existing topology untouched.
            if ((state.Flags & (NetNode.Flags.End | NetNode.Flags.Moveable | NetNode.Flags.Untouchable)) !=
                (NetNode.Flags.End | NetNode.Flags.Moveable)) return;
            var old = nm.m_segments.m_buffer[state.Segment];
            var a = nm.m_nodes.m_buffer[old.m_startNode].m_position;
            var b = nm.m_nodes.m_buffer[old.m_endNode].m_position;
            if (!NetSegment.IsStraight(a, old.m_startDirection, b, old.m_endDirection)) return;
            Vector3 oldDirection = old.m_startNode == state.Id ? old.m_startDirection : old.m_endDirection;
            oldDirection.y = 0f; newDirection.y = 0f;
            if (oldDirection.sqrMagnitude < 0.01f || newDirection.sqrMagnitude < 0.01f) return;
            if (Mathf.Abs(Vector3.Dot(oldDirection.normalized, newDirection.normalized)) > 0.998f)
                throw new NotSupportedException("straight merge would replace an existing road endpoint; " +
                    "connect at a slight angle or use a separate ground approach");
        }

        static void RestoreExisting(NetManager nm, ExistingEndpoint state)
        {
            if (state == null) return;
            ref NetNode node = ref nm.m_nodes.m_buffer[state.Id];
            if ((node.m_flags & NetNode.Flags.Created) != 0)
            {
                if ((node.m_position - state.Position).sqrMagnitude > 0.0001f)
                    nm.MoveNode(state.Id, state.Position);
                node.m_elevation = state.Elevation;
                node.m_flags = state.Flags;
                nm.UpdateNode(state.Id);
            }
            ref NetSegment segment = ref nm.m_segments.m_buffer[state.Segment];
            if ((segment.m_flags & NetSegment.Flags.Created) != 0)
            {
                segment.m_startNode = state.SegmentStart;
                segment.m_endNode = state.SegmentEnd;
                segment.m_startDirection = state.SegmentStartDirection;
                segment.m_endDirection = state.SegmentEndDirection;
                nm.UpdateSegment(state.Segment);
            }
        }

        static string Variant(RoadAI ai, NetInfo info)
        {
            if (ai == null) return info != null && info.m_netAI is RoadBridgeAI ? "bridge" : "ground";
            if (info == ai.m_bridgeInfo && info != null) return "bridge";
            if (info == ai.m_tunnelInfo && info != null) return "tunnel";
            if (info == ai.m_slopeInfo && info != null) return "slope";
            if (info == ai.m_elevatedInfo && info != null) return "elevated";
            return "ground";
        }

        static bool IsLoaded(NetInfo info)
        {
            if (info == null) return false;
            int count = PrefabCollection<NetInfo>.LoadedCount();
            for (int i = 0; i < count; i++)
                if (PrefabCollection<NetInfo>.GetLoaded((uint)i) == info) return true;
            return false;
        }

        static bool IsParentInfoOrVariant(RoadAI ai, NetInfo baseInfo, NetInfo selected)
        {
            return selected == baseInfo || selected == ai.m_elevatedInfo ||
                selected == ai.m_bridgeInfo || selected == ai.m_slopeInfo ||
                selected == ai.m_tunnelInfo || selected == ai.m_connectedInfo ||
                selected == ai.m_connectedElevatedInfo;
        }

        static NetInfo RequireMode(string mode, RoadAI ai, NetInfo baseInfo,
            NetTool.ControlPoint start, NetTool.ControlPoint end, bool curved)
        {
            if (mode != "ground" && mode != "elevated" && mode != "bridge" &&
                mode != "slope" && mode != "tunnel" && mode != "auto")
                throw new ArgumentException("mode must be auto, ground, elevated, bridge, slope or tunnel");
            if (baseInfo.m_netAI is RoadBridgeAI)
            {
                if (mode != "auto" && mode != "bridge")
                    throw new NotSupportedException("standalone RoadBridgeAI supports only auto/bridge mode");
                if (start.m_elevation <= 0.1f || end.m_elevation <= 0.1f)
                    throw new ArgumentException("standalone bridge endpoints, including existing nodes, require positive terrain-relative height");
                return baseInfo;
            }
            if (ai == null)
            {
                if ((mode != "auto" && mode != "ground") ||
                    Mathf.Abs(start.m_elevation) > 0.1f || Mathf.Abs(end.m_elevation) > 0.1f)
                    throw new NotSupportedException("non-RoadAI prefabs support ground placement only");
                return baseInfo;
            }
            if (mode == "auto" && !baseInfo.m_useFixedHeight) return baseInfo;
            float length = Vector3.Distance(start.m_position, end.m_position);
            bool enableDouble = length >= 100f;
            ToolBase.ToolErrors errors = ToolBase.ToolErrors.None;
            NetInfo selected = ai.GetInfo(Mathf.Min(start.m_elevation, end.m_elevation),
                Mathf.Max(start.m_elevation, end.m_elevation), length, false, false,
                curved, enableDouble, ref errors);
            if (baseInfo.m_useFixedHeight &&
                (errors != ToolBase.ToolErrors.None || !IsParentInfoOrVariant(ai, baseInfo, selected) || !IsLoaded(selected)))
                throw new ArgumentException("fixed-height road did not resolve a loaded parent variant (" + errors + ")");
            if (errors != ToolBase.ToolErrors.None || selected == null || Variant(ai, selected) != mode)
            {
                if (mode != "auto")
                    throw new ArgumentException("requested " + mode + " variant is unavailable at these heights/length; " +
                        "RoadAI selected " + (selected == null ? "none" : Variant(ai, selected)) + " (" + errors + ")");
            }
            // NetTool skips GetInfo when the parent prefab has fixed height.
            // Use the real variant selected by that same parent's RoadAI;
            // NetTool still performs geometry, collision and connection tests.
            return baseInfo.m_useFixedHeight ? selected : baseInfo;
        }

        static ushort BuildLeg(NetInfo info, NetTool.ControlPoint start,
            NetTool.ControlPoint middle, NetTool.ControlPoint end, out ushort firstNode)
        {
            Vector3 first = middle.m_position - start.m_position;
            Vector3 last = end.m_position - middle.m_position;
            first.y = 0f; last.y = 0f;
            if (first.sqrMagnitude < 1f || last.sqrMagnitude < 1f)
                throw new ArgumentException("road control points are too close");
            first.Normalize(); last.Normalize();
            start.m_direction = first;
            middle.m_direction = (first + last).normalized;
            end.m_direction = last;
            var positions = new FastList<NetTool.NodePosition>();
            ushort lastNode, segment;
            int cost, production;
            ToolBase.ToolErrors errors = NetTool.CreateNode(info, start, middle, end,
                positions, 1000, true, true, false, false, false, false, false, 0,
                out firstNode, out lastNode, out segment, out cost, out production);
            if (errors != ToolBase.ToolErrors.None) throw new InvalidOperationException("road test failed: " + errors);
            errors = NetTool.CreateNode(info, start, middle, end,
                positions, 1000, false, true, false, false, false, false, false, 0,
                out firstNode, out lastNode, out segment, out cost, out production);
            if (errors != ToolBase.ToolErrors.None || lastNode == 0)
                throw new InvalidOperationException("road creation failed: " + errors);
            return lastNode;
        }

        static JSONArray Ids(List<ushort> ids)
        {
            var arr = new JSONArray();
            foreach (ushort id in ids) arr.Add((int)id);
            return arr;
        }

        static List<ushort> OrderLeg(NetManager nm, List<ushort> ids, ushort start, ushort end)
        {
            var ordered = new List<ushort>();
            ushort current = start;
            while (current != end && ordered.Count < ids.Count)
            {
                ushort nextSegment = 0, nextNode = 0;
                foreach (ushort id in ids)
                {
                    if (ordered.Contains(id)) continue;
                    var segment = nm.m_segments.m_buffer[id];
                    if (segment.m_startNode == current) { nextSegment = id; nextNode = segment.m_endNode; break; }
                    if (segment.m_endNode == current) { nextSegment = id; nextNode = segment.m_startNode; break; }
                }
                if (nextSegment == 0) break;
                ordered.Add(nextSegment);
                current = nextNode;
            }
            if (current != end || ordered.Count != ids.Count)
                throw new InvalidOperationException("road tool created disconnected or unexpected segments");
            return ordered;
        }

        static JSONNode Build(NetInfo road, List<NetTool.ControlPoint> points, List<string> modes,
            bool curved, float middleX, float middleZ)
        {
            var nm = Singleton<NetManager>.instance;
            var snap = Capture(nm);
            var ai = road.m_netAI as RoadAI;
            var startExisting = Remember(nm, points[0]);
            var endExisting = Remember(nm, points[points.Count - 1]);
            var endpoints = new List<ushort>();
            var orderedSegments = new List<ushort>();
            ushort firstOverallNode = 0;
            try
            {
                for (int i = 0; i < points.Count - 1; i++)
                {
                    var start = points[i];
                    var end = points[i + 1];
                    bool isCurved = curved && points.Count == 2;
                    NetInfo legInfo = RequireMode(modes[i], ai, road, start, end, isCurved);
                    float mx = isCurved ? middleX : (start.m_position.x + end.m_position.x) * 0.5f;
                    float mz = isCurved ? middleZ : (start.m_position.z + end.m_position.z) * 0.5f;
                    var middle = Point(mx, mz, (start.m_elevation + end.m_elevation) * 0.5f, 0);
                    if (i == 0) RefuseUnsafeStraightMerge(nm, startExisting, middle.m_position - start.m_position);
                    if (i == points.Count - 2) RefuseUnsafeStraightMerge(nm, endExisting, end.m_position - middle.m_position);
                    var beforeLeg = Capture(nm);
                    ushort firstNode;
                    ushort lastNode;
                    // NetTool.TryMoveNode can merge a straight, moveable
                    // endpoint by releasing its old segment. For an internal
                    // waypoint created by this command, temporarily prevent
                    // that optimization while attaching the next leg.
                    bool protectOwned = start.m_node != 0 && !snap.Nodes[start.m_node] &&
                        (nm.m_nodes.m_buffer[start.m_node].m_flags & NetNode.Flags.Moveable) != 0;
                    if (protectOwned)
                        nm.m_nodes.m_buffer[start.m_node].m_flags &= ~NetNode.Flags.Moveable;
                    try { lastNode = BuildLeg(legInfo, start, middle, end, out firstNode); }
                    finally
                    {
                        if (protectOwned &&
                            (nm.m_nodes.m_buffer[start.m_node].m_flags & NetNode.Flags.Created) != 0)
                            nm.m_nodes.m_buffer[start.m_node].m_flags |= NetNode.Flags.Moveable;
                    }
                    if (i == 0) firstOverallNode = firstNode;
                    var legSegments = OrderLeg(nm, NewSegments(nm, beforeLeg), firstNode, lastNode);
                    if (modes[i] != "auto")
                        foreach (ushort id in legSegments)
                            if (Variant(ai, nm.m_segments.m_buffer[id].Info) != modes[i])
                                throw new InvalidOperationException("road tool created " +
                                    Variant(ai, nm.m_segments.m_buffer[id].Info) + " instead of requested " + modes[i]);
                    orderedSegments.AddRange(legSegments);
                    endpoints.Add(lastNode);
                    // Reuse the actual final node on the next leg, never create a
                    // coincident independent node at a path waypoint.
                    if (i + 1 < points.Count - 1)
                    {
                        var actual = nm.m_nodes.m_buffer[lastNode].m_position;
                        points[i + 1] = Point(actual.x, actual.z, 0f, lastNode);
                    }
                }
                var finalSegments = NewSegments(nm, snap);
                if (finalSegments.Count != orderedSegments.Count)
                    throw new InvalidOperationException("native road tool replaced a previously created segment");
                foreach (ushort id in orderedSegments)
                    if (!finalSegments.Contains(id))
                        throw new InvalidOperationException("native road tool removed a previously created segment");
                var newSegs = OrderLeg(nm, finalSegments, firstOverallNode,
                    endpoints[endpoints.Count - 1]);
                if (newSegs.Count == 0) throw new InvalidOperationException("road tool created no segments");
                CheckExisting(nm, startExisting);
                CheckExisting(nm, endExisting);
                var variants = new JSONArray();
                foreach (ushort id in newSegs)
                    variants.Add(Obj("id", (int)id, "prefab", nm.m_segments.m_buffer[id].Info.name,
                        "variant", Variant(ai, nm.m_segments.m_buffer[id].Info)));
                return Obj("road", road.name, "segment", (int)newSegs[0],
                    "segments", Ids(newSegs), "segment_details", variants,
                    "start_node", (int)firstOverallNode,
                    "end_node", (int)endpoints[endpoints.Count - 1],
                    "nodes", points.Count, "created_nodes", Ids(NewNodes(nm, snap)),
                    "curved", curved);
            }
            catch
            {
                RollBack(nm, snap);
                RestoreExisting(nm, startExisting);
                RestoreExisting(nm, endExisting);
                throw;
            }
        }

        public static JSONNode PlaceRoad(JSONNode a)
        {
            string name = a.HasKey("road") ? a["road"].Value : "Basic Road";
            var road = BaseRoad(name);
            var points = new List<NetTool.ControlPoint> {
                Point(a["start_x"].AsFloat, a["start_z"].AsFloat,
                    a.HasKey("start_height") ? a["start_height"].AsFloat : 0f,
                    a.HasKey("start_node") ? a["start_node"].AsInt : 0),
                Point(a["end_x"].AsFloat, a["end_z"].AsFloat,
                    a.HasKey("end_height") ? a["end_height"].AsFloat : 0f,
                    a.HasKey("end_node") ? a["end_node"].AsInt : 0)
            };
            if ((a.HasKey("middle_x") && !a.HasKey("middle_z")) ||
                (!a.HasKey("middle_x") && a.HasKey("middle_z")))
                throw new ArgumentException("middle_x and middle_z must be provided together");
            bool curved = a.HasKey("middle_x");
            string mode = a.HasKey("mode") ? a["mode"].Value.ToLowerInvariant() : "auto";
            return Build(road, points, new List<string> { mode }, curved,
                a["middle_x"].AsFloat, a["middle_z"].AsFloat);
        }

        public static JSONNode PlacePath(JSONNode a)
        {
            var road = BaseRoad(a.HasKey("road") ? a["road"].Value : "Basic Road");
            JSONNode raw = a["points"];
            if (raw.Count < 2 || raw.Count > 64) throw new ArgumentException("points must contain 2..64 [x,z] or [x,z,height] entries");
            var points = new List<NetTool.ControlPoint>();
            for (int i = 0; i < raw.Count; i++)
            {
                if (raw[i].Count != 2 && raw[i].Count != 3)
                    throw new ArgumentException("each point must be [x,z] or [x,z,height]");
                int nodeId = i == 0 && a.HasKey("start_node") ? a["start_node"].AsInt :
                    i == raw.Count - 1 && a.HasKey("end_node") ? a["end_node"].AsInt : 0;
                points.Add(Point(raw[i][0].AsFloat, raw[i][1].AsFloat,
                    raw[i].Count == 3 ? raw[i][2].AsFloat : 0f, nodeId));
            }
            var modes = new List<string>();
            JSONNode requested = a["modes"];
            if (a.HasKey("modes") && requested.Count != raw.Count - 1)
                throw new ArgumentException("modes must have one entry per path leg");
            for (int i = 0; i < raw.Count - 1; i++)
                modes.Add(a.HasKey("modes") ? requested[i].Value.ToLowerInvariant() : "auto");
            return Build(road, points, modes, false, 0f, 0f);
        }

        public static JSONNode Variants(JSONNode a)
        {
            string filter = a.HasKey("filter") ? a["filter"].Value : null;
            int limit = a.HasKey("limit") ? Mathf.Clamp(a["limit"].AsInt, 0, 100) : 50;
            var arr = new JSONArray();
            int count = PrefabCollection<NetInfo>.LoadedCount();
            var referenced = ReferencedRoadVariants();
            for (int i = 0; i < count && arr.Count < limit; i++)
            {
                NetInfo baseInfo = PrefabCollection<NetInfo>.GetLoaded((uint)i);
                if (baseInfo == null) continue;
                if (!string.IsNullOrEmpty(filter) && baseInfo.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                RoadAI ai = baseInfo.m_netAI as RoadAI;
                if (ai != null)
                    arr.Add(Obj("road", baseInfo.name,
                        "ground", baseInfo.name,
                        "elevated", ai.m_elevatedInfo == null ? "" : ai.m_elevatedInfo.name,
                        "bridge", ai.m_bridgeInfo == null ? "" : ai.m_bridgeInfo.name,
                        "slope", ai.m_slopeInfo == null ? "" : ai.m_slopeInfo.name,
                        "tunnel", ai.m_tunnelInfo == null ? "" : ai.m_tunnelInfo.name,
                        "bridge_requires_double", ai.m_bridgeInfo != null && ai.m_bridgeInfo.m_netAI.RequireDoubleSegments(),
                        "fixed_height", baseInfo.m_useFixedHeight,
                        "variant_selection", baseInfo.m_useFixedHeight ? "parent_get_info_then_native_child" : "native_get_info",
                        "standalone_bridge", false));
                else if (IsStandaloneBridge(baseInfo, referenced))
                    arr.Add(Obj("road", baseInfo.name,
                        "ground", "", "elevated", "", "bridge", baseInfo.name,
                        "slope", "", "tunnel", "",
                        "bridge_requires_double", baseInfo.m_netAI.RequireDoubleSegments(),
                        "fixed_height", baseInfo.m_useFixedHeight,
                        "variant_selection", "standalone_bridge",
                        "standalone_bridge", true));
            }
            return Obj("count", arr.Count, "prefabs", arr);
        }

        public static JSONNode Inspect(JSONNode a)
        {
            var nm = Singleton<NetManager>.instance;
            var referenced = ReferencedRoadVariants();
            var nodes = new JSONArray();
            var segments = new JSONArray();
            JSONNode nodeIds = a["node_ids"], segmentIds = a["segment_ids"];
            if (nodeIds.Count > 100 || segmentIds.Count > 100)
                throw new ArgumentException("inspect at most 100 nodes and 100 segments");
            for (int i = 0; i < nodeIds.Count; i++)
            {
                int id = nodeIds[i].AsInt;
                if (id <= 0 || id >= nm.m_nodes.m_buffer.Length) throw new ArgumentException("node id out of range");
                ref NetNode node = ref nm.m_nodes.m_buffer[id];
                if ((node.m_flags & NetNode.Flags.Created) == 0) continue;
                var connected = new JSONArray();
                for (int j = 0; j < 8; j++) if (node.GetSegment(j) != 0) connected.Add((int)node.GetSegment(j));
                float ground = Terrain(node.m_position.x, node.m_position.z);
                nodes.Add(Obj("id", id, "x", node.m_position.x, "y", node.m_position.y,
                    "z", node.m_position.z, "terrain_y", ground,
                    "height_above_terrain", node.m_position.y - ground,
                    "elevation", (int)node.m_elevation, "flags", node.m_flags.ToString(),
                    "building_id", (int)node.m_building,
                    "prefab", node.Info == null ? "" : node.Info.name,
                    "standalone_bridge", IsStandaloneBridge(node.Info, referenced),
                    "segments", connected));
            }
            for (int i = 0; i < segmentIds.Count; i++)
            {
                int id = segmentIds[i].AsInt;
                if (id <= 0 || id >= nm.m_segments.m_buffer.Length) throw new ArgumentException("segment id out of range");
                ref NetSegment segment = ref nm.m_segments.m_buffer[id];
                if ((segment.m_flags & NetSegment.Flags.Created) == 0) continue;
                segments.Add(Obj("id", id, "prefab", segment.Info == null ? "" : segment.Info.name,
                    "flags", segment.m_flags.ToString(),
                    "standalone_bridge", IsStandaloneBridge(segment.Info, referenced),
                    "start_node", (int)segment.m_startNode, "end_node", (int)segment.m_endNode));
            }
            var result = new JSONObject();
            result["nodes"] = nodes; result["segments"] = segments;
            if (a.HasKey("x") && a.HasKey("z"))
            {
                float x = a["x"].AsFloat, z = a["z"].AsFloat;
                result["point"] = Obj("x", x, "z", z, "terrain_y", Terrain(x, z),
                    "out_of_area", Singleton<GameAreaManager>.instance.PointOutOfArea(new Vector3(x, 0f, z)));
                if (a.HasKey("radius"))
                {
                    float radius = a["radius"].AsFloat;
                    CheckFinite(radius, "radius");
                    if (radius <= 0f || radius > 100f) throw new ArgumentException("radius must be within 0..100m");
                    var nearby = new JSONArray();
                    for (int id = 1; id < nm.m_nodes.m_buffer.Length && nearby.Count < 20; id++)
                    {
                        var node = nm.m_nodes.m_buffer[id];
                        if ((node.m_flags & NetNode.Flags.Created) == 0 || node.CountSegments() != 1) continue;
                        float dx = node.m_position.x - x, dz = node.m_position.z - z;
                        if (dx * dx + dz * dz > radius * radius) continue;
                        nearby.Add(Obj("id", id, "x", node.m_position.x,
                            "y", node.m_position.y, "z", node.m_position.z,
                            "prefab", node.Info == null ? "" : node.Info.name));
                    }
                    result["nearby_endpoints"] = nearby;
                }
            }
            return result;
        }
    }
}
