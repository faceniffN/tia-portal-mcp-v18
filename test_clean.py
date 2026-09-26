#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""headless + 删除工具验证：打开 → 清理测试残留 → 编译干净工程 → 保存。"""
import json, subprocess, time

EXE = r"C:\Users\Administrator\Doubao\chats\2026-09-24\new-chat\TiaMcpServer\bin\Release\net48\TiaMcpServer.exe"
PROJECT = r"C:\Users\Administrator\Doubao\chats\2026-09-24\new-chat\Test01\TEST01\TEST01.ap18"

p = subprocess.Popen([EXE], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                     stderr=subprocess.PIPE, text=True, encoding="utf-8", bufsize=1)
_next = [0]
def call(msg, timeout=300):
    msg.setdefault("id", _next[0]); _next[0] += 1
    p.stdin.write(json.dumps(msg, ensure_ascii=False) + "\n"); p.stdin.flush()
    t0 = time.time()
    while True:
        line = p.stdout.readline()
        if not line: raise RuntimeError("server closed stdout")
        obj = json.loads(line)
        if obj.get("id") == msg["id"]: return obj
        if time.time() - t0 > timeout: raise RuntimeError("timeout")

def step(name, msg):
    r = call(msg)
    text = ""
    if "result" in r:
        c = r["result"].get("content", [])
        if c: text = c[0].get("text", "")
    err = "result" not in r or r["result"].get("isError", False)
    print(f"[{'ERR' if err else 'OK '}] {name}")
    try:
        print(json.dumps(json.loads(text), ensure_ascii=False, indent=1)[:1500])
    except Exception:
        print(text[:1200])

try:
    call({"jsonrpc":"2.0","id":0,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"t","version":"1"}}})
    p.stdin.write(json.dumps({"jsonrpc":"2.0","method":"notifications/initialized"}) + "\n"); p.stdin.flush()
    step("open_project", {"jsonrpc":"2.0","method":"tools/call","params":{"name":"tia_open_project","arguments":{"projectPath":PROJECT}}})
    for typ, name in [("tagTable","测试_MCP表"), ("tagTable","测试_变量表2"),
                      ("screen","画面_测试MCP"), ("screen","画面_测试构建器")]:
        step(f"delete {typ} {name}", {"jsonrpc":"2.0","method":"tools/call","params":{"name":"tia_delete_object","arguments":{"type":typ,"name":name}}})
    step("compile_all (干净工程)", {"jsonrpc":"2.0","method":"tools/call","params":{"name":"tia_compile","arguments":{"target":"all"}}})
    step("save_project", {"jsonrpc":"2.0","method":"tools/call","params":{"name":"tia_save_project","arguments":{}}})
    step("project_status (最终)", {"jsonrpc":"2.0","method":"tools/call","params":{"name":"tia_project_status","arguments":{}}})
finally:
    try:
        p.stdin.write(json.dumps({"jsonrpc":"2.0","method":"shutdown","id":999}) + "\n"); p.stdin.flush()
        p.terminate()
    except Exception:
        pass
