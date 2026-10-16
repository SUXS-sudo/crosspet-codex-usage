using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CrossPet;

static class Program
{
    static int fails;
    static void Check(string name, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) fails++; }

    static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "crosspet-sprite-test", "characters");
        string file, mime;

        Check("普通立绘", SpritePath.TryResolve(root, "/characters/claude/idle.webp", out file, out mime)
            && file == Path.GetFullPath(Path.Combine(root, "claude", "idle.webp")) && mime == "image/webp");
        Check("png", SpritePath.TryResolve(root, "/characters/gpt/edge_left.png", out file, out mime) && mime == "image/png");
        Check("扩展名大写", SpritePath.TryResolve(root, "/characters/gpt/IDLE.WEBP", out file, out mime) && mime == "image/webp");
        Check("中文角色名（网址编码）", SpritePath.TryResolve(root, "/characters/%E5%8F%AF%E8%90%9D/idle.webp", out file, out mime)
            && file == Path.GetFullPath(Path.Combine(root, "可萝", "idle.webp")));
        Check("文件名里有空格（%20）", SpritePath.TryResolve(root, "/characters/my%20pet/idle%201.webp", out file, out mime));

        Check("角色目录是 ..", !SpritePath.TryResolve(root, "/characters/%2E%2E/settings.webp", out file, out mime));
        Check("文件名里编码的斜杠", !SpritePath.TryResolve(root, "/characters/claude/..%2F..%2Fsettings.webp", out file, out mime));
        Check("文件名里编码的反斜杠", !SpritePath.TryResolve(root, "/characters/claude/..%5C..%5Cx.webp", out file, out mime));
        Check("盘符冒号", !SpritePath.TryResolve(root, "/characters/claude/C%3Ax.webp", out file, out mime));
        Check("character.json 不给", !SpritePath.TryResolve(root, "/characters/gpt/character.json", out file, out mime));
        Check("数据目录里别的文件不给", !SpritePath.TryResolve(root, "/settings.json", out file, out mime));
        Check("多一层目录不给", !SpritePath.TryResolve(root, "/characters/a/b/c.webp", out file, out mime));
        Check("空的文件名", !SpritePath.TryResolve(root, "/characters/claude/", out file, out mime));
        Check("结尾是点", !SpritePath.TryResolve(root, "/characters/claude./idle.webp", out file, out mime));
        Check("控制字符", !SpritePath.TryResolve(root, "/characters/claude/a%00.webp", out file, out mime));

        // 藏起来时鼠标穿透（WS_EX_TRANSPARENT = 0x20）：只动这一位，别的样式原样保留
        const int layered = 0x80000, noActivate = 0x08000000, toolWindow = 0x80, transparent = 0x20;
        int ex = layered | noActivate | toolWindow;
        Check("藏起来：加上鼠标穿透", ConcealStyle.ClickThrough(ex, true) == (ex | transparent));
        Check("叫回来：去掉鼠标穿透", ConcealStyle.ClickThrough(ex | transparent, false) == ex);
        Check("重复藏不会出错", ConcealStyle.ClickThrough(ConcealStyle.ClickThrough(ex, true), true) == (ex | transparent));
        Check("没藏时叫回来不变", ConcealStyle.ClickThrough(ex, false) == ex);

        // Gemini（Antigravity）额度排版，样本和 macOS 的 tests/antigravity_quota_probe.sh 共用
        string Agy(string name)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "../../../../../fixtures/antigravity", name + ".json");
            var q = AntigravityQuota.Parse(Js(JsonDocument.Parse(File.ReadAllText(path)).RootElement));
            return q == null ? "null" : q["text"] + " | low=" + q["low"];
        }
        var a1 = Agy("ultra-only-model");
        Check("Gemini：不可用的模型不算进额度（" + a1 + "）", a1.StartsWith("Gemini 剩余 100%") && a1.Contains(" ｜ Claude/GPT 剩余 100% |") && a1.EndsWith("low=False"));
        var a2 = Agy("gemini-used-up");
        Check("Gemini：真用完了还是 0%（" + a2 + "）", a2.StartsWith("Gemini 剩余 0%") && a2.Contains(" ｜ Claude/GPT 剩余 100% |") && a2.EndsWith("low=True"));
        var a3 = Agy("two-pools-with-unavailable");
        Check("Gemini：两个池 + 不可用模型（" + a3 + "）", a3.StartsWith("Gemini 剩余 52%") && a3.Contains(" ｜ Claude/GPT 剩余 99% |") && !a3.Contains(" 0%") && a3.EndsWith("low=False"));

        var s1 = Agy("summary-live");
        Check("Gemini 汇总：两组各一行，98%（" + s1 + "）", s1.StartsWith("Gemini 剩余 100%") && s1.Contains(" ｜ Claude/GPT 剩余 98% |") && s1.EndsWith("low=False"));
        var s2 = Agy("summary-gemini-used-up");
        Check("Gemini 汇总：比例被省略算 0%（" + s2 + "）", s2.StartsWith("Gemini 剩余 0%") && s2.EndsWith("low=True"));
        var s3 = Agy("summary-two-windows");
        Check("Gemini 汇总：两个窗口各一行（" + s3 + "）", s3.StartsWith("Gemini 今日 剩余 40%") && s3.Contains(" ｜ Gemini 本周 剩余 100%") && s3.Contains(" ｜ Claude/GPT 剩余 98% |"));
        Check("Gemini 汇总：空的不显示", Agy("summary-empty") == "null");

        // 网页大小核对（只记日志不修正）：量两次、两次一致且和画布差 2% 以上才算对不上——拖滑块时网页还没排完，量到的是半路的数
        Check("网页大小：对得上不记", !ZoomCheck.Off(260, 362, 260, 362, 260, 362));
        Check("网页大小：差 1% 以内不记（取整误差）", !ZoomCheck.Off(260, 362, 258.5, 360, 258.5, 360));
        Check("网页大小：两次都偏小且一致才记", ZoomCheck.Off(260, 362, 208, 289.6, 208, 289.6));
        Check("网页大小：两次量的不一样（还在变）不记", !ZoomCheck.Off(488, 477, 594, 531, 415, 408));
        Check("网页大小：量到的数不对劲（0 或负）不记", !ZoomCheck.Off(260, 362, 0, 0, 0, 0));

        Console.WriteLine(fails == 0 ? "全部通过" : $"{fails} 项失败");
        return fails == 0 ? 0 : 1;
    }

    // 把 JSON 转成和 Windows 上 JavaScriptSerializer 一样的形状：对象 → Dictionary，数组 → object[]，整数 → int，小数 → decimal
    static object Js(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => e.EnumerateObject().ToDictionary(p => p.Name, p => Js(p.Value)),
        JsonValueKind.Array => e.EnumerateArray().Select(Js).ToArray(),
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.TryGetInt32(out var i) ? (object)i : e.GetDecimal(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
}
