using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Hmi;
using Siemens.Engineering.Hmi.Screen;
using Siemens.Engineering.SW;

namespace TiaProbe
{
    class Program
    {
        static void Main(string[] args)
        {
            try { Run(args); }
            catch (Exception ex)
            {
                Console.WriteLine("!! 异常: " + ex);
            }
        }

        static void Run(string[] args)
        {
            string publicApi = @"D:\TIA\Portal V18\PublicAPI\V18";
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                var name = new System.Reflection.AssemblyName(e.Name);
                string dll = Path.Combine(publicApi, name.Name + ".dll");
                return File.Exists(dll) ? System.Reflection.Assembly.LoadFrom(dll) : null;
            };
            string mainDll = Path.Combine(publicApi, "Siemens.Engineering.dll");
            if (File.Exists(mainDll)) System.Reflection.Assembly.LoadFrom(mainDll);
            if (args.Contains("--reflect")) { Reflect(args); return; }
            Probe(args);
        }

        static void DumpGroup(Siemens.Engineering.SW.Blocks.PlcBlockGroup group, int depth, string outDir, bool exportRead)
        {
            string pad = new string(' ', depth * 2);
            Console.WriteLine(pad + "组: " + group.Name);
            foreach (var g in group.Groups) DumpGroup(g, depth + 1, outDir, exportRead);
            foreach (var b in group.Blocks)
            {
                Console.WriteLine(pad + " BLOCK: " + b.Name + " [" + b.GetType().Name + "]");
                if (exportRead)
                {
                    string f = Path.Combine(outDir, b.Name + ".xml");
                    try { b.Export(new FileInfo(f), ExportOptions.WithDefaults); Console.WriteLine(pad + " 已导出 -> " + f); }
                    catch (Exception ex) { Console.WriteLine(pad + " 导出失败: " + ex.Message); }
                }
            }
        }

        static string GetProp(object o, string n)
        {
            try { var p = o.GetType().GetProperty(n); return p == null ? "(无属性)" : (p.GetValue(o, null)?.ToString() ?? ""); }
            catch (Exception ex) { return "(err:" + ex.Message + ")"; }
        }

        static void DumpHw(object item, int depth)
        {
            string pad = new string(' ', depth * 2);
            string name = GetProp(item, "Name");
            string typeId = GetProp(item, "TypeIdentifier");
            string pos = GetProp(item, "PositionNumber");
            Console.WriteLine(pad + "ITEM: " + name + " 类型=" + typeId + " 位置=" + pos);
            try
            {
                var addrComp = item.GetType().GetProperty("Addresses")?.GetValue(item, null);
                if (addrComp != null)
                {
                    foreach (var a in (System.Collections.IEnumerable)addrComp)
                    {
                        var at = a.GetType();
                        string props = string.Join(" | ", at.GetProperties().Select(p => p.Name + "=" + (p.GetValue(a, null)?.ToString() ?? "")));
                        Console.WriteLine(pad + "  ADDR: " + props);
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine(pad + "  !IO地址失败: " + ex.Message); }
            try
            {
                var subs = (System.Collections.IEnumerable)item.GetType().GetProperty("DeviceItems").GetValue(item, null);
                foreach (var sub in subs) DumpHw(sub, depth + 1);
            }
            catch (Exception ex) { Console.WriteLine(pad + "  !子项失败: " + ex.Message); }
        }

        static void Reflect(string[] args)
        {
            var asm = typeof(TiaPortal).Assembly;
            using (var portal = new TiaPortal(TiaPortalMode.WithoutUserInterface))
            {
                try
                {
                    var hc = portal.HardwareCatalog;
                    Console.WriteLine("== HardwareCatalog 运行时类型: " + (hc == null ? "null" : hc.GetType().FullName));
                    if (hc != null)
                    {
                        foreach (var p in hc.GetType().GetProperties()) Console.WriteLine("  属性: " + p.PropertyType.Name + " " + p.Name + " set=" + p.CanWrite);
                        foreach (var m in hc.GetType().GetMethods().Where(m => m.DeclaringType == hc.GetType()))
                        {
                            var ps = string.Join(", ", m.GetParameters().Select(pp => pp.ParameterType.Name + " " + pp.Name));
                            Console.WriteLine("  方法: " + m.ReturnType.Name + " " + m.Name + "(" + ps + ")");
                        }
                    }
                    var gl = portal.GlobalLibraries;
                    Console.WriteLine("== GlobalLibraries 运行时类型: " + (gl == null ? "null" : gl.GetType().FullName));
                    if (gl != null)
                    {
                        foreach (var p in gl.GetType().GetProperties()) Console.WriteLine("  属性: " + p.PropertyType.Name + " " + p.Name + " set=" + p.CanWrite);
                        foreach (var m in gl.GetType().GetMethods().Where(m => m.DeclaringType == gl.GetType()))
                        {
                            var ps = string.Join(", ", m.GetParameters().Select(pp => pp.ParameterType.Name + " " + pp.Name));
                            Console.WriteLine("  方法: " + m.ReturnType.Name + " " + m.Name + "(" + ps + ")");
                        }
                    }
                }
                catch (Exception ex) { Console.WriteLine("  HardwareCatalog 访问失败: " + ex.Message); }
            }
            string[] want = { "Siemens.Engineering.SW.Tags.PlcTag", "Siemens.Engineering.SW.Tags.PlcTagComposition", "Siemens.Engineering.SW.Tags.PlcTagTable", "Siemens.Engineering.SW.Tags.PlcTagTableGroup", "Siemens.Engineering.HW.Address", "Siemens.Engineering.HW.AddressComposition", "Siemens.Engineering.HW.DeviceItem", "Siemens.Engineering.HW.DeviceItemComposition", "Siemens.Engineering.Library.MasterCopies.MasterCopy", "Siemens.Engineering.Library.MasterCopies.MasterCopyComposition", "Siemens.Engineering.Library.MasterCopies.MasterCopySystemFolder", "Siemens.Engineering.TiaPortal", "Siemens.Engineering.Library.GlobalLibrary", "Siemens.Engineering.HW.HardwareCatalog.CatalogEntry", "Siemens.Engineering.Project", "Siemens.Engineering.Library.ProjectLibrary" };            
            var ceType = asm.GetType("Siemens.Engineering.HW.HardwareCatalog.CatalogEntry");
            if (ceType != null)
            {
                Console.WriteLine("== CatalogEntry 接口:");
                foreach (var i in ceType.GetInterfaces()) Console.WriteLine("   " + i.FullName);
            }
            var glType = typeof(TiaPortal).Assembly.GetType("Siemens.Engineering.GlobalLibraryComposition");
            Console.WriteLine("== GlobalLibraryComposition 类型: " + (glType == null ? "null(在其它程序集)" : glType.FullName));
            if (glType != null)
            {
                Console.WriteLine("-- 方法:");
                foreach (var m in glType.GetMethods().Where(m => m.DeclaringType == glType))
                {
                    var ps = string.Join(", ", m.GetParameters().Select(pp => pp.ParameterType.Name + " " + pp.Name));
                    Console.WriteLine("   " + m.ReturnType.Name + " " + m.Name + "(" + ps + ")");
                }
            }
            foreach (var w in want)
            {
                var t = asm.GetType(w);
                if (t == null) { Console.WriteLine("== 未找到: " + w); continue; }
                Console.WriteLine("===== " + t.FullName + " 基类=" + t.BaseType?.FullName);
                Console.WriteLine("-- 属性:");
                foreach (var p in t.GetProperties()) Console.WriteLine("   " + p.PropertyType.Name + " " + p.Name + " set=" + p.CanWrite);
                Console.WriteLine("-- 方法:");
                foreach (var m in t.GetMethods().Where(m => m.DeclaringType == t))
                {
                    var ps = string.Join(", ", m.GetParameters().Select(pp => pp.ParameterType.Name + " " + pp.Name));
                    Console.WriteLine("   " + m.ReturnType.Name + " " + m.Name + "(" + ps + ")");
                }
            }
            // 枚举所有含 BlockType 的枚举类型
            Type[] allTypes;
            try { allTypes = asm.GetTypes(); }
            catch (System.Reflection.ReflectionTypeLoadException ex) { allTypes = ex.Types.Where(x => x != null).ToArray(); }
            foreach (var t in allTypes.Where(x => x.IsEnum && x.Name.Contains("BlockType")))
            {
                Console.WriteLine("===== 枚举 " + t.FullName + ": " + string.Join(", ", Enum.GetNames(t)));
            }
        }

        static object FindMasterCopy(object folder, string orderPrefix)
        {
            if (folder == null) return null;
            var ft = folder.GetType();
            try
            {
                var mcsProp = ft.GetProperty("MasterCopies");
                if (mcsProp != null)
                {
                    var mcs = mcsProp.GetValue(folder, null);
                    if (mcs != null)
                        foreach (var mc in (System.Collections.IEnumerable)mcs)
                        {
                            string n = GetProp(mc, "Name");
                            if (n != null && n.StartsWith("OrderNumber:" + orderPrefix)) return mc;
                        }
                }
            }
            catch (Exception) { }
            try
            {
                var foldersProp = ft.GetProperty("Folders");
                if (foldersProp != null)
                {
                    var folders = foldersProp.GetValue(folder, null);
                    if (folders != null)
                        foreach (var f in (System.Collections.IEnumerable)folders)
                        {
                            var r = FindMasterCopy(f, orderPrefix);
                            if (r != null) return r;
                        }
                }
            }
            catch (Exception) { }
            return null;
        }

        static object FindDeviceItem(object container, string name)
        {
            if (container == null) return null;
            try
            {
                var itemsProp = container.GetType().GetProperty("DeviceItems");
                if (itemsProp == null) return null;
                foreach (var di in (System.Collections.IEnumerable)itemsProp.GetValue(container, null))
                {
                    if (GetProp(di, "Name") == name) return di;
                    var r = FindDeviceItem(di, name);
                    if (r != null) return r;
                }
            }
            catch (Exception) { }
            return null;
        }

        static void Probe(string[] args)
        {
            string projectPath = @"C:\Users\Administrator\Doubao\chats\2026-09-24\new-chat\Test01\TEST01\TEST01.ap18";
            bool doTemplate = false, doSave = false, doExport = false, doApply = false, doBlocks = false, doPlcTags = false, doImportBlock = false, doDeleteBlock = false, doCompilePlc = false, doImportAll = false, doRestoreGroups = false, doHw = false, doAddTags = false, doAddModule = false, doModuleInfo = false, doImportToGroup = false;
            string importXml = null, deleteBlockName = null, importDir = null, addTagsJson = null, addModuleJson = null, importGroup = null;
            foreach (var a in args)
            {
                if (a == "--template") doTemplate = true;
                else if (a == "--save") doSave = true;
                else if (a == "--export") doExport = true;
                else if (a == "--apply") doApply = true;
                else if (a == "--blocks") doBlocks = true;
                else if (a == "--plc-tags") doPlcTags = true;
                else if (a == "--import-block") doImportBlock = true;
                else if (a == "--delete-block") doDeleteBlock = true;
                else if (a == "--compile-plc") doCompilePlc = true;
                else if (a == "--import-all") doImportAll = true;
                else if (a == "--restore-groups") doRestoreGroups = true;
                else if (a == "--hw") doHw = true;
                else if (a == "--add-tags") doAddTags = true;
                else if (a == "--add-module") doAddModule = true;
                else if (a == "--module-info") doModuleInfo = true;
                else if (a == "--import-to-group") doImportToGroup = true;
                else if (!a.StartsWith("--")) { if (doImportBlock) importXml = a; else if (doImportAll) importDir = a; else if (doAddTags) addTagsJson = a; else if (doAddModule) addModuleJson = a; else if (doImportToGroup) { if (importGroup == null) importGroup = a; else importXml = a; } else if (doDeleteBlock && deleteBlockName == null) deleteBlockName = a; else projectPath = a; }
            }
            using (var portal = new TiaPortal(TiaPortalMode.WithoutUserInterface))
            {
                Console.WriteLine("== 打开工程 ==");
                var project = portal.Projects.Open(new FileInfo(projectPath));
                Console.WriteLine("  工程: " + project.Name);
                var devices = project.Devices;
                foreach (var dev in devices)
                {
                    Console.WriteLine("  设备: " + dev.Name);
                    foreach (var item in dev.DeviceItems)
                    {
                        var container = item.GetService<SoftwareContainer>();
                        if (container?.Software is PlcSoftware plc)
                        {
                            Console.WriteLine("  PLC: " + plc.Name);
                            if (doBlocks)
                            {
                                string outDir = @"C:\Users\Administrator\Doubao\chats\2026-09-24\new-chat\Test01Assets\plc_blocks";
                                Directory.CreateDirectory(outDir);
                                foreach (var g in plc.BlockGroup.Groups) DumpGroup(g, 3, outDir, doBlocks);
                                foreach (var b in plc.BlockGroup.Blocks)
                                {
                                    Console.WriteLine("   BLOCK(顶层): " + b.Name + " [" + b.GetType().Name + "]");
                                    string f = Path.Combine(outDir, b.Name + ".xml");
                                    try { b.Export(new FileInfo(f), ExportOptions.WithDefaults); Console.WriteLine("   已导出 -> " + f); }
                                    catch (Exception ex) { Console.WriteLine("   导出失败: " + ex.Message); }
                                }
                            }
                            if (doPlcTags)
                            {
                                Console.WriteLine("  --- PLC 变量表 ---");
                                try
                                {
                                    foreach (var t in plc.TagTableGroup.TagTables)
                                    {
                                        Console.WriteLine("   TABLE: " + t.Name);
                                        try
                                        {
                                            foreach (var tag in t.Tags)
                                            {
                                                string addr = "";
                                                try { addr = tag.LogicalAddress?.ToString() ?? ""; } catch (Exception) { }
                                                string dt = "";
                                                try { dt = tag.DataTypeName ?? ""; } catch (Exception) { }
                                                Console.WriteLine("     TAG: " + tag.Name + " | " + addr + " | " + dt);
                                            }
                                        }
                                        catch (Exception ex) { Console.WriteLine("     !读取 Tags 失败: " + ex.Message); }
                                    }
                                }
                                catch (Exception ex) { Console.WriteLine("  !枚举 TagTables 失败: " + ex.Message); }
                            }
                            if (doImportBlock && importXml != null)
                            {
                                try
                                {
                                    var imported = plc.BlockGroup.Blocks.Import(new FileInfo(importXml), ImportOptions.Override);
                                    foreach (var b in imported) Console.WriteLine("  导入块成功: " + b.Name);
                                }
                                catch (Exception ex) { Console.WriteLine("  导入块失败: " + ex.Message); }
                                try
                                {
                                    var comp = plc.GetService<Siemens.Engineering.Compiler.ICompilable>();
                                    if (comp != null)
                                    {
                                        var cr = comp.Compile();
                                        Console.WriteLine("  PLC 编译: " + cr.State + "（错误 " + cr.ErrorCount + "，警告 " + cr.WarningCount + "）");
                                        foreach (var m in cr.Messages)
                                        {
                                            Console.WriteLine("    [" + m.State + "] " + m.Path + " | " + m.Description);
                                        }
                                    }
                                }
                                catch (Exception ex) { Console.WriteLine("  PLC 编译异常: " + ex.Message); }
                            }
                            if (doDeleteBlock && deleteBlockName != null)
                            {
                                try
                                {
                                    var blk = plc.BlockGroup.Blocks.Find(deleteBlockName);
                                    if (blk == null)
                                    {
                                        foreach (var g in plc.BlockGroup.Groups)
                                        {
                                            blk = g.Blocks.Find(deleteBlockName);
                                            if (blk != null) { Console.WriteLine("  在组[" + g.Name + "]找到块: " + deleteBlockName); break; }
                                        }
                                    }
                                    if (blk != null) { blk.Delete(); Console.WriteLine("  已删除块: " + deleteBlockName); }
                                    else Console.WriteLine("  块不存在: " + deleteBlockName);
                                }
                                catch (Exception ex) { Console.WriteLine("  删除块失败: " + ex.Message); }
                            }
                            if (doCompilePlc)
                            {
                                try
                                {
                                    var comp = plc.GetService<Siemens.Engineering.Compiler.ICompilable>();
                                    if (comp != null)
                                    {
                                        var cr = comp.Compile();
                                        Console.WriteLine("  PLC 编译: " + cr.State + "（错误 " + cr.ErrorCount + "，警告 " + cr.WarningCount + "）");
                                        foreach (var m in cr.Messages)
                                        {
                                            Console.WriteLine("    [" + m.State + "] " + m.Path + " | " + m.Description);
                                        }
                                    }
                                }
                                catch (Exception ex) { Console.WriteLine("  PLC 编译异常: " + ex.Message); }
                            }
                            if (doHw)
                            {
                                Console.WriteLine("  --- PLC 硬件组态 ---");
                                try
                                {
                                    foreach (var hwItem in dev.DeviceItems) DumpHw(hwItem, 2);
                                }
                                catch (Exception ex) { Console.WriteLine("  !枚举硬件失败: " + ex.Message); }
                            }
                            if (doAddTags && addTagsJson != null)
                            {
                                try
                                {
                                    var ser = new JavaScriptSerializer();
                                    var list = ser.Deserialize<List<Dictionary<string, string>>>(File.ReadAllText(addTagsJson));
                                    var table = plc.TagTableGroup.TagTables[0];
                                    Console.WriteLine("  目标变量表: " + table.Name);
                                    var compType = table.Tags.GetType();
                                    var createM = compType.GetMethod("Create", new Type[] { typeof(string) });
                                    var createM3 = compType.GetMethod("Create", new Type[] { typeof(string), typeof(string), typeof(string) });
                                    foreach (var tagItem in list)
                                    {
                                        string name = tagItem["Name"], addr = tagItem["Address"], dt = tagItem["DataType"];
                                        // 已存在则更新地址/类型
                                        object existing = null;
                                        try { existing = table.Tags.Find(name); } catch (Exception) { }
                                        if (existing != null)
                                        {
                                            try
                                            {
                                                existing.GetType().GetProperty("LogicalAddress").SetValue(existing, addr, null);
                                                existing.GetType().GetProperty("DataTypeName").SetValue(existing, dt, null);
                                                Console.WriteLine("  已更新: " + name + " | " + addr + " | " + dt);
                                            }
                                            catch (Exception ex) { Console.WriteLine("  更新失败(" + name + "): " + ex.Message.Substring(0, Math.Min(120, ex.Message.Length))); }
                                            continue;
                                        }
                                        object nt = null;
                                        if (createM3 != null)
                                        {
                                            // 三参顺序猜测: (name, dataTypeName, logicalAddress)；失败则回退
                                            try { nt = createM3.Invoke(table.Tags, new object[] { name, dt, addr }); }
                                            catch (Exception ex) { Console.WriteLine("  三参Create失败(" + name + "): " + ex.Message.Substring(0, Math.Min(120, ex.Message.Length))); }
                                        }
                                        if (nt == null && createM != null)
                                        {
                                            nt = createM.Invoke(table.Tags, new object[] { name });
                                            var ntType = nt.GetType();
                                            ntType.GetProperty("LogicalAddress").SetValue(nt, addr, null);
                                            ntType.GetProperty("DataTypeName").SetValue(nt, dt, null);
                                        }
                                        if (nt == null) { Console.WriteLine("  !无可用 Create 方法"); return; }
                                        Console.WriteLine("  已添加: " + name + " | " + addr + " | " + dt);
                                    }
                                }
                                catch (Exception ex) { Console.WriteLine("  !添加变量失败: " + ex.Message); }
                            }
                            if (doModuleInfo)
                            {
                                Console.WriteLine("  --- 模块信息 ---");
                                try
                                {
                                    var hwUtils = project.GetType().GetProperty("HwUtilities")?.GetValue(project, null);
                                    var mip = hwUtils?.GetType().GetMethod("Find")?.Invoke(hwUtils, new object[] { "ModuleInformationProvider" });
                                    string[] orders = { "6ES7 222-1BF32-0XB0", "6ES7 231-5HD32-0XB0", "6ES7 231-5HF32-0XB0", "6ES7 231-5ND32-0XB0", "6ES7 231-1HD32-0XB0", "6ES7 231-4HA30-0XB0" };
                                    if (mip != null)
                                    {
                                        var fm = mip.GetType().GetMethod("FindModuleTypes", new Type[] { typeof(string) });
                                        foreach (var o in orders)
                                        {
                                            try
                                            {
                                                var types = (System.Collections.IEnumerable)fm.Invoke(mip, new object[] { "OrderNumber:" + o });
                                                var list = new List<string>();
                                                foreach (var t in types) list.Add(t.ToString());
                                                Console.WriteLine("  " + o + " → " + (list.Count > 0 ? string.Join(", ", list) : "(目录无)"));
                                            }
                                            catch (Exception ex)
                                            {
                                                // 打印内部异常原因
                                                string msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                                                Console.WriteLine("  " + o + " → 查询失败: " + msg);
                                            }
                                        }
                                    // 额外用 HardwareCatalog.Find 直接验证 4AI 是否存在
                                    var hcFind = portal.HardwareCatalog.GetType().GetMethod("Find", new Type[] { typeof(string) });
                                    foreach (var o in orders)
                                    {
                                        try
                                        {
                                            var found = hcFind.Invoke(portal.HardwareCatalog, new object[] { "OrderNumber:" + o });
                                            var cnt = 0; string tids = "";
                                            foreach (var e in (System.Collections.IEnumerable)found) { cnt++; tids += (cnt > 1 ? ", " : "") + GetProp(e, "TypeIdentifier") + " [" + GetProp(e, "TypeName") + "]"; }
                                            Console.WriteLine("  Find(" + o + ") → " + cnt + " 条: " + tids);
                                        }
                                        catch (Exception ex) { Console.WriteLine("  Find(" + o + ") 异常: " + ex.Message); }
                                    }
                                    }
                                    else Console.WriteLine("  !ModuleInformationProvider 不可用");
                                    var plcItem = FindDeviceItem(dev, "PLC_1");
                                    var rackItem = FindDeviceItem(dev, "Rack_0");
                                    foreach (var host in new[] { plcItem, rackItem })
                                    {
                                        if (host == null) continue;
                                        Console.WriteLine("  == 容器: " + GetProp(host, "Name"));
                                        var gl = host.GetType().GetMethod("GetPlugLocations")?.Invoke(host, null);
                                        if (gl != null)
                                        {
                                            foreach (var pl in (System.Collections.IEnumerable)gl)
                                            {
                                                Console.WriteLine("    可用槽: " + string.Join(" | ", pl.GetType().GetProperties().Select(p => p.Name + "=" + (p.GetValue(pl, null)?.ToString() ?? ""))));
                                            }
                                        }
                                        var cpn = host.GetType().GetMethod("CanPlugNew", new Type[] { typeof(string), typeof(string), typeof(int) });
                                        if (cpn != null)
                                        {
                                            foreach (var o in orders)
                                            {
                                                try { Console.WriteLine("    CanPlugNew(" + o + "/V2.0@3): " + cpn.Invoke(host, new object[] { "OrderNumber:" + o + "/V2.0", "T_" + o, 3 })); }
                                                catch (Exception ex) { Console.WriteLine("    CanPlugNew(" + o + ") 异常: " + ex.Message); }
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex) { Console.WriteLine("  !模块信息失败: " + ex.Message); }
                            }
                            if (doAddModule && addModuleJson != null)
                            {
                                try
                                {
                                    var ser = new JavaScriptSerializer();
                                    var mods = ser.Deserialize<List<Dictionary<string, string>>>(File.ReadAllText(addModuleJson));
                                    var host = FindDeviceItem(dev, "Rack_0") ?? FindDeviceItem(dev, "PLC_1");
                                    if (host == null) { Console.WriteLine("  !未找到机架"); return; }
                                    Console.WriteLine("  目标容器: " + GetProp(host, "Name"));
                                    var createM = host.GetType().GetProperty("DeviceItems").GetValue(host, null).GetType().GetMethod("CreateFrom");
                                    var hc = portal.HardwareCatalog;
                                    var findM = hc == null ? null : hc.GetType().GetMethod("Find", new Type[] { typeof(string) });
                                    var cpn = host.GetType().GetMethod("CanPlugNew", new Type[] { typeof(string), typeof(string), typeof(int) });
                                    var m3 = host.GetType().GetMethod("PlugNew", new Type[] { typeof(string), typeof(string), typeof(int) });
                                    if (m3 == null) { Console.WriteLine("  !无 PlugNew 方法"); return; }
                                    foreach (var mod in mods)
                                    {
                                        string order = mod["Order"];
                                        string typeId = "OrderNumber:" + order;
                                        // 从目录拿完整 TypeIdentifier（可能带固件版本）
                                        if (findM != null)
                                        {
                                            var found = findM.Invoke(hc, new object[] { typeId });
                                            foreach (var e in (System.Collections.IEnumerable)found)
                                            {
                                                string tid = GetProp(e, "TypeIdentifier");
                                                if (!string.IsNullOrEmpty(tid) && !tid.Contains("(无属性)")) { typeId = tid; break; }
                                            }
                                        }
                                        string mname = mod.ContainsKey("Name") ? mod["Name"] : (order + "_1");
                                        int pos = mod.ContainsKey("Position") ? int.Parse(mod["Position"]) : 3;
                                        if (cpn != null)
                                        {
                                            bool ok = false;
                                            try { ok = (bool)cpn.Invoke(host, new object[] { typeId, mname, pos }); }
                                            catch (Exception iex) { Console.WriteLine("  !CanPlugNew异常(" + typeId + "): " + (iex.InnerException != null ? iex.InnerException.Message : iex.Message)); continue; }
                                            if (!ok) { Console.WriteLine("  !CanPlugNew=False(" + typeId + " @" + pos + ")，跳过"); continue; }
                                        }
                                        try
                                        {
                                            var ni = m3.Invoke(host, new object[] { typeId, mname, pos });
                                            Console.WriteLine("  已插模块: " + GetProp(ni, "Name") + " " + GetProp(ni, "TypeIdentifier") + " 位置=" + GetProp(ni, "PositionNumber"));
                                        }
                                        catch (Exception iex)
                                        {
                                            string msg = iex.InnerException != null ? iex.InnerException.Message : iex.Message;
                                            Console.WriteLine("  !PlugNew失败(" + typeId + " 位置" + pos + "): " + msg);
                                        }
                                    }
                                }
                                catch (Exception ex) { Console.WriteLine("  !添加模块失败: " + ex.Message); }
                            }
                            if (doImportToGroup && importGroup != null && importXml != null)
                            {
                                try
                                {
                                    var g = plc.BlockGroup.Groups.Find(importGroup);
                                    if (g == null) { Console.WriteLine("  组不存在: " + importGroup); }
                                    else
                                    {
                                        var imported = g.Blocks.Import(new FileInfo(importXml), ImportOptions.Override);
                                        foreach (var b in imported) Console.WriteLine("  已导入组[" + importGroup + "]: " + b.Name);
                                    }
                                }
                                catch (Exception ex) { Console.WriteLine("  导入到组失败: " + ex.Message); }
                                try
                                {
                                    var comp = plc.GetService<Siemens.Engineering.Compiler.ICompilable>();
                                    if (comp != null)
                                    {
                                        var cr = comp.Compile();
                                        Console.WriteLine("  PLC 编译: " + cr.State + "（错误 " + cr.ErrorCount + "，警告 " + cr.WarningCount + "）");
                                        foreach (var m in cr.Messages)
                                        {
                                            Console.WriteLine("    [" + m.State + "] " + m.Path + " | " + m.Description);
                                        }
                                    }
                                }
                                catch (Exception ex) { Console.WriteLine("  PLC 编译异常: " + ex.Message); }
                            }
                            if (doRestoreGroups)
                            {
                                // 把误放到顶层的块移回原组：date 组、Alarm 组
                                string dir = @"C:\Users\Administrator\Doubao\chats\2026-09-24\new-chat\Test01Assets\plc_blocks";
                                string[] dateNames = { "date", "ADC", "ADC_DB", "ADC_DB_1", "ADC_DB_2", "ADC_DB_3", "ADC_DB_4", "ADC_DB_5", "ADC_DB_6", "ADC_DB_7", "ADC_DB_8", "ADC_DB_9", "ADC_DB_10", "ADC_DT_DB_1", "ADC_DT_DB_2", "ADC_DT_DB_3", "ADC_DT_DB_4", "ADC_DT_DB_5", "ADC_DT_DB_6", "ADC_DT_DB_7", "ADC_DT_DB_8", "ADC_DT_DB_9", "ADC_DT_DB_10", "Read" };
                                string[] alarmNames = { "ALARM_PT_DB", "AL_DIS", "ALARM_DT_DB", "Alarm_PT" };
                                var dateGroup = plc.BlockGroup.Groups.Find("date");
                                var alarmGroup = plc.BlockGroup.Groups.Find("Alarm");
                                Console.WriteLine("  date组: " + (dateGroup != null ? dateGroup.Name : "未找到") + " | Alarm组: " + (alarmGroup != null ? alarmGroup.Name : "未找到"));
                                // 1. 删除顶层误放块
                                foreach (var n in dateNames.Concat(alarmNames))
                                {
                                    var b = plc.BlockGroup.Blocks.Find(n);
                                    if (b != null) { b.Delete(); Console.WriteLine("  已删除顶层块: " + n); }
                                }
                                // 2. 导入回 date 组
                                foreach (var n in dateNames)
                                {
                                    string f = Path.Combine(dir, n + ".xml");
                                    if (File.Exists(f) && dateGroup != null)
                                    {
                                        var imported = dateGroup.Blocks.Import(new FileInfo(f), ImportOptions.Override);
                                        foreach (var b in imported) Console.WriteLine("  已导入 date组: " + b.Name);
                                    }
                                    else Console.WriteLine("  跳过(文件缺失或组不存在): " + n);
                                }
                                // 3. 导入回 Alarm 组
                                foreach (var n in alarmNames)
                                {
                                    string f = Path.Combine(dir, n + ".xml");
                                    if (File.Exists(f) && alarmGroup != null)
                                    {
                                        var imported = alarmGroup.Blocks.Import(new FileInfo(f), ImportOptions.Override);
                                        foreach (var b in imported) Console.WriteLine("  已导入 Alarm组: " + b.Name);
                                    }
                                    else Console.WriteLine("  跳过(文件缺失或组不存在): " + n);
                                }
                                // 4. 编译
                                try
                                {
                                    var comp = plc.GetService<Siemens.Engineering.Compiler.ICompilable>();
                                    if (comp != null)
                                    {
                                        var cr = comp.Compile();
                                        Console.WriteLine("  PLC 编译: " + cr.State + "（错误 " + cr.ErrorCount + "，警告 " + cr.WarningCount + "）");
                                        foreach (var m in cr.Messages)
                                        {
                                            Console.WriteLine("    [" + m.State + "] " + m.Path + " | " + m.Description);
                                        }
                                    }
                                }
                                catch (Exception ex) { Console.WriteLine("  PLC 编译异常: " + ex.Message); }
                            }
                        }
                        if (container?.Software is HmiTarget hmi)
                        {
                        Console.WriteLine("  HMI: " + hmi.Name);
                        var tf = hmi.ScreenTemplateFolder;
                        Console.WriteLine("  现有模板: " + string.Join(",", tf.ScreenTemplates.Select(x => x.Name)));
                        if (doTemplate)
                        {
                            string xml = @"C:\Users\Administrator\Doubao\chats\2026-09-24\new-chat\Test01Assets\template_1.xml";
                            try
                            {
                                var imported = tf.ScreenTemplates.Import(new FileInfo(xml), ImportOptions.None);
                                foreach (var t in imported) Console.WriteLine("  导入模板成功: " + t.Name);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("  导入模板失败: " + ex.Message);
                            }
                            Console.WriteLine("  导入后模板: " + string.Join(",", tf.ScreenTemplates.Select(x => x.Name)));
                        }
                        if (doExport)
                        {
                            string outDir = @"C:\Users\Administrator\Doubao\chats\2026-09-24\new-chat\Test01Assets\screen_export";
                            Directory.CreateDirectory(outDir);
                            foreach (var s in hmi.ScreenFolder.Screens)
                            {
                                string f = Path.Combine(outDir, s.Name + ".xml");
                                try
                                {
                                    s.Export(new FileInfo(f), ExportOptions.WithDefaults);
                                    Console.WriteLine("  已导出画面: " + s.Name + " -> " + f);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("  导出失败 " + s.Name + ": " + ex.Message);
                                }
                            }
                        }
                        if (doApply)
                        {
                            string xml = @"C:\Users\Administrator\Doubao\chats\2026-09-24\new-chat\Test01Assets\screen_export\画面_2_with_template.xml";
                            var existing = hmi.ScreenFolder.Screens.Find("画面_2");
                            if (existing != null)
                            {
                                existing.Delete();
                                Console.WriteLine("  已删除旧画面_2（内容已备份于导出 XML）");
                            }
                            try
                            {
                                var imported = hmi.ScreenFolder.Screens.Import(new FileInfo(xml), ImportOptions.None);
                                foreach (var s in imported) Console.WriteLine("  导入画面成功: " + s.Name);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("  导入画面失败: " + ex.Message);
                            }
                            Console.WriteLine("  现有画面: " + string.Join(",", hmi.ScreenFolder.Screens.Select(x => x.Name)));
                        }
                        if (doSave)
                        {
                            project.Save();
                            Console.WriteLine("  已保存工程");
                        }
                        }
                    }
                }
                Console.WriteLine("== 探测完成 ==");
            }
        }
    }
}
