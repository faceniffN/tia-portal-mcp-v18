#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""with-ui 模式完整闭环：打开 → 状态 → 编译 → 保存 → 彻底释放（TIA GUI 可见）。"""
import json, subprocess, time

EXE = r"C:\Users\Administrator\Doubao\chats\2026-09-24\new-chat\TiaMcpServer\bin\Release\net48\TiaMcpServer.exe"
ARGS = ["--with-ui"]
PROJECT = r"C:\Users\Administrator\Doubao\chats\2026-09-24\new-chat\Test01\TEST01\TEST01.ap18"

p = subprocess.Popen([EXE] + ARGS, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                     stderr=subprocess.PIPE, text=True, encoding="utf-8", bufsize=1)
_next = [0]
def call(msg, timeout=420):
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
        print(json.dumps(json.loads(text), ensure_ascii=False, indent=1)[:900])
    except Exception:
        print(text[:800])

try:
    call({"jsonrpc":"2.0","id":0,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"t","version":"1"}}})
    p.stdin.write(json.dumps({"jsonrpc":"2.0","method":"notifications/initialized"}) + "\n"); p.stdin.flush()
    step("1 open", {"jsonrpc":"2.0","method":"tools/call","params":{"name":"tia_open_project","arguments":{"projectPath":PROJECT}}})
    step("2 status", {"jsonrpc":"2.0","method":"tools/call","params":{"name":"tia_project_status","arguments":{}}})
    step("3 compile", {"jsonrpc":"2.0","method":"tools/call","params":{"name":"tia_compile","arguments":{"target":"all"}}})
    step("4 save", {"jsonrpc":"2.0","method":"tools/call","params":{"name":"tia_save_project","arguments":{}}})
    step("5 shutdown (释放)", {"jsonrpc":"2.0","method":"tools/call","params":{"name":"tia_shutdown","arguments":{}}})
finally:
    try:
        p.stdin.write(json.dumps({"jsonrpc":"2.0","method":"shutdown","id":999}) + "\n"); p.stdin.flush()
        p.terminate()
    except Exception:
        pass
