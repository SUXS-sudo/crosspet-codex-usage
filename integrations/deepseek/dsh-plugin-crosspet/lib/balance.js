// DeepSeek 余额 → 桌宠的额度文件。用 Key 查和用账号查两条路都走这里，规则只有一份。
// 账户可能同时有人民币和美元（#10）：官方余额接口一次返回两项，而且顺序每次可能不一样，
// 所以不能取第 0 项，要按币种认、各币种分别汇总、分别显示。

const SYMBOL = { CNY: "¥", USD: "$" };
const ORDER = ["CNY", "USD"];  // 显示顺序固定：人民币在前，不跟着接口返回的顺序跳
// 余额低的门槛按币种分：¥5 和 $0.7 差不多是同一笔钱
export const DEFAULT_LOW = { CNY: 5, USD: 0.7 };

// 插件配置 lowBalance：数字 = 人民币门槛（老写法，美元还用默认）；对象 = 按币种，如 { CNY: 10, USD: 1.5 }
export function lowThresholds(config) {
  if (typeof config === "number") return { ...DEFAULT_LOW, CNY: config };
  if (config && typeof config === "object") return { ...DEFAULT_LOW, ...config };
  return { ...DEFAULT_LOW };
}

// entries：[{ currency, amount }]，同一币种可以出现多次（现金钱包 + 赠送钱包），这里加起来
// 返回写进 deepseek-quota.json 的内容；没有任何币种时返回 null
export function summarize(entries, { available = true, low = DEFAULT_LOW } = {}) {
  const totals = {};
  for (const e of entries || []) {
    const c = String(e?.currency || "").toUpperCase();
    if (!c) continue;
    totals[c] = (totals[c] || 0) + (Number(e.amount) || 0);
  }
  const currencies = Object.keys(totals).sort((a, b) => rank(a) - rank(b) || a.localeCompare(b));
  if (!currencies.length) return null;
  const balances = {};
  for (const c of currencies) balances[c] = round2(totals[c]);
  // 显示：余额为 0 的币种不占地方（比如只充过美元的人，接口也可能带一项 ¥0.00）；全是 0 就显示主币种
  const shown = currencies.filter((c) => balances[c] > 0);
  const text = "余额 " + (shown.length ? shown : currencies.slice(0, 1)).map((c) => fmt(c, balances[c])).join("｜");
  const primary = currencies[0];
  return {
    text,
    balance: balances[primary],  // 老版本桌宠只认这一个数：人民币优先，和以前一样
    balances,                    // 新版本桌宠按币种分别比，判断是不是充值
    // 所有币种都低于各自的门槛才算低：有一种还够用，她就不该喊饿
    low: !available || currencies.every((c) => balances[c] < (low[c] ?? low.CNY ?? DEFAULT_LOW.CNY)),
  };
}

function rank(c) { const i = ORDER.indexOf(c); return i < 0 ? ORDER.length : i; }
function round2(n) { return Math.round(n * 100) / 100; }
function fmt(c, n) { return (SYMBOL[c] ?? `${c} `) + n.toFixed(2); }
