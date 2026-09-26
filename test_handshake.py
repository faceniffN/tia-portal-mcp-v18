#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""模拟 opencode 完整 MCP 握手：initialize → initialized → tools/list → ping → 未知方法 → tools/list。"""
import json, subprocess, time

EXE = r"C:\Users\Administrator\Doubao\chats\2026-09-24\new-chat\TiaMcpServer\bin\Release\net48\TiaMcpServer.exe"
ARGS = ["--with-ui"]

p = subprocess.Popen([EXE] + ARGS, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                     stderr=subprocess.PIPE, text=True, encoding="utf-8", bufsize=1)
_next = [0]

def send(obj):
    obj.setdefault("id", _next[0]); _next[0] += 1
    p.stdin.write(json.dumps(obj, ensure_ascii=False) + "\n"); p.stdin.flush()

def recv(timeout=20):
    p.stdout.flush()
    import select
    # 非阻塞读一行
    import threading
    result = {}
    def reader():
        try: result["line"] = p.stdout.readline()
        except Exception as e: result["err"] = str(e)
    t = threading.Thread(target=reader, daemon=True)
    t.start(); t.join(timeout)
    if t.is_alive():
        return ("TIMEOUT(>%ds)" % timeout, None)
    return (result.get("line"), result.get("err"))

print("== 1) initialize ==")
send({"jsonrpc":"2.0","method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"opencode","version":"1"}}})
line, err = recv(); print("resp:", (line or "").strip()[:200] or err)

print("== 2) notifications/initialized (无响应预期) ==")
send({"jsonrpc":"2.0","method":"notifications/initialized"})
line, err = recv(3); print("resp:", (line or "无(正确,notification不应回复)").strip()[:100])

print("== 3) tools/list ==")
send({"jsonrpc":"2.0","method":"tools/list"})
line, err = recv(); print("resp:", (line or "").strip()[:300] or err)

print("== 4) ping ==")
send({"jsonrpc":"2.0","method":"ping"})
line, err = recv(); print("resp:", (line or "").strip()[:200] or err)

print("== 5) 未知方法 logging/setLevel (应回 error) ==")
send({"jsonrpc":"2.0","method":"logging/setLevel","params":{"level":"debug"}})
line, err = recv(); print("resp:", (line or "").strip()[:200] or err)

print("== 6) 未知方法 resources/list (应回 error) ==")
send({"jsonrpc":"2.0","method":"resources/list"})
line, err = recv(); print("resp:", (line or "").strip()[:200] or err)

print("== 7) tools/list 再次 ==")
send({"jsonrpc":"2.0","method":"tools/list"})
line, err = recv(); print("resp:", (line or "").strip()[:300] or err)

print("== 8) shutdown ==")
send({"jsonrpc":"2.0","method":"shutdown"})
line, err = recv(); print("resp:", (line or "").strip()[:200] or err)

try:
    p.stdin.write(json.dumps({"jsonrpc":"2.0","method":"exit","id":99}) + "\n"); p.stdin.flush()
    p.wait(timeout=5)
except Exception:
    p.kill()
print("== 服务器已退出 ==")
