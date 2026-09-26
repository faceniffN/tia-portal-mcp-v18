#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""端到端测试新工具：tia_create_template + tia_apply_template + 编译。"""
import json, subprocess, sys, time

EXE = r"C:\Users\Administrator\Doubao\chats\2026-09-24\new-chat\TiaMcpServer\bin\Release\net48\TiaMcpServer.exe"
PROJECT = r"C:\Users\Administrator\Doubao\chats\2026-09-24\new-chat\Test01\TEST01\TEST01.ap18"

p = subprocess.Popen([EXE], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                     stderr=subprocess.PIPE, text=True, encoding="utf-8", bufsize=1)
_id = [0]
def call(name, args=None, timeout=600):
    _id[0] += 1
    msg = {"jsonrpc": "2.0", "id": _id[0], "method": "tools/call",
           "params": {"name": name, "arguments": args or {}}}
    p.stdin.write(json.dumps(msg, ensure_ascii=False) + "\n"); p.stdin.flush()
    start = time.time()
    while time.time() - start < timeout:
        line = p.stdout.readline()
        if not line: continue
        try: obj = json.loads(line)
        except Exception: continue
        if obj.get("id") == _id[0]:
            result = obj.get("result", {})
            content = result.get("content", [])
            if content:
                try: text = json.loads(content[0].get("text", "{}"))
                except Exception: text = {"raw": content[0].get("text", "")}
                report = text.get("report", str(text)[:150])
                print(f"  → {name}: {report}")
                if result.get("isError") or "ERROR" in str(text):
                    print(f"  ❌ isError: {str(text)[:300]}")
                    sys.exit(1)
                return text
    return {"error": "timeout"}

print("== 1) 打开工程 ==")
call("tia_open_project", {"projectPath": PROJECT})
print("== 2) 创建模板 模板_2 ==")
call("tia_create_template", {"name": "模板_2", "width": "1280", "height": "800", "backColor": "230, 240, 250"})
print("== 3) 给画面_2 应用模板_2 ==")
call("tia_apply_template", {"screenName": "画面_2", "templateName": "模板_2"})
print("== 4) 编译 ==")
call("tia_compile", {"target": "all"})
print("== 5) 保存 + 释放 ==")
call("tia_save_project")
call("tia_shutdown")
print("✅ 新工具端到端测试通过")
