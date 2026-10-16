#!/bin/sh
# 用假 codex 测 Mac 版问服务的各条路径：先 app/build.sh 编译，再 CROSSPET_CODEX_TIMEOUT=3 tests/codex_quota_probe.sh
set -eu
cd "$(dirname "$0")/.."
BIN=build/CrossPet.app/Contents/MacOS/CrossPet
FAKE="$(pwd)/tests/fake_codex.py"
EMPTY=$(mktemp -d)   # 空的 CODEX_HOME：没有会话记录，失败时应该是 none
fail=0
check() {   # 模式 期望的 source 期望 error 里包含的字（可空）
  out=$(CODEX_HOME="$EMPTY" CROSSPET_CODEX_BIN="$FAKE" FAKE_CODEX_MODE="$1" "$BIN" --codex-quota-probe)
  python3 - "$1" "$2" "$3" "$out" <<'PY' || fail=1
import json, sys
mode, src, err, out = sys.argv[1:5]
p = json.loads(out)
ok = p["source"] == src and (err in (p.get("error") or "")) and "acct-secret" not in out
print(("PASS" if ok else "FAIL"), mode, out[:160])
sys.exit(0 if ok else 1)
PY
}
check ok live ""
check single live ""
check auth none "authentication"
check nomethod none "method not found"
check garbage none "超时"
start=$(date +%s); check hang none "超时"; [ $(( $(date +%s) - start )) -le 8 ] || { echo "FAIL hang 超过 8 秒（超时没生效）"; fail=1; }
# 对面一启动就退出：往它的 stdin 写东西不能把整个程序带崩（SIGPIPE），连跑 20 次
crash=0; for i in $(seq 20); do CODEX_HOME="$EMPTY" CROSSPET_CODEX_BIN="$FAKE" FAKE_CODEX_MODE=exit "$BIN" --codex-quota-probe >/dev/null 2>&1 || crash=$((crash+1)); done
[ $crash -eq 0 ] && echo "PASS exit-no-crash" || { echo "FAIL exit 崩了 $crash 次"; fail=1; }
out=$(CODEX_HOME="$EMPTY" CROSSPET_CODEX_BIN=/nonexistent/codex "$BIN" --codex-quota-probe)
echo "$out" | grep -q '"source":"none"' && echo "PASS missing-binary" || { echo "FAIL missing-binary $out"; fail=1; }
# 会话记录不可靠（Codex 回复了也不一定写额度），1.3.3 起不再读：有记录也应该是 none
REC=$(mktemp -d); D="$REC/sessions/$(date +%Y/%m/%d)"; mkdir -p "$D"
now=$(date +%s)
printf '{"timestamp":"2026-01-01T00:00:09Z","payload":{"rate_limits":{"primary":{"used_percent":70,"window_minutes":300,"resets_at":%s}}}}\n' $((now+600)) > "$D/rollout-a.jsonl"
printf '{"timestamp":"2026-01-01T00:00:01Z","payload":{"rate_limits":{"primary":{"used_percent":10,"window_minutes":300,"resets_at":%s}}}}\n' $((now+600)) > "$D/rollout-b.jsonl"
touch "$D/rollout-a.jsonl"; sleep 1; touch "$D/rollout-b.jsonl"   # b 文件更新，但记录更旧
out=$(CODEX_HOME="$REC" CROSSPET_CODEX_BIN="$FAKE" FAKE_CODEX_MODE=auth "$BIN" --codex-quota-probe)
echo "$out" | grep -q '"source":"none"' && ! echo "$out" | grep -q 'used_percent' && echo "PASS records-ignored" || { echo "FAIL 不该再读会话记录 $out"; fail=1; }
rm -rf "$REC"
# 定时（每分钟一次）到点时要不要问：Codex 在干活（10 分钟内有不是 Stop 的事件）1 分钟问一次，闲着 2 分钟一次
# 参数：离上次问过了几秒 离 Codex 最近一次干活事件几秒（-1 = 没有 / 已经 Stop）
# 程序不认识这个参数时会当成正常启动、一直开着：最多等 5 秒
tick() { got=$(perl -e 'alarm 5; exec @ARGV' "$BIN" --codex-tick-probe "$1" "$2" 2>/dev/null || echo "（没有输出）"); [ "$got" = "$3" ] && echo "PASS tick $4" || { echo "FAIL tick $4：期望 $3，得到 $got"; fail=1; }; }
tick 58 30 yes "干活中、定时器早到 2 秒也算到点"
tick 40 30 no "干活中、刚问过"
tick 58 -1 no "闲着、才 1 分钟"
tick 118 -1 yes "闲着、2 分钟（定时器早到 2 秒）"
tick 70 700 no "上次干活是 10 分钟前：算闲着"
rm -rf "$EMPTY"
exit $fail
