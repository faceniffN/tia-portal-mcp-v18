# TiaMcpServer — TIA Portal V18 Openness MCP 服务器

将本机已验证的 TIA Openness V18 自动化能力封装为标准 **MCP（Model Context Protocol）** 服务器，
任何支持 MCP 的 AI 客户端（Claude Desktop / VS Code / Cursor / Cline / Roo Code 等）均可通过自然语言直接操作博途工程。

- 运行环境：Windows + TIA Portal **V18**（本机 `D:\TIA\Portal V18`）+ .NET Framework 4.8
- 协议：JSON-RPC 2.0 over stdio（newline-delimited），MCP 规范 `2024-11-05`
- 全部工具逻辑均已在 Test01 工程**实测验证**（见文末验证记录）

## 构建

```powershell
dotnet build TiaMcpServer.csproj -c Release
# 产物: bin\Release\net48\TiaMcpServer.exe
```

## 运行 / 自检

```powershell
TiaMcpServer.exe            # 作为 MCP stdio 服务器运行（由 MCP 客户端拉起）
TiaMcpServer.exe --tools    # 打印工具清单（自检，不启动 TIA）
TiaMcpServer.exe --doctor   # 环境体检：TIA 安装/API dll/.NET 4.8/用户组/授权，逐项给结论
TiaMcpServer.exe --config   # 一键写入 VS Code 的 mcp.json（用户级+项目级，已有配置自动备份 .bak）
```

> **两种运行模式**（启动参数选择）：
> - **默认（无参数）**：headless 无界面，冷启动约 10-30 秒（快），适合纯自动化批量操作
> - **`--with-ui`**：带界面模式，**AI 操作时 TIA Portal 窗口保持打开**，可实时看到工程变化（导入/编译/保存），适合人机协作、边操作边查看；冷启动约 1-3 分钟
>
> 首次连接 TIA 会弹 Openness 授权窗，勾"始终允许"一次后免弹。

## 客户端配置

将下面的配置加入你的 MCP 客户端（路径按实际替换）：

```json
{
  "mcpServers": {
    "tia-v18": {
      "command": "C:\\Users\\Administrator\\Doubao\\chats\\2026-09-24\\new-chat\\TiaMcpServer\\bin\\Release\\net48\\TiaMcpServer.exe",
      "args": []
    }
  }
}
```

- **Claude Desktop**：`claude_desktop_config.json`（`%APPDATA%\Claude\`）
- **VS Code**：`.vscode/mcp.json` 或用户设置 `mcp` 段
- **Cursor**：`~/.cursor/mcp.json`
- **Cline / Roo Code**：设置页 MCP servers 中添加
- **OpenCode（终端版/桌面版）**：`~/.config/opencode/opencode.json`（Windows: `%USERPROFILE%\.config\opencode\opencode.json`）：

```json
{
  "mcp": {
    "tia-v18": {
      "type": "local",
      "command": ["C:\\path\\to\\TiaMcpServer.exe", "--with-ui"],
      "enabled": true
    }
  }
}
```
> 注意：opencode 本地 MCP 用 `type: "local"`（不是 `stdio`）。

首次被客户端拉起并打开工程时，TIA Portal 会弹 **Openness 应用授权** 窗口——勾选"始终允许"一次，
此后免弹窗（TIA 会记住授权）。

## 工具清单（13 个）

### 工程类
| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `tia_open_project` | `projectPath` | 打开 .ap18 工程（Openness 会话内复用），返回设备/PLC/HMI/连接/画面/变量表状态 |
| `tia_project_status` | — | 当前工程状态（设备、PLC、HMI、连接、画面、变量表） |
| `tia_save_project` | — | 保存工程 |
| `tia_close_project` | — | 关闭工程（TIA 会话保持） |
| `tia_delete_object` | `type`, `name` | 删除 HMI 对象：type=`tagTable`\|`screen`\|`connection`（工程维护） |
| `tia_shutdown` | — | **彻底释放**：关工程 + 释放 TIA + 清理无窗口 headless 进程，解锁工程文件（收尾必调，之后可开 GUI 查看） |

### 查询类
| 工具 | 说明 |
| --- | --- |
| `tia_list_connections` | 枚举 HMI 连接。**Openness 只支持非集成连接**（集成连接不可枚举，官方限制） |
| `tia_list_screens` | 列出 HMI 画面 |
| `tia_list_tag_tables` | 列出 HMI 变量表及变量 |

### 导入类
| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `tia_import_tag_table` | `xmlPath` | 导入标签表 XML。**V18 已验证规则**：非集成连接 + 绝对地址（`LogicalAddress=%M0.0`、无 ControllerTag）+ 标签名全局唯一 + `Engineering version="V18"` + `AddressAccessMode=Absolute` + **Simatic ML ID 全 HMI 设备唯一（建议用 900xxx 大 ID 避开已有对象）** + **变量名不得与工程既有变量重名**（重名报"object already exists"） |
| `tia_import_screen` | `xmlPath` | 导入画面 XML。**V18 已验证规则**：元素含 `ObjectName`、画面号唯一、Button 不含 `Enabled`/`Visible`；导入后回读 `verified` |

### 编译类
| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `tia_compile` | `target` | `plc` \| `hmi` \| `all`（默认 all），返回逐条错误/警告 |

### 资产生成（离线，不连 TIA）
| 工具 | 参数 | 说明 |
| --- | --- | --- |
| `hmi_build_package` | `packageJsonPath`, `outDir` | 从 package JSON（Name/TagTable/ScreenDesign）生成标签表+画面 XML+manifest，**自动归一化 V18** |
| `hmi_validate_package` | `dir` | 校验生成的包（引用/边界/一致性） |
| `hmi_validate_plc_sync` | `dir`, `plcSymbolsJson` | 校验 HMI 标签与 PLC 符号同步（`{"symbols":["Start"]}`） |

## package JSON 格式要点

- `TagTable.Tags[]`：`Name` / `DataType` / `Connection` / `ControllerTag`（符号名，集成连接用）
- `ScreenDesign.Items[]`：`Type`(Text/Button/IOField/Rectangle)、`Name`/`Text`/`Left`/`Top`/`Width`/`Height`、
  样式属性放 `Properties` 嵌套（FontSize/ForeColor/TabIndex…）、按钮事件 `Actions:[{Event,ActionKind,TargetTag}]`
- 完整示例见 `..\ClassicHmiBuilder\test01_package.json`

## 文字汇报（report 字段）

**所有工具返回都带 `report` 字段**——人类可读的操作汇报（中文），AI 客户端应原样转述给用户，实现"全程文字看过程"：

```json
// tia_open_project / tia_project_status
"report": "工程 TEST01 已打开：PLC 1 台、HMI 1 台；连接 [Connection_1]；画面 [画面_1]；变量表 [默认变量表、测试_变量表]"

// tia_import_tag_table
"report": "已导入变量表：测试_变量表，共 3 个变量（HMI_Start、HMI_Stop、HMI_Run_X），回读验证通过"

// tia_compile
"report": "PLC 编译成功（0 错误 0 警告）；HMI 编译成功（0 错误 0 警告）"

// tia_save_project / tia_delete_object / tia_shutdown
"report": "工程已保存" / "已删除画面 画面_2" / "TIA 会话已释放，工程文件已解锁..."
```

## 推荐工作流（AI 全程操作，你随时 GUI 查看）

```
配置 --with-ui 后：
AI 打开工程 → TIA 窗口弹出，你全程可见
→ AI 导入/修改/编译/保存（画面、变量表实时变化）
→ AI 操作完，窗口保持打开，你继续查看/手动调整
→ 你保存关闭 TIA → 下次再让 AI 打开操作（循环）
```

- **带界面模式**（`--with-ui`）：AI 操作时 GUI 保持打开，人机协作最佳体验；冷启动慢
- **headless 模式**（默认）：纯自动化提速，GUI 不显示
- AI 操作后 `tia_shutdown` 收尾：headless 模式彻底释放；with-ui 模式保留 GUI 窗口（有窗口实例不杀）
- 孤儿 TIA 进程（异常退出残留）处置：`Stop-Process -Name "Siemens.Automation.Portal"`

## 关键知识与边界（全部实测/官方文档确认）

1. **HMI 连接两类**：集成连接（Devices&Networks 配置、绑定伙伴）**Openness 不可枚举/不可导出**；
   非集成连接（Connections 编辑器创建、无伙伴、IP 直连）**Openness 可枚举/可导入导出**。
2. **标签绑定**：非集成连接必须**绝对地址**（`LogicalAddress=%M0.0`，无 ControllerTag）；
   集成连接用符号名 ControllerTag，但要求连接已存在于工程（Openness 不创建连接）。
3. **连接创建**：V18 Openness **不支持创建 HMI 连接**（V21 新增），只能 GUI 建或 XML 导入。
4. **HMI 标签名全局唯一**（跨变量表），画面号唯一，画面元素需 `ObjectName`。
5. **版本**：标签表 XML 需 `Engineering version="V18"`（构建器默认 V21，工具已归一化）。

## 安全提示

- 工具**直接修改真实工程**（导入/编译/保存），无撤销。生产使用前先备份 .ap18。
- Openness 会话与 TIA GUI 共用实例：导入/编译失败可能污染会话，建议失败后重启 MCP 进程（或重启 TIA）。

## 验证记录（2026-09-26，本机实测）

| 测试 | 结果 |
| --- | --- |
| initialize / tools/list / tools/call 协议握手 | ✅ |
| tia_open_project → TEST01（PLC_1 + HMI_RT_1） | ✅ 设备/连接/画面/变量表完整返回 |
| tia_import_tag_table（非集成+绝对地址+大 ID） | ✅ 导入成功 + verified 回读 |
| tia_import_screen（构建器产物） | ✅ 导入成功 + verified 回读 |
| tia_compile all（headless 模式） | ✅ PLC Success（0 错误）+ HMI Success（0 错误） |
| tia_delete_object（清理测试残留 4 项） | ✅ 全部删除 |
| tia_save_project | ✅ 已保存 |
| --doctor / --config CLI | ✅ 体检全绿 / 双配置写入

## 许可与来源

- MCP 协议层/服务层：本会话自研（MIT 精神，保留注释即可复用）
- 离线构建器（ClassicHmi*Builder）：移植自 `bulaofen0036-coder/TIA_Portal_Openness_MCP`（MIT License）
- Openness 边界结论：Siemens Openness 系统手册 109477163 + 官方 docs.tia.siemens.cloud
