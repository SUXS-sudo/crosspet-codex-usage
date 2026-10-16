using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CodexUsageViewer;

namespace CrossPet
{
    sealed partial class Pet
    {
        readonly TaskUsageStore usageStore = new TaskUsageStore();
        Task<TaskUsageResult> usageRead;
        TaskUsageResult usageResult;
        DateTime usageReadAt;
        string usageTokenText = "今日 Token  …", usageTokenDetail = "点击查看任务统计";
        TaskUsageWindow taskUsageWindow;
        bool usageRefreshing, usageQuotaStale;

        void StartUsageViewer()
        {
            // The simplified app always displays Codex quota; character choice is independent.
            Store.Settings["gptQuota"] = true;
            Store.Settings["agyQuota"] = false;
            Store.Settings["dsQuota"] = false;
            Store.Settings["quotaAggregate"] = false;
            Store.Settings["codexResets"] = false;
            Store.SaveSettings();
            Every(TimeSpan.FromSeconds(30), () => { if (!concealed && !sessionLocked) _ = RefreshUsageTokens(); });
            _ = RefreshUsageTokens();
        }

        async Task<TaskUsageResult> ReadUsage()
        {
            // Both views share one scanner/cache and coalesce concurrent refresh requests.
            if (usageRead != null) return await usageRead;
            if (usageResult != null && (DateTime.UtcNow - usageReadAt).TotalSeconds < 5) return usageResult;
            usageRead = Task.Run(() => usageStore.Load());
            try { usageResult = await usageRead; usageReadAt = DateTime.UtcNow; return usageResult; }
            finally { usageRead = null; }
        }

        async Task RefreshUsageTokens()
        {
            if (usageRefreshing) return;
            usageRefreshing = true;
            try
            {
                var result = await ReadUsage();
                var today = new TokenCounts();
                foreach (var t in result.Tasks)
                    if (t.Days.TryGetValue(TaskUsageStore.Now.Date, out var day)) today.Add(day);
                bool any = result.Tasks.Any(t => t.HasUsage);
                usageTokenText = result.MissingSessions && !any ? "今日 Token  —" :
                    "今日 Token  " + (any ? TaskUsageWindow.Compact(today.Total) : "—");
                usageTokenDetail = "北京时间今日累计增量，包含子代理\n" + today.Total.ToString("N0") +
                    " Token\n输入 " + today.Input.ToString("N0") + " · 输出 " + today.Output.ToString("N0") +
                    "\n缺失记录的任务不视为零消耗";
                if (result.FailedFiles + result.MalformedLines + result.Regressions > 0) usageTokenDetail += "\n记录不完整，详情页可查看说明";
                PushUsageFooter();
            }
            catch (Exception e) { usageTokenText = "今日 Token  —"; usageTokenDetail = "读取失败，可从右键菜单刷新"; PushUsageFooter(); Store.Log("Token 用量: " + e.Message); }
            finally { usageRefreshing = false; }
        }

        void RefreshUsage()
        {
            usageReadAt = DateTime.MinValue;
            RefreshCodexQuota(CodexWhy.Now);
            _ = RefreshUsageTokens();
        }

        string UsageQuotaText()
        {
            if (!Store.Flag("gptQuota")) return "Codex 额度显示已关闭";
            if (codexLast == null) return string.IsNullOrEmpty(codexNote) ? "正在读取 Codex 额度…" : codexNote;
            return UsageSummary.Quota(codexLast) + (usageQuotaStale ? $" · {codexLastAt:HH:mm} 缓存" : "");
        }

        void UpdateUsageQuota()
        {
            taskUsageWindow?.UpdateQuota();
        }

        void OpenTaskUsage()
        {
            if (taskUsageWindow == null)
            {
                taskUsageWindow = new TaskUsageWindow(UsageQuotaText, ReadUsage);
                taskUsageWindow.Closed += (_, __) => taskUsageWindow = null;
            }
            ApplyUsageTheme();
            taskUsageWindow.Show();
            if (taskUsageWindow.WindowState == WindowState.Minimized) taskUsageWindow.WindowState = WindowState.Normal;
            taskUsageWindow.Activate();
        }

        void PushUsageFooter() => Js($"setUsageFooter({Q(usageTokenText)}, {Q(usageTokenDetail)})");

        void ApplyUsageTheme()
        {
            if (taskUsageWindow == null) return;
            var m = Store.Json.DeserializeObject(ManifestJson) as System.Collections.Generic.Dictionary<string, object>;
            var chars = m?["characters"] as System.Collections.Generic.Dictionary<string, object>;
            var c = chars != null && chars.TryGetValue(current, out var v) ? v as System.Collections.Generic.Dictionary<string, object> : null;
            var accent = c != null && c.TryGetValue("accent", out var a) ? a as string : null;
            var name = c != null && c.TryGetValue("name", out var n) ? n as string : current;
            taskUsageWindow.SetPetTheme(accent ?? "#7b5cd6", name);
        }
    }
}
