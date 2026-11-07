# CS1 MCP Bridge：Windows 使用说明

这是 [cs1-mcp](README.md) 的 Windows 快速说明。原项目作者及许可见 [LICENSE](LICENSE)。项目由游戏内 C# 模组和 Python MCP 服务组成，协议见 [PROTOCOL.md](PROTOCOL.md)。

## 构建与安装

需要本机《Cities: Skylines》一代、.NET SDK、PowerShell 7，以及 Python 3.10 以上和 `uv`。构建读取本机游戏程序集，不会把游戏 DLL 放入仓库或发布包。

```powershell
pwsh -File scripts/windows-mod.ps1 -Action Build -GamePath 'X:\Steam\steamapps\common\Cities_Skylines'
# 关闭游戏后安装：
pwsh -File scripts/windows-mod.ps1 -Action Install -GamePath 'X:\Steam\steamapps\common\Cities_Skylines'
```

`-GamePath` 可省略，脚本会尝试查找 Steam 库；也可指定 `-ManagedPath`。安装前如已有同名 DLL，脚本会在 Mods 目录之外备份；`-Action Uninstall` 可恢复。安装后在游戏内容管理器启用 **CS1 MCP Bridge**，加载一个城市。

## 连接与只读检查

```powershell
cd server
uv sync --locked
uv run python tests/stdio_smoke.py --output report.json
```

这个检查通过真实 stdio MCP 会话初始化、列出工具，并默认只读调用 `ping`、`get_city_stats`、`get_camera`、`list_prefabs` 和 `find_buildings`。没有加载城市时，游戏查询会报连接错误。Codex 的连接示例见 [server/README.md](server/README.md)。城市写入命令应先在存档副本上测试。

## 0.1.1 Windows 历史验证记录

验证环境：Windows、CS1 1.21.1-f9（Unity 5.6.7f1）、.NET SDK 9.0.304、Python 3.11.14、FastMCP 3.4.2、MCP SDK 1.27.2。路径、账户及存档内容不收录。

| 项目 | 结果 |
|---|---|
| 使用本机游戏程序集构建 .NET 3.5 模组 | 通过，0 错误、0 警告 |
| Python 传输单元测试 | 4/4 通过 |
| stdio MCP `initialize` / `list_tools` | 通过，发现 19 个工具 |
| Windows 安装/卸载脚本 | 中文路径、重复安装保留首次备份、卸载恢复原 DLL 且保留无关文件、临时环境变量恢复、真实 Mods 目录安装—卸载—再安装均通过 |
| 加载隔离测试城市后的 `ping` / `get_city_stats` | 通过：`pong`、人口 1142；该测试城启用了无限金钱 |
| 只读 `get_camera` / `list_prefabs` / `find_buildings` | 通过：相机数据可读；Basic Road 与公园预制件可枚举；建筑 ID 可查询 |
| 隔离副本上的 `place_road` / `place_path` | 通过：最终复测短直路段 ID 10397，三点路径两段 ID 11416、5143；此前普通启动模式下也有可见路段的截图证据 |
| 隔离副本上的 `place_building` / `find_buildings` / `bulldoze_building` | 通过：最终复测 Regular Park ID 116，查询确认存在，拆除后确认消失 |
| 暂停与昼夜时间控制 | 通过：暂停后 2 秒两个游戏时钟均未前进、有效速度为 0；设置 17 点后约 2 秒读回 16.99988 点，误差约 0.00012 小时 |

安装脚本验证包含隔离目录测试及真实 Mods 目录安装验证。道路测试未清理新路段，隔离副本不作为原存档发布。

最终复测使用 `-noWorkshop` 启动。此前普通模式下道路和公园写入也成功；这次普通模式重启停在工坊初始化、尚未进入城市，因此未用于最终控制命令验证。

## 0.2.0 桥隧验证

0.1.1 版本道路命令按地面高度创建节点与路段，不支持桥隧、分区或存档。0.2.0 增加相对地面高度和原生道路变体：`place_path` 可传 `[x,z,height]`，负高度用于坡道/隧道，正高度用于高架/桥梁；`modes` 可逐段要求真实变体。`start_node` / `end_node` 仅连接显式指定且坐标匹配的既有道路端点，不会自动拆分现有路段或吸附到邻近道路。`list_prefabs(kind="road_variants")` 查看已加载道路族能力，`inspect_network` 回读真实节点高度、flags、预制件与路段端点。

0.2.0 的真实 stdio MCP 会话列出 20 个工具，并在隔离副本中验证了四种实际加载的道路族：

| 基础道路 | 隧道 | 桥梁 |
|---|---|---|
| Basic Road | 直线路径，包含地面/坡道/隧道变体，地下节点距地面 12 米且标记 `Underground` | 直线路径，包含地面/高架/桥梁变体，桥面节点高于地面 28 米 |
| Medium Road | 与本次新建地面引道共享端点，6 段中有 2 段隧道 | 与引道共享端点，10 段中有 4 段桥梁 |
| Large Road | 与地面引道共享端点，6 段中有 2 段隧道 | 与引道共享端点，10 段中有 2 段桥梁；高跨节点具有 `Double` 标志 |
| Highway | 与地面引道共享端点，6 段中有 2 段隧道 | 与引道共享端点，10 段中有 2 段桥梁；高跨节点具有 `Double` 标志 |

兼容与失败路径回归也在隔离副本完成：实际加载的 `Pedestrian Pavement` 用旧地面 `place_road` 参数创建一段，读回两个 `OnGround` 节点和路段；Basic Road 旧二维 `[x,z]` 路径创建两段地面路。近乎共线的显式旧端点接入按预期拒绝，查询到的原节点与邻接路段在前后保持一致。刻意让后续路径腿请求不匹配的隧道变体时，命令返回错误，查询到的原端点与邻接路段仍一致。这些检查不证明任意第三方模组、地形、树木或全城状态可完全回滚。另一次在有障碍的位置出现 `ObjectCollision`，改在确认空地后旧二维路径成功。

上述结果由真实 MCP `inspect_network` 回读节点世界高度、地形高度、flags、路段预制件和两端节点；Medium/Large/Highway 的地面引道与桥隧路径使用相同端点 ID。桥上节点存在实际附属建筑关联。测试没有修改原存档，也没有把测试道路保存进发布包。

一次早期失败揭示 `TerrainManager` 浮点双参数重载使用格点坐标与原始高度单位；当前实现已改用世界坐标 `Vector3` 重载，并添加本机游戏 IL 回归检查。高度和入口由原生 NetTool 处理；某些旧端点的近乎共线接入会被安全拒绝，需略带角度的连接。此版仍未提供分区或存档工具。
