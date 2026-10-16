#!/bin/sh
# 文字大小：窗口放在哪（Mac 版 App.placeWindow 的纯计算），先 app/build.sh 编译，再 tests/window_place_probe.sh
# 参数：锚点 旧x 旧y 旧宽 旧高 新宽 新高 旧底栏 新底栏 可见区底边（屏幕点，y 向上）
set -eu
cd "$(dirname "$0")/.."
BIN=build/CrossPet.app/Contents/MacOS/CrossPet
fail=0
check() {   # 名字 期望输出 参数…
  name=$1; want=$2; shift 2
  got=$(perl -e 'alarm 5; exec @ARGV' "$BIN" --window-place-probe "$@" 2>/dev/null || echo "（没有输出）")
  if [ "$got" = "$want" ]; then echo "PASS $name"; else echo "FAIL $name：期望 $want，得到 $got"; fail=1; fi
}
# 启动 / 网页重载：存下的位置本来就是这个画布的窗口，左下角不动，只换大小（审查第 1 条：每次启动往左下漂）
check corner-startup "100 200 252 265.8" corner 100 200 156 217.2 252 265.8 37.2 61.8 0
# 改人物大小：底边中点不动，和以前一样；底栏的网页高度变了也不挪（审查第 2 条：屏幕上的底栏没变）
check bottom-size "152 200 156 217.2" bottom 100 200 260 362 156 217.2 62 37.2 0
check bottom-unlocked "148 200 252 265.8" bottom 100 200 348 341.4 252 265.8 61.8 61.8 0
# 改文字大小：底栏在屏幕上变高多少，窗口底边就往下挪多少，人物不动；水平按中心
check feet-text "52 175.4 252 265.8" feet 100 200 156 217.2 252 265.8 37.2 61.8 0
# 往下扩出了可见区：挪回可见区底边
check feet-clamp "52 20 252 265.8" feet 100 30 156 217.2 252 265.8 37.2 61.8 20
# 本来就放在可见区下面：不往上挪到比原来还高
check feet-already-below "52 10 252 265.8" feet 100 10 156 217.2 252 265.8 37.2 61.8 20
exit $fail
