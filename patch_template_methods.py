# -*- coding: utf-8 -*-
"""在 TiaService.cs 中 EnsureProject 后插入模板方法（create/apply）。"""
path = r"C:\Users\Administrator\Doubao\chats\2026-09-24\new-chat\TiaMcpServer\TiaService.cs"
s = open(path, encoding="utf-8").read()

methods = '''        /// <summary>新建 HMI 模板画面（背景模板）。模板为纯色背景 + 空图层，元素可在 GUI 中添加。
        /// V18 实测：模板 XML 导入 TextField 的 Text/ColorSet 属性不支持，故模板内容保持简单。</summary>
        public JsonObject CreateTemplate(string name, int width = 1280, int height = 800, string backColor = "153, 204, 255")
        {
            EnsureProject();
            var hmi = FindHmiTarget();
            if (hmi == null) throw new InvalidOperationException("No HmiTarget found in project.");
            var tf = hmi.ScreenTemplateFolder;
            if (tf.ScreenTemplates.Find(name) != null)
                return new JsonObject { ["created"] = false, ["reason"] = "template exists", ["report"] = $"模板 {name} 已存在，未重复创建" };

            string xml = BuildTemplateXml(name, width, height, backColor);
            string tmp = Path.Combine(Path.GetTempPath(), "tia_template_" + Guid.NewGuid().ToString("N") + ".xml");
            File.WriteAllText(tmp, xml, new UTF8Encoding(false));
            try
            {
                var imported = tf.ScreenTemplates.Import(new FileInfo(tmp), ImportOptions.None);
                string createdName = imported.Count > 0 ? imported[0].Name : name;
                return new JsonObject
                {
                    ["created"] = true,
                    ["name"] = createdName,
                    ["report"] = $"已创建模板 {createdName}（{width}×{height}，背景色 {backColor}）；画面可在 GUI 中引用为背景模板，也可用 tia_apply_template 应用"
                };
            }
            catch (Exception ex)
            {
                return new JsonObject { ["created"] = false, ["reason"] = ex.Message, ["report"] = $"创建模板失败：{ex.Message}" };
            }
        }

        /// <summary>给指定画面应用模板作为背景。流程：导出画面 XML → 注入 LinkList 模板引用 → 重建画面（内容保留）。V18 实测。</summary>
        public JsonObject ApplyTemplate(string screenName, string templateName)
        {
            EnsureProject();
            var hmi = FindHmiTarget();
            if (hmi == null) throw new InvalidOperationException("No HmiTarget found in project.");

            var tpl = hmi.ScreenTemplateFolder.ScreenTemplates.Find(templateName);
            if (tpl == null)
                return new JsonObject { ["applied"] = false, ["reason"] = "template not found", ["report"] = $"模板 {templateName} 不存在，请先用 tia_create_template 创建" };
            var screen = hmi.ScreenFolder.Screens.Find(screenName);
            if (screen == null)
                return new JsonObject { ["applied"] = false, ["reason"] = "screen not found", ["report"] = $"画面 {screenName} 不存在" };

            string tmpExport = Path.Combine(Path.GetTempPath(), "tia_screen_" + Guid.NewGuid().ToString("N") + ".xml");
            string tmpImport = Path.Combine(Path.GetTempPath(), "tia_screen_" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                screen.Export(new FileInfo(tmpExport), ExportOptions.WithDefaults);
                string xml = File.ReadAllText(tmpExport, Encoding.UTF8);
                string modified = InjectTemplateLink(xml, templateName);
                File.WriteAllText(tmpImport, modified, new UTF8Encoding(false));
                screen.Delete();
                var imported = hmi.ScreenFolder.Screens.Import(new FileInfo(tmpImport), ImportOptions.None);
                string importedName = imported.Count > 0 ? imported[0].Name : screenName;
                return new JsonObject
                {
                    ["applied"] = true,
                    ["screen"] = importedName,
                    ["template"] = templateName,
                    ["report"] = $"画面 {importedName} 已应用背景模板 {templateName}（画面内容保留）"
                };
            }
            catch (Exception ex)
            {
                return new JsonObject { ["applied"] = false, ["reason"] = ex.Message, ["report"] = $"应用模板失败：{ex.Message}" };
            }
        }

        private static string BuildTemplateXml(string name, int width, int height, string backColor)
        {
            return "<?xml version=\\"1.0\\" encoding=\\"utf-8\\"?>\\r\\n" +
                   "<Document>\\r\\n" +
                   "  <Engineering version=\\"V18\\" />\\r\\n" +
                   "  <Hmi.Screen.ScreenTemplate ID=\\"0\\">\\r\\n" +
                   "    <AttributeList>\\r\\n" +
                   "      <BackColor>" + backColor + "</BackColor>\\r\\n" +
                   "      <Height>" + height + "</Height>\\r\\n" +
                   "      <Name>" + name + "</Name>\\r\\n" +
                   "      <Width>" + width + "</Width>\\r\\n" +
                   "    </AttributeList>\\r\\n" +
                   "    <ObjectList>\\r\\n" +
                   "      <Hmi.Screen.ScreenLayer ID=\\"900001\\" CompositionName=\\"Layers\\">\\r\\n" +
                   "        <AttributeList>\\r\\n" +
                   "          <Index>0</Index>\\r\\n" +
                   "          <Name />\\r\\n" +
                   "          <VisibleES>true</VisibleES>\\r\\n" +
                   "        </AttributeList>\\r\\n" +
                   "        <ObjectList />\\r\\n" +
                   "      </Hmi.Screen.ScreenLayer>\\r\\n" +
                   "    </ObjectList>\\r\\n" +
                   "  </Hmi.Screen.ScreenTemplate>\\r\\n" +
                   "</Document>";
        }

        /// <summary>在画面 XML 中注入/替换背景模板引用（LinkList/Template）。V18 实测结构。</summary>
        private static string InjectTemplateLink(string xml, string templateName)
        {
            string block = "    <LinkList>\\r\\n" +
                           "      <Template TargetID=\\"@OpenLink\\">\\r\\n" +
                           "        <Name>" + templateName + "</Name>\\r\\n" +
                           "      </Template>\\r\\n" +
                           "    </LinkList>";

            if (xml.Contains("<LinkList>"))
            {
                int idx = xml.IndexOf("<LinkList>");
                int end = xml.IndexOf("</LinkList>", idx) + "</LinkList>".Length;
                return xml.Substring(0, idx) + block + xml.Substring(end);
            }

            string needleCrlf = "    </AttributeList>\\r\\n    <ObjectList>";
            string needleLf = "    </AttributeList>\\n    <ObjectList>";
            if (xml.Contains(needleCrlf))
                return xml.Replace(needleCrlf, "    </AttributeList>\\r\\n" + block + "\\r\\n    <ObjectList>");
            if (xml.Contains(needleLf))
                return xml.Replace(needleLf, "    </AttributeList>\\n" + block.Replace("\\r\\n", "\\n") + "\\n    <ObjectList>");

            int ai = xml.IndexOf("</AttributeList>");
            if (ai >= 0)
                return xml.Insert(ai + "</AttributeList>".Length, "\\r\\n" + block);
            throw new InvalidOperationException("无法定位画面 XML 结构（无 AttributeList）");
        }

'''

anchor = """        private void EnsureProject()
        {
            if (_project == null) throw new InvalidOperationException("No project open. Call tia_open_project first.");
        }

        public void Dispose()"""

if anchor not in s:
    print("!! 锚点未找到")
    raise SystemExit(1)

new_block = anchor.replace("        public void Dispose()", methods + "        public void Dispose()")
s = s.replace(anchor, new_block)
with open(path, "w", encoding="utf-8", newline="") as f:
    f.write(s)
print("OK 已插入模板方法")
