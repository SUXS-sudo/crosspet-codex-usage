using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace CrossPet
{
    sealed partial class Pet
    {
        // ---------------- 反馈问题 ----------------
        // 点选「哪方面」+ 必填一句「具体是哪里」，附上版本、系统、接入情况、最近日志，
        // 由页面拼成卡片，发到飞书群机器人。地址可在 settings.json 里用 "feedbackURL" 改（测试用）
        const string FeedbackEndpoint = "https://open.feishu.cn/open-apis/bot/v2/hook/854f8a9a-d3f4-4705-b6f8-15cc007f714b";

        void OpenFeedback()
        {
            if (feedback == null) { feedback = new FeedbackWindow(this, env); feedback.Closed += () => feedback = null; }
            feedback.Show();
        }

        /// <summary>每台机器一个随机 id，只用来让服务端限流、合并同一个人的多条反馈</summary>
        static string InstallId
        {
            get
            {
                if (Store.Settings.TryGetValue("installId", out var v) && v is string s && s != "") return s;
                var id = Guid.NewGuid().ToString();
                Store.Settings["installId"] = id; Store.SaveSettings();
                return id;
            }
        }

        /// <summary>去掉日志里的隐私：用户目录换成 ~，路径里的用户名、像 API Key 的字符串打码，顺便去掉终端颜色控制符</summary>
        static string Redact(string text)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var s = string.IsNullOrEmpty(home) ? text : text.Replace(home, "~");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\x1B\[[0-9;]*[A-Za-z]", "");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"(\\Users\\|/Users/)[^\\/\s]+", "$1<user>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            s = System.Text.RegularExpressions.Regex.Replace(s, @"sk-[A-Za-z0-9_\-]{8,}", "sk-***");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"(?i)(bearer|token|key|secret|password)([\s""':=]+)[A-Za-z0-9_\-\.]{8,}", "$1$2***");
            return s;
        }

        Dictionary<string, object> FeedbackInfo()
        {
            var lines = (Store.ReadText(Path.Combine(Store.Data, "windows.log")) ?? "").Replace("\r\n", "\n").Split('\n');
            var tail = Redact(string.Join("\n", lines.Skip(Math.Max(0, lines.Length - 60))).Trim());
            if (tail.Length > 6000) tail = tail.Substring(tail.Length - 6000);
            return new Dictionary<string, object>
            {
                ["version"] = Store.Version, ["platform"] = "windows",
                ["os"] = $"{Environment.OSVersion.VersionString} {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}",
                ["character"] = current ?? "", ["integrations"] = integrated ?? new List<string>(), ["log"] = tail, ["install"] = InstallId,
                ["displays"] = DisplaysSummary(),
                ["runtime"] = Redact(RuntimeSummary()), ["diag"] = Redact(diagText),
            };
        }
        string lastUpdateCheck = "还没查过", diagText = "";
        readonly DateTime launchedAt = DateTime.Now;
        /// <summary>反馈附带的运行状态：一次把排查要用的都列出来（不读对话、不带 Key）</summary>
        string RuntimeSummary()
        {
            var L = new List<string>();
            try
            {
                L.Add($"前台程序 {Native.ForegroundExe()}");
                var vis = Window?.IsVisible == true && !concealed;   // 隐身（全屏 / 快捷键藏起来）也算不显示
                var rect = "";
                if (hwnd != IntPtr.Zero && Native.GetWindowRect(hwnd, out var r))
                {
                    var on = Forms.Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(new System.Drawing.Rectangle(r.Left + 40, r.Top + 40, Math.Max(1, r.Right - r.Left - 80), Math.Max(1, r.Bottom - r.Top - 80))));
                    rect = $"窗口 {r.Left},{r.Top} {r.Right - r.Left}×{r.Bottom - r.Top}{(on ? "" : "（不在任何屏幕里）")}；";
                }
                L.Add($"{rect}显示中 {(vis ? "是" : "否")}，因全屏藏着 {(hiddenForFullscreen ? "是" : "否")}，置顶 {(Window?.Topmost == true ? "是" : "否")}；层级 {LayerMode}；贴边 {dock ?? "无"}{(dock != null ? (dockOut ? "·跑出来了" : "·缩着") : "")}；大小 {(int)(Scale * 100)}%（文字 {(TextLinked ? "跟着" : (int)(TextScale * 100) + "%")}，画布 {Math.Round(pageW)}×{Math.Round(pageH)}，网页缩放 {Num(Math.Round((web?.ZoomFactor ?? 0), 3))}，系统缩放 {(Window != null ? Math.Round(VisualTreeHelper.GetDpi(Window).DpiScaleX * 100) : 0)}%）；网页 {(ready ? "就绪" : "没就绪")}");
                var skins = Store.Settings.TryGetValue("skins", out var sv) ? Store.Json.Serialize(sv) : "";
                L.Add($"角色 {current}{(skins == "" || skins == "{}" ? "" : "，衣服 " + skins)}；设置：名字 {(ShowName ? "开" : "关")}，光晕 {AuraMode}，省电 {(PowerSave ? "开" : "关")}，彩蛋 {(EggsEnabled ? "开" : "关")}，"
                      + $"跟随前台 {(Store.Flag("followApps") ? "开" : "关")}，跟随事件 {(Store.Flag("followEvents") ? "开" : "关")}，AI 干活自动出现 {(AIAutostart ? "开" : "关")}，贴边 {(EdgeDockEnabled ? "开" : "关")}，"
                      + $"GPT 额度 {(Store.Flag("gptQuota") ? "开" : "关")}，Gemini 额度 {(Store.Flag("agyQuota") ? "开" : "关")}，DeepSeek 余额 {(DsQuotaEnabled ? "开" : "关")}，额度合并 {(QuotaAggregate ? "开" : "关")}，重置提醒 {(CodexResets ? "开" : "关")}，登录启动 {(LoginEnabled ? "开" : "关")}");
                var now = DateTime.UtcNow;
                var poses = lastPose.Select(p => $"{p.Key}={p.Value.pose}（{(int)(now - p.Value.at).TotalSeconds}秒前）").OrderBy(x => x).ToList();
                L.Add("最近状态 " + (poses.Count == 0 ? "无" : string.Join("，", poses)));
                L.Add("最近收到消息 " + string.Join("，", SeenKeys.Keys.OrderBy(k => k).Select(k => $"{k} {AgoText(SeenAt(SeenKeys[k]))}")));
                if (Store.Flag("gptQuota")) L.Add($"GPT 额度最近一次读到 {lastGptQuota}");
                string wv = "?"; try { wv = CoreWebView2Environment.GetAvailableBrowserVersionString(); } catch { }
                L.Add($"装在 {Store.Install}；数据 {Store.Data}；随包 Python {(File.Exists(Store.Python) ? "在" : "不在")}；WebView2 {wv}；检查更新 {lastUpdateCheck}；已运行 {(int)(DateTime.Now - launchedAt).TotalMinutes} 分钟");
            }
            catch (Exception e) { L.Add("运行状态出错：" + e.Message); }
            return string.Join("\n", L);
        }
        /// <summary>反馈附带的显示信息：几块显示器、各自大小和相对主屏的位置、桌宠在哪块、显示层级 / 全屏隐藏 / 贴边</summary>
        string DisplaysSummary()
        {
            try
            {
                var all = Forms.Screen.AllScreens;
                var main = Forms.Screen.PrimaryScreen.Bounds;
                var mine = hwnd == IntPtr.Zero ? null : Forms.Screen.FromHandle(hwnd);
                var parts = all.Select(s =>
                {
                    var b = s.Bounds;
                    var where = s.Primary ? "主屏" : b.Left >= main.Right ? "右侧" : b.Right <= main.Left ? "左侧" : b.Bottom <= main.Top ? "上方" : b.Top >= main.Bottom ? "下方" : "重叠";
                    return $"{b.Width}×{b.Height} {where}{(mine != null && s.DeviceName == mine.DeviceName ? "（桌宠在这）" : "")}";
                });
                var flags = new List<string> { "层级 " + LayerMode, HideFullscreen ? "全屏隐藏" : "全屏不隐藏" };
                if (dock != null) flags.Add(dock == "left" ? "贴左边" : "贴右边");
                if (hiddenForFullscreen) flags.Add("此刻因全屏藏着");
                if (hwnd != IntPtr.Zero && Native.GetWindowRect(hwnd, out var wr))
                {
                    var onAny = all.Any(sc => sc.WorkingArea.IntersectsWith(new System.Drawing.Rectangle(wr.Left + 40, wr.Top + 40, Math.Max(1, wr.Right - wr.Left - 80), Math.Max(1, wr.Bottom - wr.Top - 80))));
                    flags.Add($"窗口 {wr.Left},{wr.Top} {wr.Right - wr.Left}×{wr.Bottom - wr.Top}{(onAny ? "" : "（不在任何屏幕里）")}{(Window.IsVisible && !concealed ? "" : "（隐藏）")}");
                }
                return $"{all.Length} 块：{string.Join("、", parts)}；{string.Join("，", flags)}";
            }
            catch { return ""; }
        }

        public void HandleFeedback(Dictionary<string, object> msg)
        {
            var cmd = msg.TryGetValue("cmd", out var c) ? c as string : null;
            if (cmd != "send")
            {
                feedback?.Run($"setInfo({Store.Json.Serialize(FeedbackInfo())})");
                if (integrated == null) RefreshIntegrated(() => feedback?.Run($"setInfo({Store.Json.Serialize(FeedbackInfo())})"));
                if (File.Exists(Store.Python))
                    Task.Run(() =>
                    {
                        var (output, _) = RunIntegrate("diagnose");
                        diagText = output.Trim(); if (diagText.Length > 4000) diagText = diagText.Substring(0, 4000);
                        Window.Dispatcher.Invoke(() => feedback?.Run($"setInfo({Store.Json.Serialize(FeedbackInfo())})"));
                    });
                return;
            }
            var endpoint = Store.Settings.TryGetValue("feedbackURL", out var u) && u is string us && us != "" ? us : FeedbackEndpoint;
            void Done(bool ok, string text)
            {
                Window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    feedback?.Run($"onSent({(ok ? "true" : "false")}, {Q(text)})");
                    if (ok) Js("show('happy', true); say('收到反馈啦，谢谢你！')");
                }));
            }
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var url)) { Done(false, "反馈暂时关闭了"); return; }
            var body = msg.TryGetValue("body", out var b) ? b as string : null;
            if (body == null || Encoding.UTF8.GetByteCount(body) > 20000) { Done(false, "内容太长了，删短一点再发"); return; }
            // 本机限流：一小时最多 5 条（飞书群机器人的地址是公开的，不能让一台机器刷屏）
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var recent = ((Store.Settings.TryGetValue("feedbackTimes", out var ft) ? ft as object[] : null) ?? new object[0])
                .Select(x => { try { return (long)Convert.ToDouble(x, System.Globalization.CultureInfo.InvariantCulture); } catch { return 0L; } }).Where(x => now - x < 3600).ToList();
            if (recent.Count >= 5) { Done(false, "发得有点多啦，过一会儿再发"); return; }
            Task.Run(async () =>
            {
                try
                {
                    var res = await http.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));
                    // 飞书不论成败都回 HTTP 200，看返回里的 code：0 才是发到了群里
                    var reply = Store.Json.DeserializeObject(await res.Content.ReadAsStringAsync()) as Dictionary<string, object>;
                    var code = reply != null && reply.TryGetValue("code", out var c2) ? Convert.ToInt32(c2) : -1;
                    if (code == 0)
                    {
                        await Window.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            recent.Add(now);
                            Store.Settings["feedbackTimes"] = recent.Cast<object>().ToArray(); Store.SaveSettings();
                        }));
                        Done(true, "");
                    }
                    else if (code == 11232) Done(false, "反馈的人有点多，过一会儿再发");
                    else { Store.Log("发送反馈失败: " + code); Done(false, $"没发出去（{code}），稍后再试试"); }
                }
                catch (Exception e) { Store.Log("发送反馈失败: " + e.Message); Done(false, "连不上网络，稍后再试试"); }
            });
        }
    }
}
