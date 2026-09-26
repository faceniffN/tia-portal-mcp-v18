#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""模拟 opencode：一次性批量发送 initialize+initialized+tools/list+ping，逐行读取全部响应。"""
import json, subprocess, time

EXE = r"C:\Users\Administrator\Doubao\chats\2026-09-24\new-chat\TiaMcpServer\bin\Release\net48\TiaMcpServer.exe"

p = subprocess.Popen([EXE], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                     stderr=subprocess.PIPE, text=True, encoding="utf-8", bufsize=1)

msgs = [
    {"jsonrpc":"2.0","id":0,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"opencode","version":"1"}}},
    {"jsonrpc":"2.0","method":"notifications/initialized"},
    {"jsonrpc":"2.0","id":2,"method":"tools/list"},
    {"jsonrpc":"2.0","id":3,"method":"ping"},
]
payload = "".join(json.dumps(m, ensure_ascii=False) + "\n" for m in msgs)
p.stdin.write(payload); p.stdin.flush()
print(f"已一次性发送 {len(msgs)} 条消息")

p.stdout.flush()
deadline = time.time() + 30
lines = []
while time.time() < deadline:
    import select
    # 尝试读一行（非阻塞检查）
    # 简单方案：反复尝试 readline 但带整体超时
    try:
        line = p.stdout.readline()
        if not line:
            break
        lines.append(line.strip())
        # 读够 3 条响应（initialized 无响应）
        if len(lines) >= 3:
            # 再多等 1 秒确认没有额外响应
            time.sleep(1)
            import threading
            extra = []
            def r():
                try: extra.append(p.stdout.readline())
                except Exception: pass
            t = threading.Thread(target=r, daemon=True); t.start(); t.join(1)
            if extra and extra[0]:
                lines.append(extra[0].strip())
            break
    except Exception as e:
        print("read err:", e); break

print(f"收到 {len(lines)} 条响应:")
for i, l in enumerate(lines):
    print(f"[{i}] {l[:180]}")

try:
    p.kill()
except Exception:
    pass
