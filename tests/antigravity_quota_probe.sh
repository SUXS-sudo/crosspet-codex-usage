#!/bin/sh
# Gemini（Antigravity）额度排版：先 app/build.sh 编译，再 tests/antigravity_quota_probe.sh
# 样本在 tests/fixtures/antigravity/（只留模型名和额度字段）
set -eu
cd "$(dirname "$0")/.."
BIN=build/CrossPet.app/Contents/MacOS/CrossPet
F=tests/fixtures/antigravity
fail=0
check() {   # 名字 样本 期望（Python 表达式，q 是结果）
  out=$(perl -e 'alarm 5; exec @ARGV' "$BIN" --agy-quota-probe "$F/$2.json" 2>/dev/null || echo '"（没有输出）"')
  python3 - "$1" "$3" "$out" <<'PY' || fail=1
import json, sys
name, expect, out = sys.argv[1:4]
try: q = json.loads(out)
except Exception: q = None
ok = False
try: ok = bool(eval(expect, {"q": q}))
except Exception: pass
print(("PASS" if ok else "FAIL"), name, "" if ok else out)
sys.exit(0 if ok else 1)
PY
}
# 2026-10-14 实测：非 Ultra 账号里「Gemini 4 Argon」标着不可用、额度信息是空的，以前被当成「剩 0%」排在第一行
# 固定两行，和 Antigravity 自己「View Usage」的分组一样：Gemini 一行、Claude/GPT 一行，数字相同也不合并
check "不可用的模型不算进额度" ultra-only-model 'q["text"].split(" ｜ ")[0].startswith("Gemini 剩余 100%") and q["text"].split(" ｜ ")[1] == "Claude/GPT 剩余 100%" and q["low"] is False'
check "真用完了还是 0%" gemini-used-up 'q["text"].startswith("Gemini 剩余 0%") and "重置" in q["text"].split(" ｜ ")[0] and q["text"].split(" ｜ ")[1] == "Claude/GPT 剩余 100%" and q["low"] is True'
check "两个池 + 不可用模型" two-pools-with-unavailable 'q["text"].startswith("Gemini 剩余 52%") and q["text"].split(" ｜ ")[1] == "Claude/GPT 剩余 99%" and " 0%" not in q["text"] and q["low"] is False'
# 主要来源：额度汇总（RetrieveUserQuotaSummary，和 Antigravity 的「View Usage」同一份数据，刚对话完就更新）
check "汇总：两组各一行，98% 是新数据" summary-live 'q["text"].split(" ｜ ")[0].startswith("Gemini 剩余 100%") and "重置" in q["text"].split(" ｜ ")[0] and q["text"].split(" ｜ ")[1] == "Claude/GPT 剩余 98%" and q["low"] is False'
check "汇总：用完时比例被省略，算 0%" summary-gemini-used-up 'q["text"].startswith("Gemini 剩余 0%") and q["low"] is True'
check "汇总：一组有两个窗口，各一行带说明" summary-two-windows 'q["text"].split(" ｜ ")[0].startswith("Gemini 今日 剩余 40%") and q["text"].split(" ｜ ")[1].startswith("Gemini 本周 剩余 100%") and q["text"].split(" ｜ ")[2] == "Claude/GPT 剩余 98%"'
check "汇总是空的：不显示（程序会退回查用户状态）" summary-empty 'q == {}'
exit $fail
