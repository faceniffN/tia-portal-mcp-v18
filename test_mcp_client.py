#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""TiaMcpServer 端到端测试客户端：走完整 MCP stdio 协议。"""
import json, subprocess, sys, time

EXE = r"C:\Users\Administrator\Doubao\chats\2026-09-24\new-chat\TiaMcpServer\bin\Release\net48\TiaMcpServer.exe"
PROJECT = r"C:\Users\Administrator\Doubao\chats\2026-09-24\new-chat\Test01\TEST01\TEST01.ap18"

p = subprocess.Popen([EXE], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                     stderr=subprocess.PIPE, text=True, encoding="utf-8", bufsize=1)

def call(msg, timeout=300):
    p.stdin.write(json.dumps(msg, ensure_ascii=False) + "\n")
    p.stdin.flush()
    t0 = time.time()
    while True:
        line = p.stdout.readline()
        if not line:
            raise RuntimeError("server closed stdout")
        obj = json.loads(line)
        if obj.get("id") == msg.get("id"):
            return obj
        if time.time() - t0 > timeout:
            raise RuntimeError("timeout waiting response")

def step(name, msg, show=True):
    resp = call(msg)
    ok = "result" in resp and ("isError" not in resp["result"] or not resp["result"]["isError"])
    text = ""
    if "result" in resp:
        content = resp["result"].get("content", [])
        if content:
            text = content[0].get("text", "")
    status = "OK " if ok else "ERR"
    print(f"[{status}] {name}")
    if show:
        try:
            parsed = json.loads(text)
            print(json.dumps(parsed, ensure_ascii=False, indent=1)[:2500])
        except Exception:
            print(text[:1500])
    return ok

try:
    step("initialize", {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}, show=False)
    p.stdin.write(json.dumps({"jsonrpc":"2.0","method":"notifications/initialized"}) + "\n")
    p.stdin.flush()

    tl = call({"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}})
    tools = tl["result"]["tools"]
    print(f"[OK ] tools/list — {len(tools)} 个工具: " + ", ".join(t["name"] for t in tools))

    step("tia_open_project", {"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"tia_open_project","arguments":{"projectPath":PROJECT}}})
    step("tia_project_status", {"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"tia_project_status","arguments":{}}})
    step("tia_compile", {"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"tia_compile","arguments":{"target":"all"}}})
    step("tia_save_project", {"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"tia_save_project","arguments":{}}})

    ASSETS = r"C:\Users\Administrator\Doubao\chats\2026-09-24\new-chat\Test01Assets"
    step("tia_import_tag_table", {"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"tia_import_tag_table","arguments":{"xmlPath": ASSETS + r"\hmi_tags.xml"}}})
    step("tia_import_screen", {"jsonrpc":"2.0","id":8,"method":"tools/call","params":{"name":"tia_import_screen","arguments":{"xmlPath": ASSETS + r"\hmi_screen.xml"}}})
    step("tia_compile", {"jsonrpc":"2.0","id":9,"method":"tools/call","params":{"name":"tia_compile","arguments":{"target":"all"}}})
    step("tia_save_project", {"jsonrpc":"2.0","id":10,"method":"tools/call","params":{"name":"tia_save_project","arguments":{}}})
finally:
    try:
        p.stdin.write(json.dumps({"jsonrpc":"2.0","method":"shutdown","id":99}) + "\n")
        p.stdin.flush()
        p.terminate()
    except Exception:
        pass
