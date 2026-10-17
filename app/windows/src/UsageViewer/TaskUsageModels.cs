using System;
using System.Collections.Generic;

namespace CodexUsageViewer;

internal sealed class TokenCounts
{
    public long Input, Cached, Output, Reasoning, Total;
    public void Add(TokenCounts n) { Input += n.Input; Cached += n.Cached; Output += n.Output; Reasoning += n.Reasoning; Total += n.Total; }
    public static TokenCounts Read(Dictionary<string, object> d) { return new TokenCounts { Input = JsonValue.Number(d, "input_tokens"), Cached = JsonValue.Number(d, "cached_input_tokens"), Output = JsonValue.Number(d, "output_tokens"), Reasoning = JsonValue.Number(d, "reasoning_output_tokens"), Total = JsonValue.Number(d, "total_tokens") }; }
    public TokenCounts Difference(TokenCounts previous) { return new TokenCounts { Input = Math.Max(0, Input - previous.Input), Cached = Math.Max(0, Cached - previous.Cached), Output = Math.Max(0, Output - previous.Output), Reasoning = Math.Max(0, Reasoning - previous.Reasoning), Total = Math.Max(0, Total - previous.Total) }; }
}

internal sealed class TaskUsage
{
    public string Id = "", Title = "", Directory = "", FilePath = "", Model = "";
    public bool Archived, Subagent, HasUsage;
    public int Malformed, Regressions;
    public DateTime LastActivity;
    public TokenCounts Counts = new TokenCounts();
    public Dictionary<DateTime, TokenCounts> Days = new Dictionary<DateTime, TokenCounts>();
}

internal sealed class TaskUsageResult
{
    public List<TaskUsage> Tasks = new List<TaskUsage>();
    public int FailedFiles, MalformedLines, Regressions;
    public string Root;
    public bool MissingSessions;
}

internal sealed class TaskUsageRow
{
    public TaskUsage Task;
    public TokenCounts Counts;
    public string Title { get { return Task.Title; } }
    public string Kind { get { return Task.Subagent ? "子代理" : Task.Archived ? "已归档" : "聊天"; } }
    public long Total { get { return Counts.Total; } }
    public long Input { get { return Counts.Input; } }
    public long Output { get { return Counts.Output; } }
    public double CacheRate { get { return Counts.Input == 0 ? 0 : (double)Counts.Cached / Counts.Input; } }
    public DateTime Activity { get { return Task.LastActivity; } }
    public string TotalText { get { return Task.HasUsage ? Total.ToString("N0") : "—"; } }
    public string InputText { get { return Task.HasUsage ? Input.ToString("N0") : "—"; } }
    public string OutputText { get { return Task.HasUsage ? Output.ToString("N0") : "—"; } }
    public string Cache { get { return Counts.Input == 0 ? "—" : (100.0 * Counts.Cached / Counts.Input).ToString("F1") + "%"; } }
    public double Weight { get; set; }
    public string Updated { get { return Task.LastActivity.ToString("MM-dd HH:mm"); } }
}
