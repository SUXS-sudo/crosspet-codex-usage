#!/usr/bin/env python3
"""假的 codex 命令：只模拟 `--version` 和 `app-server` 的 account/rateLimits/read，给 CrossPet 的测试用。
FAKE_CODEX_MODE：exit（一启动就退出，测写 stdin 时对面已经没了）/ ok（两个窗口）/ single（只有一个窗口）/ auth（没登录）/ nomethod（旧版没有这个接口）/ hang（一直不回）/ garbage（乱回）
"""
import json, os, sys, time

mode = os.environ.get("FAKE_CODEX_MODE", "ok")
if sys.argv[1:] == ["--version"]:
    print("codex-cli 0.0.0-fake"); sys.exit(0)
if sys.argv[1:2] != ["app-server"]:
    sys.exit(2)
if mode == "exit":
    sys.exit(0)
now = int(time.time())
LIMITS = {
    "ok": {"limitId": "codex", "primary": {"usedPercent": 3, "windowDurationMins": 300, "resetsAt": now + 3600},
           "secondary": {"usedPercent": 40, "windowDurationMins": 10080, "resetsAt": now + 3 * 86400}, "planType": "plus"},
    "single": {"limitId": "codex", "primary": {"usedPercent": 12, "windowDurationMins": 10080, "resetsAt": now + 5 * 86400}, "secondary": None},
}
for line in sys.stdin:
    msg = json.loads(line)
    if msg.get("method") == "initialize":
        print(json.dumps({"id": msg["id"], "result": {"userAgent": "fake"}}), flush=True)
    elif msg.get("method") == "account/rateLimits/read":
        if mode == "hang":
            time.sleep(3600)
        elif mode == "garbage":
            print("not json", flush=True)
        elif mode == "auth":
            print(json.dumps({"id": msg["id"], "error": {"code": -32600, "message": "codex account authentication required to read rate limits"}}), flush=True)
        elif mode == "nomethod":
            print(json.dumps({"id": msg["id"], "error": {"code": -32601, "message": "method not found"}}), flush=True)
        else:
            rl = LIMITS[mode]
            print(json.dumps({"id": msg["id"], "result": {"rateLimits": rl, "rateLimitsByLimitId": {"codex": rl},
                              "rateLimitResetCredits": {"availableCount": 1}, "accountId": "acct-secret"}}), flush=True)
