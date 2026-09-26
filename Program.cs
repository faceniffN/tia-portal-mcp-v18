using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using TiaMcpServer.ModelContextProtocol;

namespace TiaMcpServer
{
    internal static class Program
    {
        private static string GetStr(JsonObject args, string key, string fallback = null)
        {
            if (args != null && args[key] != null) return args[key].GetValue<string>();
            return fallback;
        }

        private static int Main(string[] args)
        {
            try
            {
                return Run(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FATAL: " + ex.GetType().FullName);
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                return 1;
            }
        }

        private static int Run(string[] args)
        {
            if (args.Length > 0 && (args[0] == "--help" || args[0] == "-h" || args[0] == "--version"))
            {
                Console.WriteLine("TiaMcpServer — TIA Portal V18 Openness MCP 服务器 (stdio)");
                Console.WriteLine("用法:");
                Console.WriteLine("  TiaMcpServer.exe              # 作为 MCP stdio 服务器运行");
                Console.WriteLine("  TiaMcpServer.exe --tools      # 打印工具清单（自检）");
                Console.WriteLine("  TiaMcpServer.exe --doctor     # 环境体检（TIA/API/.NET/用户组/授权）");
                Console.WriteLine("  TiaMcpServer.exe --config     # 一键写入 VS Code 的 MCP 配置");
                return 0;
            }

            if (args.Length > 0 && args[0] == "--tools")
            {
                foreach (var t in BuildTools(new TiaService())) Console.WriteLine(t.Name + " — " + t.Description);
                return 0;
            }

            if (args.Length > 0 && args[0] == "--doctor")
            {
                return Doctor();
            }

            if (args.Length > 0 && args[0] == "--config")
            {
                return WriteConfig();
            }

            using (var service = new TiaService())
            {
                var server = new McpServer(Console.In, Console.Out);
                foreach (var tool in BuildTools(service)) server.Register(tool);
                server.Run();
            }
            return 0;
        }

        private static List<ToolDef> BuildTools(TiaService tia)
        {
            var tools = new List<ToolDef>();

            tools.Add(new ToolDef
            {
                Name = "tia_open_project",
                Description = "打开 TIA Portal V18 工程（Openness 会话内）。返回工程设备/PLC/HMI/连接/画面/变量表状态。",
                InputSchema = Schema(new[] { Prop("projectPath", "工程 .ap18 文件绝对路径") }),
                Handler = args => tia.OpenProject(GetStr(args, "projectPath"))
            });

            tools.Add(new ToolDef
            {
                Name = "tia_project_status",
                Description = "查询当前打开工程的设备、PLC、HMI、连接（非集成可枚举）、画面、变量表。",
                InputSchema = Schema(),
                Handler = args => tia.Status()
            });

            tools.Add(new ToolDef
            {
                Name = "tia_list_connections",
                Description = "枚举 HMI 连接。注意：Openness 只支持非集成连接（集成连接不可枚举，官方限制）。",
                InputSchema = Schema(),
                Handler = args =>
                {
                    var hmi = tia.FindHmiTarget();
                    if (hmi == null) throw new InvalidOperationException("No HmiTarget found.");
                    return new JsonObject { ["connections"] = tia.ListConnections(hmi) };
                }
            });

            tools.Add(new ToolDef
            {
                Name = "tia_list_screens",
                Description = "列出 HMI 画面。",
                InputSchema = Schema(),
                Handler = args =>
                {
                    var hmi = tia.FindHmiTarget();
                    if (hmi == null) throw new InvalidOperationException("No HmiTarget found.");
                    return new JsonObject { ["screens"] = tia.ListScreens(hmi) };
                }
            });

            tools.Add(new ToolDef
            {
                Name = "tia_list_tag_tables",
                Description = "列出 HMI 变量表及其变量。",
                InputSchema = Schema(),
                Handler = args =>
                {
                    var hmi = tia.FindHmiTarget();
                    if (hmi == null) throw new InvalidOperationException("No HmiTarget found.");
                    return new JsonObject { ["tagTables"] = tia.ListTagTables(hmi) };
                }
            });

            tools.Add(new ToolDef
            {
                Name = "tia_import_tag_table",
                Description = "导入 HMI 标签表 XML。V18 已验证规则：非集成连接 + 绝对地址（LogicalAddress=%M0.0，无 ControllerTag）+ 标签名全局唯一 + Engineering version=V18。",
                InputSchema = Schema(new[] { Prop("xmlPath", "标签表 XML 文件绝对路径") }),
                Handler = args => tia.ImportTagTable(GetStr(args, "xmlPath"))
            });

            tools.Add(new ToolDef
            {
                Name = "tia_import_screen",
                Description = "导入 HMI 画面 XML。V18 已验证规则：每个元素必须有 ObjectName、画面号唯一、Button 不含 Enabled/Visible。",
                InputSchema = Schema(new[] { Prop("xmlPath", "画面 XML 文件绝对路径") }),
                Handler = args => tia.ImportScreen(GetStr(args, "xmlPath"))
            });

            tools.Add(new ToolDef
            {
                Name = "tia_compile",
                Description = "编译 PLC / HMI / all，返回状态与错误明细。",
                InputSchema = Schema(new[] { Prop("target", "plc | hmi | all（默认 all）", "all") }),
                Handler = args => tia.Compile(GetStr(args, "target", "all"))
            });

            tools.Add(new ToolDef
            {
                Name = "tia_save_project",
                Description = "保存当前工程。",
                InputSchema = Schema(),
                Handler = args => { tia.Save(); return new JsonObject { ["saved"] = true }; }
            });

            tools.Add(new ToolDef
            {
                Name = "tia_close_project",
                Description = "关闭当前工程（不退出 TIA 会话）。",
                InputSchema = Schema(),
                Handler = args => { tia.CloseProject(); return new JsonObject { ["closed"] = true }; }
            });

            tools.Add(new ToolDef
            {
                Name = "tia_delete_object",
                Description = "删除 HMI 对象：type 为 tagTable | screen | connection，name 为对象名（工程维护，借鉴 bulaofen 能力）。",
                InputSchema = Schema(new[] { Prop("type", "tagTable | screen | connection"), Prop("name", "对象名称") }),
                Handler = args => tia.DeleteObject(GetStr(args, "type"), GetStr(args, "name"))
            });

            tools.Add(new ToolDef
            {
                Name = "tia_shutdown",
                Description = "彻底关闭：关闭工程 + 释放 TIA 会话，解锁工程文件（用户可立即用 GUI 打开查看）。操作完成后的收尾调用。",
                InputSchema = Schema(),
                Handler = args => tia.Shutdown()
            });

            tools.Add(new ToolDef
            {
                Name = "hmi_build_package",
                Description = "离线生成 Classic HMI 资产（标签表+画面 XML+manifest）并归一化为 V18 版。输入 package JSON（Name/TagTable/ScreenDesign），输出到指定目录。",
                InputSchema = Schema(new[]
                {
                    Prop("packageJsonPath", "package JSON 文件绝对路径"),
                    Prop("outDir", "输出目录绝对路径")
                }),
                Handler = args =>
                {
                    string pkgPath = GetStr(args, "packageJsonPath");
                    string outDir = GetStr(args, "outDir");
                    if (!File.Exists(pkgPath)) throw new FileNotFoundException("package JSON not found: " + pkgPath);
                    var result = ClassicHmiMinimalPackageBuilder.WriteFiles(File.ReadAllText(pkgPath, Encoding.UTF8), outDir);
                    // 归一化 TagTable XML 为 V18
                    if (result["outputDirectory"] != null && result["files"] is JsonArray files)
                    {
                        foreach (var f in files)
                        {
                            string file = f?.ToString();
                            if (file != null && file.EndsWith("_TagTable.xml", StringComparison.OrdinalIgnoreCase) && File.Exists(file))
                            {
                                string xml = File.ReadAllText(file, Encoding.UTF8);
                                xml = xml.Replace("<Engineering version=\"V21\" />", "<Engineering version=\"V18\" />");
                                File.WriteAllText(file, xml, new UTF8Encoding(false));
                            }
                        }
                    }
                    return result;
                }
            });

            tools.Add(new ToolDef
            {
                Name = "hmi_validate_package",
                Description = "校验已生成的 HMI 包文件（tag 引用/边界/一致性）。",
                InputSchema = Schema(new[] { Prop("dir", "包目录绝对路径") }),
                Handler = args => ClassicHmiMinimalPackageBuilder.ValidateFiles(GetStr(args, "dir"))
            });

            tools.Add(new ToolDef
            {
                Name = "hmi_validate_plc_sync",
                Description = "校验 HMI 标签 ControllerTag 与 PLC 符号表同步。plcSymbolsJson 格式: {\"symbols\":[\"Start\",\"Stop\"]}",
                InputSchema = Schema(new[] { Prop("dir", "包目录绝对路径"), Prop("plcSymbolsJson", "PLC 符号 JSON 文件路径") }),
                Handler = args =>
                {
                    string dir = GetStr(args, "dir");
                    string symFile = GetStr(args, "plcSymbolsJson");
                    if (!File.Exists(symFile)) throw new FileNotFoundException("symbols file not found: " + symFile);
                    return ClassicHmiMinimalPackageBuilder.ValidateFilesWithPlcSymbols(dir, File.ReadAllText(symFile, Encoding.UTF8));
                }
            });

            return tools;
        }

        private static JsonObject Schema(JsonObject[] props = null)
        {
            var properties = new JsonObject();
            if (props != null)
            {
                foreach (var p in props)
                {
                    properties[p["_name"].GetValue<string>()] = p["_schema"].DeepClone();
                }
            }
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties
            };
        }

        private static JsonObject Prop(string name, string description, string defaultValue = null)
        {
            var schema = new JsonObject { ["type"] = "string", ["description"] = description };
            if (defaultValue != null) schema["default"] = defaultValue;
            return new JsonObject { ["_name"] = name, ["_schema"] = schema };
        }

        // ===== CLI 辅助：--doctor 环境体检（借鉴 bulaofen doctor 思路） =====

        private static int Doctor()
        {
            bool allOk = true;
            Console.WriteLine("== TIA Portal V18 Openness 环境体检 ==");

            // 1. TIA 安装目录
            string tiaRoot = @"D:\TIA\Portal V18";
            bool tiaOk = Directory.Exists(tiaRoot);
            Console.WriteLine((tiaOk ? "[OK] " : "[FAIL] ") + "TIA 安装目录: " + tiaRoot);
            allOk &= tiaOk;

            // 2. PublicAPI 程序集
            string apiDir = Path.Combine(tiaRoot, "PublicAPI", "V18");
            string engDll = Path.Combine(apiDir, "Siemens.Engineering.dll");
            string hmiDll = Path.Combine(apiDir, "Siemens.Engineering.Hmi.dll");
            bool engOk = File.Exists(engDll);
            bool hmiOk = File.Exists(hmiDll);
            Console.WriteLine((engOk ? "[OK] " : "[FAIL] ") + "Openness API: Siemens.Engineering.dll  " + (engOk ? engDll : "（缺失）"));
            Console.WriteLine((hmiOk ? "[OK] " : "[FAIL] ") + "Openness API: Siemens.Engineering.Hmi.dll  " + (hmiOk ? hmiDll : "（缺失，HMI 标签局部类型不可用）"));
            allOk &= engOk && hmiOk;

            // 3. .NET Framework 4.8
            int release = 0;
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                {
                    if (key != null) release = Convert.ToInt32(key.GetValue("Release", 0));
                }
            }
            catch (Exception) { }
            bool netOk = release >= 528040;
            Console.WriteLine((netOk ? "[OK] " : "[FAIL] ") + ".NET Framework 4.8: Release=" + release + (netOk ? "（满足）" : "（需 ≥528040）"));
            allOk &= netOk;

            // 4. Siemens TIA Openness 用户组
            bool inGroup = false;
            try
            {
                string who = RunProcess("whoami", "/groups");
                inGroup = who.IndexOf("Siemens TIA Openness", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch (Exception) { }
            Console.WriteLine((inGroup ? "[OK] " : "[WARN] ") + "用户组 Siemens TIA Openness: " + (inGroup ? "当前用户已加入（免弹窗前提，仍建议 GUI 授权一次）" : "当前用户未加入/未生效（注销重登生效；GUI 首次弹窗点'始终允许'也可）"));
            allOk &= inGroup;

            // 5. Openness 授权记录
            bool auth = false;
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Siemens\Automation\Openness"))
                {
                    auth = key != null;
                }
            }
            catch (Exception) { }
            Console.WriteLine((auth ? "[OK] " : "[WARN] ") + "Openness 授权记录: " + (auth ? "已存在（曾授权）" : "无——首次连接 TIA 会弹授权窗，勾'始终允许'一次即可"));

            Console.WriteLine();
            Console.WriteLine(allOk ? "体检通过：环境满足运行条件。" : "体检完成：有 FAIL 项需处理（见上）。");
            return allOk ? 0 : 1;
        }

        // ===== CLI 辅助：--config 一键写入 VS Code MCP 配置（借鉴 bulaofen 配置MCP.bat） =====

        private static int WriteConfig()
        {
            string exePath = typeof(Program).Assembly.Location;
            string configJson = "{\n  \"servers\": {\n    \"tia-v18\": {\n      \"type\": \"stdio\",\n      \"command\": \"" + exePath.Replace("\\", "\\\\") + "\",\n      \"args\": []\n    }\n  }\n}\n";

            int written = 0;

            // 用户级
            string userDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Code", "User");
            string userCfg = Path.Combine(userDir, "mcp.json");
            try
            {
                if (File.Exists(userCfg) && !File.Exists(userCfg + ".bak"))
                    File.Copy(userCfg, userCfg + ".bak");
                Directory.CreateDirectory(userDir);
                File.WriteAllText(userCfg, configJson, new System.Text.UTF8Encoding(false));
                Console.WriteLine("[OK] 已写入用户级配置: " + userCfg);
                written++;
            }
            catch (Exception ex) { Console.WriteLine("[FAIL] 用户级配置: " + ex.Message); }

            // 项目级（当前工作目录）
            string projCfg = Path.Combine(Directory.GetCurrentDirectory(), ".vscode", "mcp.json");
            try
            {
                if (File.Exists(projCfg) && !File.Exists(projCfg + ".bak"))
                    File.Copy(projCfg, projCfg + ".bak");
                Directory.CreateDirectory(Path.GetDirectoryName(projCfg));
                File.WriteAllText(projCfg, configJson, new System.Text.UTF8Encoding(false));
                Console.WriteLine("[OK] 已写入项目级配置: " + projCfg);
                written++;
            }
            catch (Exception ex) { Console.WriteLine("[FAIL] 项目级配置: " + ex.Message); }

            Console.WriteLine(written > 0 ? "完成：重启 VS Code 后在 Copilot Chat 中选择 MCP 服务器 tia-v18。" : "失败：未能写入任何配置。");
            return written > 0 ? 0 : 1;
        }

        private static string RunProcess(string fileName, string arguments)
        {
            using (var p = new System.Diagnostics.Process())
            {
                p.StartInfo.FileName = fileName;
                p.StartInfo.Arguments = arguments;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.CreateNoWindow = true;
                p.Start();
                return p.StandardOutput.ReadToEnd();
            }
        }
    }
}
