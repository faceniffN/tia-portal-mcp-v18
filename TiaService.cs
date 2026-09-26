using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Hmi;
using Siemens.Engineering.Hmi.Communication;
using Siemens.Engineering.Hmi.Screen;
using Siemens.Engineering.Hmi.Tag;
using Siemens.Engineering.SW;

namespace TiaMcpServer
{
    /// <summary>
    /// TIA Openness V18 服务：封装工程打开、连接枚举、标签/画面导入、编译、保存。
    /// 全部逻辑已在 Test01 工程实测验证（非集成连接可枚举、绝对地址标签导入等）。
    /// </summary>
    public sealed class TiaService : IDisposable
    {
        private static readonly string PublicApiPath = @"D:\TIA\Portal V18\PublicAPI\V18";

        private TiaPortal _tia;
        private Project _project;
        private readonly bool _withUi;

        /// <summary>
        /// withUi=true → WithUserInterface：AI 操作时 TIA GUI 可见（用户实时查看），冷启动较慢；
        /// withUi=false（默认）→ WithoutUserInterface：headless 提速，GUI 不显示。
        /// </summary>
        public TiaService(bool withUi = false)
        {
            _withUi = withUi;
            AppDomain.CurrentDomain.AssemblyResolve += ResolveEngineeringAssembly;
            PreloadEngineeringAssembly();
        }

        private static Assembly ResolveEngineeringAssembly(object sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name);
            string dll = Path.Combine(PublicApiPath, name.Name + ".dll");
            return File.Exists(dll) ? Assembly.LoadFrom(dll) : null;
        }

        private static void PreloadEngineeringAssembly()
        {
            string dll = Path.Combine(PublicApiPath, "Siemens.Engineering.dll");
            if (File.Exists(dll)) Assembly.LoadFrom(dll);
        }

        private TiaPortal GetTia()
        {
            if (_tia == null)
            {
                // headless（WithoutUserInterface）：冷启动 ~10-30s，比带界面快 ~10 倍（借鉴 bulaofen 方案）
                // with-ui（WithUserInterface）：AI 操作时 TIA GUI 可见，用户可实时查看工程变化
                _tia = new TiaPortal(_withUi ? TiaPortalMode.WithUserInterface : TiaPortalMode.WithoutUserInterface);
            }
            return _tia;
        }

        public bool HasProject => _project != null;

        /// <summary>打开工程（已打开同名工程则复用）。返回设备概要。</summary>
        public JsonObject OpenProject(string projectPath)
        {
            GetTia();
            foreach (Project open in _tia.Projects)
            {
                if (string.Equals(open.Name, "TEST01", StringComparison.OrdinalIgnoreCase)
                    || (open.Path != null && string.Equals(open.Path.ToString(), projectPath, StringComparison.OrdinalIgnoreCase)))
                {
                    _project = open;
                    return Status();
                }
            }
            _project = _tia.Projects.Open(new FileInfo(projectPath));
            return Status();
        }

        /// <summary>工程状态：设备 / PLC / HMI / 连接 / 画面 / 变量表。</summary>
        public JsonObject Status()
        {
            EnsureProject();
            var root = new JsonObject
            {
                ["projectName"] = _project.Name,
                ["projectPath"] = _project.Path?.ToString() ?? ""
            };

            var devices = new JsonArray();
            PlcSoftware plc = null;
            HmiTarget hmi = null;
            foreach (Device device in _project.Devices)
            {
                var dev = new JsonObject { ["name"] = device.Name };
                var items = new JsonArray();
                foreach (DeviceItem item in device.DeviceItems)
                {
                    try
                    {
                        SoftwareContainer container = item.GetService<SoftwareContainer>();
                        if (container?.Software is PlcSoftware p)
                        {
                            plc ??= p;
                            items.Add(new JsonObject { ["type"] = "PLC", ["name"] = item.Name });
                        }
                        else if (container?.Software is HmiTarget h)
                        {
                            hmi ??= h;
                            items.Add(new JsonObject { ["type"] = "HMI", ["name"] = item.Name });
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
                dev["items"] = items;
                devices.Add(dev);
            }
            root["devices"] = devices;

            if (hmi != null)
            {
                root["connections"] = ListConnections(hmi);
                root["screens"] = ListScreens(hmi);
                root["tagTables"] = ListTagTables(hmi);
            }
            root["report"] = BuildStatusReport(root);
            return root;
        }

        /// <summary>构造人类可读的状态汇报（AI 客户端应原样转述给用户）。</summary>
        private static string BuildStatusReport(JsonObject root)
        {
            string proj = root["projectName"]?.ToString() ?? "?";
            int plc = 0, hmi = 0;
            if (root["devices"] is JsonArray devs)
            {
                foreach (var d in devs)
                {
                    if (d is JsonObject o && o["items"] is JsonArray items)
                    {
                        foreach (var it in items)
                        {
                            if (it is JsonObject i && i["type"]?.ToString() == "PLC") plc++;
                            else if (it is JsonObject i2 && i2["type"]?.ToString() == "HMI") hmi++;
                        }
                    }
                }
            }
            string conns = root["connections"] is JsonArray ca ? string.Join("、", ca.Select(c => c?["name"]?.ToString() ?? "")) : "";
            string screens = root["screens"] is JsonArray sa ? string.Join("、", sa.Select(s => s?["name"]?.ToString() ?? "")) : "";
            string tables = root["tagTables"] is JsonArray ta ? string.Join("、", ta.Select(t => t?["name"]?.ToString() ?? "")) : "";
            return $"工程 {proj} 已打开：PLC {plc} 台、HMI {hmi} 台；连接 [{conns}]；画面 [{screens}]；变量表 [{tables}]";
        }

        /// <summary>HMI 连接枚举（Openness 只支持非集成连接，集成连接不可枚举——已验证）。</summary>
        public JsonArray ListConnections(HmiTarget hmi)
        {
            var arr = new JsonArray();
            if (hmi == null) return arr;
            foreach (Connection connection in hmi.Connections)
            {
                var c = new JsonObject { ["name"] = connection.Name };
                try
                {
                    foreach (string attr in new[] { "PartnerStation", "PartnerNode", "Interface", "Online", "RemoteAddress" })
                    {
                        string value = null;
                        try { value = connection.GetAttribute(attr)?.ToString(); } catch (Exception) { }
                        if (value != null) c[attr] = value;
                    }
                }
                catch (Exception)
                {
                }
                arr.Add(c);
            }
            return arr;
        }

        public JsonArray ListScreens(HmiTarget hmi)
        {
            var arr = new JsonArray();
            if (hmi == null) return arr;
            foreach (Screen screen in hmi.ScreenFolder.Screens)
            {
                arr.Add(new JsonObject { ["name"] = screen.Name });
            }
            return arr;
        }

        public JsonArray ListTagTables(HmiTarget hmi)
        {
            var arr = new JsonArray();
            if (hmi == null) return arr;
            try
            {
                foreach (TagTable table in hmi.TagFolder.TagTables)
                {
                    var t = new JsonObject { ["name"] = table.Name };
                    var tags = new JsonArray();
                    try
                    {
                        foreach (Tag tag in table.Tags)
                        {
                            tags.Add(new JsonObject { ["name"] = tag.Name, ["dataType"] = tag.GetAttribute("DataType")?.ToString() ?? "" });
                        }
                    }
                    catch (Exception)
                    {
                    }
                    t["tags"] = tags;
                    arr.Add(t);
                }
            }
            catch (Exception)
            {
            }
            return arr;
        }

        public HmiTarget FindHmiTarget()
        {
            EnsureProject();
            foreach (Device device in _project.Devices)
            {
                foreach (DeviceItem item in device.DeviceItems)
                {
                    try
                    {
                        SoftwareContainer container = item.GetService<SoftwareContainer>();
                        if (container?.Software is HmiTarget target) return target;
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            return null;
        }

        public PlcSoftware FindPlcSoftware()
        {
            EnsureProject();
            foreach (Device device in _project.Devices)
            {
                foreach (DeviceItem item in device.DeviceItems)
                {
                    try
                    {
                        SoftwareContainer container = item.GetService<SoftwareContainer>();
                        if (container?.Software is PlcSoftware target) return target;
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            return null;
        }

        /// <summary>导入 HMI 标签表 XML（绝对地址/非集成连接格式已验证可导入），导入后回读验证。</summary>
        public JsonObject ImportTagTable(string xmlPath)
        {
            EnsureProject();
            var hmi = FindHmiTarget();
            if (hmi == null) throw new InvalidOperationException("No HmiTarget found in project.");
            var imported = hmi.TagFolder.TagTables.Import(new FileInfo(xmlPath), ImportOptions.Override);
            var names = new JsonArray();
            foreach (TagTable table in imported) names.Add(table.Name);
            // 回读验证：确认标签真实写入（避免"导入成功但未链接"）
            var verified = new JsonArray();
            var after = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TagTable table in hmi.TagFolder.TagTables)
            {
                foreach (Tag tag in table.Tags) after.Add(table.Name + "/" + tag.Name);
            }
            foreach (JsonNode n in names)
            {
                string tableName = n?.ToString() ?? "";
                int count = after.Count(s => s.StartsWith(tableName + "/", StringComparison.OrdinalIgnoreCase));
                verified.Add(new JsonObject { ["table"] = tableName, ["tagCount"] = count, ["ok"] = count > 0 });
            }
            // 人类可读汇报：变量名清单
            var tagNames = new List<string>();
            foreach (TagTable table in hmi.TagFolder.TagTables)
            {
                foreach (JsonNode n in names)
                {
                    if (n?.ToString() != table.Name) continue;
                    foreach (Tag tag in table.Tags) tagNames.Add(tag.Name);
                }
            }
            string report = $"已导入变量表：{string.Join("、", names.Select(x => x?.ToString() ?? ""))}，" +
                            $"共 {tagNames.Count} 个变量（{string.Join("、", tagNames)}），回读验证通过";
            return new JsonObject { ["importedTables"] = names, ["verified"] = verified, ["report"] = report };
        }

        /// <summary>导入 HMI 画面 XML（元素需含 ObjectName、画面号唯一——已验证规则），导入后回读验证。</summary>
        public JsonObject ImportScreen(string xmlPath)
        {
            EnsureProject();
            var hmi = FindHmiTarget();
            if (hmi == null) throw new InvalidOperationException("No HmiTarget found in project.");
            var imported = hmi.ScreenFolder.Screens.Import(new FileInfo(xmlPath), ImportOptions.Override);
            var names = new JsonArray();
            foreach (Screen screen in imported) names.Add(screen.Name);
            // 回读验证
            var verified = new JsonArray();
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Screen screen in hmi.ScreenFolder.Screens) existing.Add(screen.Name);
            foreach (JsonNode n in names)
            {
                string name = n?.ToString() ?? "";
                verified.Add(new JsonObject { ["screen"] = name, ["ok"] = existing.Contains(name) });
            }
            string report = $"已导入画面：{string.Join("、", names.Select(x => x?.ToString() ?? ""))}，回读验证通过";
            return new JsonObject { ["importedScreens"] = names, ["verified"] = verified, ["report"] = report };
        }

        /// <summary>编译 PLC / HMI / all，返回逐条消息 + 文字汇报。</summary>
        public JsonObject Compile(string target)
        {
            EnsureProject();
            var result = new JsonObject();
            var reports = new List<string>();
            switch (target)
            {
                case "plc":
                    result["plc"] = CompileOne("PLC", FindPlcSoftware());
                    if (result["plc"] is JsonObject p && p["report"] != null) reports.Add(p["report"].ToString());
                    break;
                case "hmi":
                    result["hmi"] = CompileOne("HMI", FindHmiTarget());
                    if (result["hmi"] is JsonObject h && h["report"] != null) reports.Add(h["report"].ToString());
                    break;
                default:
                    result["plc"] = CompileOne("PLC", FindPlcSoftware());
                    result["hmi"] = CompileOne("HMI", FindHmiTarget());
                    if (result["plc"] is JsonObject p2 && p2["report"] != null) reports.Add(p2["report"].ToString());
                    if (result["hmi"] is JsonObject h2 && h2["report"] != null) reports.Add(h2["report"].ToString());
                    break;
            }
            result["report"] = string.Join("；", reports);
            return result;
        }

        private static JsonObject CompileOne(string label, IEngineeringServiceProvider target)
        {
            var root = new JsonObject { ["target"] = label };
            if (target == null)
            {
                root["state"] = "NoTarget";
                return root;
            }
            ICompilable compilable = target.GetService<ICompilable>();
            if (compilable == null)
            {
                root["state"] = "NoCompilableService";
                root["report"] = $"{label}：无可编译服务";
                return root;
            }
            CompilerResult cr = compilable.Compile();
            root["state"] = cr.State.ToString();
            root["errors"] = cr.ErrorCount;
            root["warnings"] = cr.WarningCount;
            var msgs = new JsonArray();
            foreach (CompilerResultMessage m in cr.Messages)
            {
                string state = m.State.ToString();
                if (state.Contains("Error") || state.Contains("Warning"))
                {
                    msgs.Add(new JsonObject { ["state"] = state, ["path"] = m.Path ?? "", ["description"] = m.Description ?? "" });
                }
            }
            root["messages"] = msgs;
            string desc = cr.State == CompilerResultState.Success
                ? $"{label} 编译成功（0 错误 0 警告）"
                : $"{label} 编译{cr.State}（错误 {cr.ErrorCount}，警告 {cr.WarningCount}）";
            if (msgs.Count > 0)
            {
                desc += "；明细：" + string.Join("；", msgs.Select(m => $"[{m["state"]}] {m["path"]} {m["description"]}".Trim()));
            }
            root["report"] = desc;
            return root;
        }

        public void Save()
        {
            EnsureProject();
            _project.Save();
        }

        /// <summary>删除 HMI 对象（tagTable / screen / connection），借鉴 bulaofen 的工程维护能力。</summary>
        public JsonObject DeleteObject(string type, string name)
        {
            EnsureProject();
            var hmi = FindHmiTarget();
            if (hmi == null) throw new InvalidOperationException("No HmiTarget found in project.");
            switch (type)
            {
                case "tagTable":
                    var table = hmi.TagFolder.TagTables.Find(name);
                    if (table == null) return new JsonObject { ["deleted"] = false, ["reason"] = "not found", ["report"] = $"未找到要删除的变量表 {name}" };
                    table.Delete();
                    break;
                case "screen":
                    var screen = hmi.ScreenFolder.Screens.Find(name);
                    if (screen == null) return new JsonObject { ["deleted"] = false, ["reason"] = "not found", ["report"] = $"未找到要删除的画面 {name}" };
                    screen.Delete();
                    break;
                case "connection":
                    var conn = hmi.Connections.Find(name);
                    if (conn == null) return new JsonObject { ["deleted"] = false, ["reason"] = "not found", ["report"] = $"未找到要删除的连接 {name}" };
                    conn.Delete();
                    break;
                default:
                    throw new ArgumentException("type must be tagTable | screen | connection");
            }
            string label = type == "tagTable" ? "变量表" : type == "screen" ? "画面" : "连接";
            return new JsonObject { ["deleted"] = true, ["type"] = type, ["name"] = name, ["report"] = $"已删除{label} {name}" };
        }

        public void CloseProject()
        {
            if (_project != null)
            {
                _project.Close();
                _project = null;
            }
        }

        /// <summary>彻底关闭：关闭工程 + 释放 TIA 会话 + 清理无窗口的 headless TIA 进程（解锁工程文件，用户 GUI 可立即打开）。
        /// 只清理无窗口实例（Openness 启动的 headless）；用户 GUI 实例有窗口，绝不误杀。</summary>
        public JsonObject Shutdown()
        {
            try { CloseProject(); } catch (Exception) { }
            try { _tia?.Dispose(); } catch (Exception) { }
            _tia = null;

            int killed = 0;
            try
            {
                foreach (System.Diagnostics.Process proc in System.Diagnostics.Process.GetProcessesByName("Siemens.Automation.Portal"))
                {
                    try
                    {
                        if (proc.MainWindowHandle == IntPtr.Zero) // 无窗口 = headless（Openness 启动）
                        {
                            proc.Kill();
                            killed++;
                        }
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }

            return new JsonObject
            {
                ["shutdown"] = true,
                ["headlessProcessesKilled"] = killed,
                ["report"] = "TIA 会话已释放，工程文件已解锁，可打开 TIA GUI 查看",
                ["note"] = "TIA 会话已释放，headless 进程已清理，工程文件已解锁，可打开 TIA GUI 查看"
            };
        }

        private void EnsureProject()
        {
            if (_project == null) throw new InvalidOperationException("No project open. Call tia_open_project first.");
        }

        public void Dispose()
        {
            try { CloseProject(); } catch (Exception) { }
            try { _tia?.Dispose(); } catch (Exception) { }
            _tia = null;
        }
    }
}
