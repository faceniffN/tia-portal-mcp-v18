using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TiaMcpServer
{
    /// <summary>MCP 工具定义</summary>
    public sealed class ToolDef
    {
        public string Name;
        public string Description;
        public JsonObject InputSchema;
        public Func<JsonObject, JsonObject> Handler;
    }

    /// <summary>
    /// 极简 MCP 服务器：JSON-RPC 2.0 over stdio（newline-delimited）。
    /// 实现 initialize / notifications/initialized / tools/list / tools/call / ping / shutdown。
    /// </summary>
    public sealed class McpServer
    {
        private readonly List<ToolDef> _tools = new List<ToolDef>();
        private readonly TextReader _in;
        private readonly TextWriter _out;

        public McpServer(TextReader input, TextWriter output)
        {
            _in = input;
            _out = output;
        }

        public void Register(ToolDef tool)
        {
            _tools.Add(tool);
        }

        private const string NoReply = "__NO_REPLY__";

        public void Run()
        {
            string line;
            while ((line = _in.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                string response = Handle(line);
                if (response == null) break; // exit
                if (response == NoReply) continue; // notification, no reply
                _out.WriteLine(response);
                _out.Flush();
            }
        }

        private string Handle(string line)
        {
            try
            {
                JsonNode node = JsonNode.Parse(line);
                if (node == null) return Error(-32700, "Parse error", null);
                JsonObject msg = node.AsObject();
                string method = (string)msg["method"];
                JsonNode id = msg["id"];

                switch (method)
                {
                    case "initialize":
                        return Result(id, new JsonObject
                        {
                            ["protocolVersion"] = "2024-11-05",
                            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                            ["serverInfo"] = new JsonObject { ["name"] = "tia-mcp-v18", ["version"] = "1.0.0" }
                        });
                    case "notifications/initialized":
                        return NoReply; // no reply
                    case "notifications/cancelled":
                        return NoReply;
                    case "ping":
                        return Result(id, new JsonObject());
                    case "tools/list":
                        return Result(id, ListTools());
                    case "tools/call":
                        return CallTool(id, msg["params"] as JsonObject);
                    case "shutdown":
                        return Result(id, new JsonObject());
                    case "exit":
                        return null;
                    default:
                        return Error(-32601, "Method not found: " + method, id);
                }
            }
            catch (Exception ex)
            {
                return Error(-32603, "Internal error: " + ex.Message, null);
            }
        }

        private JsonObject ListTools()
        {
            var arr = new JsonArray();
            foreach (var t in _tools)
            {
                arr.Add(new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["inputSchema"] = t.InputSchema ?? new JsonObject { ["type"] = "object" }
                });
            }
            return new JsonObject { ["tools"] = arr };
        }

        private string CallTool(JsonNode id, JsonObject parameters)
        {
            string name = parameters?["name"]?.GetValue<string>() ?? "";
            JsonObject args = parameters?["arguments"] as JsonObject ?? new JsonObject();

            foreach (var t in _tools)
            {
                if (t.Name != name) continue;
                try
                {
                    JsonObject content = t.Handler(args);
                    return Result(id, new JsonObject
                    {
                        ["content"] = new JsonArray
                        {
                            new JsonObject { ["type"] = "text", ["text"] = content?.ToJsonString() ?? "{}" }
                        },
                        ["isError"] = false
                    });
                }
                catch (Exception ex)
                {
                    return Result(id, new JsonObject
                    {
                        ["content"] = new JsonArray
                        {
                            new JsonObject { ["type"] = "text", ["text"] = "ERROR: " + ex.GetType().Name + ": " + ex.Message }
                        },
                        ["isError"] = true
                    });
                }
            }
            return Error(-32602, "Unknown tool: " + name, id);
        }

        private string Result(JsonNode id, JsonObject result)
        {
            var msg = new JsonObject { ["jsonrpc"] = "2.0", ["result"] = result };
            if (id != null) msg["id"] = id.DeepClone();
            return msg.ToJsonString();
        }

        private string Error(int code, string message, JsonNode id)
        {
            var msg = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
            };
            if (id != null) msg["id"] = id.DeepClone();
            return msg.ToJsonString();
        }
    }
}
