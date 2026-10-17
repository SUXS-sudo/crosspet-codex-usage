using System;
using System.Collections.Generic;

namespace CodexUsageViewer;

// Shared calculations for the pet footer, task list and daily chart. No UI dependencies.
internal static class UsageMetrics
{
    public static TokenCounts SelectCounts(TaskUsage task, DateTime? start, DateTime end)
    {
        if (!start.HasValue) return task.Counts;
        var counts = new TokenCounts();
        foreach (var day in task.Days)
            if (day.Key >= start.Value.Date && day.Key <= end) counts.Add(day.Value);
        return counts;
    }

    public static TokenCounts Sum(IEnumerable<TokenCounts> values)
    {
        var counts = new TokenCounts();
        foreach (var value in values) counts.Add(value);
        return counts;
    }

    public static string Compact(long value) =>
        value >= 1000000000 ? (value / 1000000000.0).ToString("0.##") + "B" :
        value >= 1000000 ? (value / 1000000.0).ToString("0.##") + "M" :
        value >= 1000 ? (value / 1000.0).ToString("0.##") + "K" : value.ToString("N0");
}
