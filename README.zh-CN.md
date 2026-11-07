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

## Windows 验证记录

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

当前道路命令按地面高度创建节点与路段，不接受桥梁、隧道或高度参数，也不会自动连接到既有道路。本版未提供分区和存档工具。
