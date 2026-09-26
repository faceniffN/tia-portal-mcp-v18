#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""聚焦测试：import_tag_table + import_screen + compile + save（全新进程 = 全新 TIA 会话）。"""
import json, subprocess, time

EXE = r"C:\Users\Administrator\Doubao\chats\2026-09-24\new-chat\TiaMcpServer\bin\Release\net48\TiaMcpServer.exe"
PROJECT = r"C:\Users\Administrator\Doubao\chats\2026-09-24\new-chat\Test01\TEST01\TEST01.ap18"
ASSETS = r"C:\Users\Administrator\Doubao\chats\2026-09-24\new-chat\Test01Assets"

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
        print(json.dumps(json.loads(text), ensure_ascii=False, indent=1)[:1800])
    except Exception:
        print(text[:1200])

try:
    call({"jsonrpc":"2.0","id":0,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"t","version":"1"}}})
    p.stdin.write(json.dumps({"jsonrpc":"2.0","method":"notifications/initialized"}) + "\n"); p.stdin.flush()
    step("open_project", {"jsonrpc":"2.0","method":"tools/call","params":{"name":"tia_open_project","arguments":{"projectPath":PROJECT}}})
    step("import_tag_table", {"jsonrpc":"2.0","method":"tools/call","params":{"name":"tia_import_tag_table","arguments":{"xmlPath": ASSETS + r"\hmi_tags.xml"}}})
    step("import_screen", {"jsonrpc":"2.0","method":"tools/call","params":{"name":"tia_import_screen","arguments":{"xmlPath": ASSETS + r"\hmi_screen.xml"}}})
    step("compile_all", {"jsonrpc":"2.0","method":"tools/call","params":{"name":"tia_compile","arguments":{"target":"all"}}})
    step("save_project", {"jsonrpc":"2.0","method":"tools/call","params":{"name":"tia_save_project","arguments":{}}})
finally:
    try:
        p.stdin.write(json.dumps({"jsonrpc":"2.0","method":"shutdown","id":999}) + "\n"); p.stdin.flush()
        p.terminate()
    except Exception:
        pass
