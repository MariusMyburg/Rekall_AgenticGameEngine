"""Minimal MCP stdio client for the Rekall AGE server.

The CLI's `command execute <name> <json>` form cannot carry a large blueprint:
a scene with procedural ship hulls is ~93 KB of JSON and blows past the OS
argument limit. The MCP server takes the same commands as JSON-RPC over stdio,
which has no such ceiling.

usage: python mcp.py <tool-name> <payload.json> [<tool-name> <payload.json> ...]
"""
import json
import subprocess
import sys
import os

REPO = "F:/Dev/Rekall_AGE"
CLI = ["dotnet", "run", "--project", "src/Rekall.Age.Cli", "-c", "Release", "--", "mcp", "stdio"]


class Mcp:
    def __init__(self):
        self.p = subprocess.Popen(
            CLI, cwd=REPO, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            stderr=subprocess.PIPE, text=True, encoding="utf-8", bufsize=1)
        self.n = 0

    def call(self, method, params=None):
        self.n += 1
        msg = {"jsonrpc": "2.0", "id": self.n, "method": method}
        if params is not None:
            msg["params"] = params
        self.p.stdin.write(json.dumps(msg) + "\n")
        self.p.stdin.flush()
        while True:
            line = self.p.stdout.readline()
            if not line:
                err = self.p.stderr.read()
                raise RuntimeError(f"server closed. stderr:\n{err[:2000]}")
            line = line.strip()
            if not line:
                continue
            try:
                d = json.loads(line)
            except json.JSONDecodeError:
                continue                      # server chatter, not a response
            if d.get("id") == self.n:
                return d

    def close(self):
        try:
            self.p.stdin.close()
            self.p.wait(timeout=10)
        except Exception:
            self.p.kill()


def summarize(resp):
    if "error" in resp:
        return f"ERROR {resp['error'].get('code')}: {resp['error'].get('message')}"
    result = resp.get("result", {})
    parts = []
    for c in result.get("content", []):
        if c.get("type") == "text":
            t = c["text"]
            try:
                d = json.loads(t)
                parts.append(f"ok={d.get('ok')} {d.get('summary')}")
                for e in (d.get("errors") or [])[:12]:
                    parts.append("   - " + e.get("message", ""))
            except json.JSONDecodeError:
                parts.append(t[:400])
    if result.get("isError"):
        parts.append("(isError)")
    return "\n".join(parts) or json.dumps(result)[:400]


if __name__ == "__main__":
    m = Mcp()
    try:
        init = m.call("initialize", {
            "protocolVersion": "2024-11-05",
            "capabilities": {},
            "clientInfo": {"name": "stellar-dominion-authoring", "version": "1"},
        })
        if "error" in init:
            print("initialize:", summarize(init)); sys.exit(1)
        print("initialize: ok")

        args = sys.argv[1:]
        for i in range(0, len(args), 2):
            tool, path = args[i], args[i + 1]
            payload = json.load(open(path, encoding="utf-8"))
            resp = m.call("tools/call", {"name": tool, "arguments": payload})
            print(f"{tool}: {summarize(resp)}")
    finally:
        m.close()
