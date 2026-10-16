using System;
using System.Collections.Generic;
using System.Linq;

namespace CrossPet
{
    static class UsageSummary
    {
        public static string Quota(Dictionary<string, object> limits)
        {
            var parts = new List<string>();
            foreach (var key in new[] { "primary", "secondary" })
            {
                if (!limits.TryGetValue(key, out var value) || !(value is Dictionary<string, object> w) ||
                    !w.TryGetValue("usedPercent", out var used) || used == null) continue;
                var percent = Math.Max(0, Math.Min(100, 100 - Convert.ToDouble(used)));
                var minutes = w.TryGetValue("windowDurationMins", out var m) && m != null ? Convert.ToInt32(m) : 0;
                var label = minutes >= 10080 ? "本周" : minutes >= 1440 ? "今日" : minutes > 0 ? $"{Math.Max(1, minutes / 60)}小时" : key == "primary" ? "短期" : "长期";
                parts.Add($"{label}剩余 {Math.Round(percent)}%");
            }
            return parts.Any() ? string.Join(" · ", parts) : "暂无可用额度数据";
        }

        public static (double x, double y) Place(double left, double top, double right, double bottom,
            double workLeft, double workTop, double workRight, double workBottom, double width, double height, double gap)
        {
            double x = left - width - gap;
            if (x < workLeft) x = right + gap;
            x = Math.Max(workLeft, Math.Min(x, workRight - width));
            double y = Math.Max(workTop, Math.Min(bottom - height - 30, workBottom - height));
            return (x, y);
        }
    }
}
