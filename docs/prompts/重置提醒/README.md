# Codex 重置提醒 · 立绘（GPT 原版 + 降智龙崽，各 5 张）

设置里打开「Codex 重置提醒」后，GPT 被问「会重置吗？」、或官方宣布要重置时用的动作。

| 文件名 | 什么时候用 | 没画时退回 |
|---|---|---|
| `reset_check` | 被问时先查一下（刷手机） | `thinking` |
| `reset_bell` | 官方已宣布要重置（摇铃铛：「要来了」） | `reset` |
| `reset_hope` | 预测概率 ≥ 70% | `happy` |
| `reset_maybe` | 30–69%，半信半疑 | `hum` |
| `reset_nope` | 低于 30%、没风声、连不上那个网站 | `sulk` |

`reset`（举起发光的重置卡）仍然只用在「额度真的重置了」的庆祝，和 `reset_bell` 区分：一个是「要来了」，一个是「来了」。

## 怎么画

- [01-GPT原版.md](01-GPT原版.md)：附 `../ref-gpt.webp`，一次发完 5 张；画 `reset_hope` 时另附 `characters/gpt/raw/reset.png`，让重置卡长得一样
- [02-降智龙崽.md](02-降智龙崽.md)：附 `characters/gpt-onesie/raw/idle.png`；画 `reset_hope` 时另附 `characters/gpt-onesie/raw/tired.png`，让零食袋长得一样

画好后放进 `characters/gpt/raw/`、`characters/gpt-onesie/raw/`，交给 Claude 去背景、裁切、转 webp。画一张生效一张，没画的照常用上表右栏的动作。

验收：缩到约 200px 看，一眼认得出是在「查手机 / 摇铃 / 期待 / 怀疑 / 摇头」；道具都贴着她，不悬空；龙崽肚子上的结饰布贴看得见。
