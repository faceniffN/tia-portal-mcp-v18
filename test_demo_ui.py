#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""豆包客户端直接驱动 TIA：调 TiaMcpServer.exe --with-ui（GUI 可见）完成 open→status→compile→save→shutdown。"""
import json, subprocess, sys, time

EXE = r"C:\Users\Administrator\Doubao\chats\2026-09-24\new-chat\TiaMcpServer\bin\Release\net48\TiaMcpServer.exe"
PROJECT = r"C:\Users\Administrator\Doubao\chats\2026-09-24\new-chat\Test01\TEST01\TEST01.ap18"

p = subprocess.Popen([EXE, "--with-ui"], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
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
        try:
            obj = json.loads(line)
        except Exception:
            continue
        if obj.get("id") == _id[0]:
            return obj
    return {"error": {"message": "timeout"}}

steps = [
    ("tia_open_project", {"projectPath": PROJECT}),
    ("tia_project_status", None),
    ("tia_compile", {"target": "all"}),
    ("tia_save_project", None),
    ("tia_shutdown", None),
]
for name, args in steps:
    r = call(name, args)
    if "error" in r:
        print(f"❌ {name}: {r['error'].get('message', r['error'])}")
        sys.exit(1)
    result = r.get("result", {})
    content = result.get("content", [])
    if not content:
        print(f"❌ {name}: 无 content；完整响应={str(r)[:300]}")
        sys.exit(1)
    raw = content[0].get("text", "")
    try:
        text = json.loads(raw)
    except Exception:
        print(f"❌ {name}: 服务器返回非 JSON：{raw[:300]}")
        sys.exit(1)
    if result.get("isError"):
        print(f"❌ {name}: isError=true，{raw[:300]}")
        sys.exit(1)
    report = text.get("report", str(text)[:120]) if isinstance(text, dict) else str(text)[:120]
    print(f"[豆包驱动] {name} → {report}")

p.kill()
print("✅ 演示完成：豆包客户端直接驱动 TIA V18 全程 OK")
