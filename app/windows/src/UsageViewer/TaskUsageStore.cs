using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace CodexUsageViewer;

internal sealed class TokenCounts
{
    public long Input, Cached, Output, Reasoning, Total;
    public TokenCounts Copy() { return (TokenCounts)MemberwiseClone(); }
    public void Add(TokenCounts n) { Input += n.Input; Cached += n.Cached; Output += n.Output; Reasoning += n.Reasoning; Total += n.Total; }
    public static TokenCounts Read(Dictionary<string, object> d) { return new TokenCounts { Input = JsonValue.Number(d, "input_tokens"), Cached = JsonValue.Number(d, "cached_input_tokens"), Output = JsonValue.Number(d, "output_tokens"), Reasoning = JsonValue.Number(d, "reasoning_output_tokens"), Total = JsonValue.Number(d, "total_tokens") }; }
    public TokenCounts Difference(TokenCounts previous) { return new TokenCounts { Input = Math.Max(0, Input - previous.Input), Cached = Math.Max(0, Cached - previous.Cached), Output = Math.Max(0, Output - previous.Output), Reasoning = Math.Max(0, Reasoning - previous.Reasoning), Total = Math.Max(0, Total - previous.Total) }; }
}

internal static class JsonValue
{
    public static Dictionary<string, object> Object(Dictionary<string, object> d, string key) { object o; return d != null && d.TryGetValue(key, out o) ? o as Dictionary<string, object> : null; }
    public static string Text(Dictionary<string, object> d, string key) { object o; return d != null && d.TryGetValue(key, out o) && o != null ? Convert.ToString(o, CultureInfo.InvariantCulture) : ""; }
    public static long Number(Dictionary<string, object> d, string key) { long n; return long.TryParse(Text(d, key), out n) ? Math.Max(0, n) : 0; }
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

internal sealed class TaskUsageStore
{
    private sealed class Entry { public long Length, Modified; public TaskUsage Task; }
    private readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 150 };
    internal static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
    internal static DateTime Now { get { return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone).DateTime; } }

    public TaskUsageResult Load(string root = null)
    {
        root = root ?? Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrWhiteSpace(root)) root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        var result = new TaskUsageResult { Root = root, MissingSessions = !System.IO.Directory.Exists(Path.Combine(root, "sessions")) };
        var names = new Dictionary<string, string>();
        string index = Path.Combine(root, "session_index.jsonl");
        if (File.Exists(index))
        {
            try { using (var stream = Open(index)) using (var reader = new StreamReader(stream, Encoding.UTF8)) { string line; while ((line = reader.ReadLine()) != null) { try { var d = json.Deserialize<Dictionary<string, object>>(line); string id = JsonValue.Text(d, "id"), name = JsonValue.Text(d, "thread_name"); if (id.Length > 0 && name.Length > 0) names[id] = name; } catch { } } } }
            catch (IOException) { result.FailedFiles++; }
            catch (UnauthorizedAccessException) { result.FailedFiles++; }
        }
        var seen = new Dictionary<string, TaskUsage>();
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string folder in new[] { "sessions", "archived_sessions" })
        {
            string path = Path.Combine(root, folder);
            if (!System.IO.Directory.Exists(path)) continue;
            string[] files;
            try { files = System.IO.Directory.GetFiles(path, "*.jsonl", SearchOption.AllDirectories); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { result.FailedFiles++; continue; }
            foreach (string file in files)
            {
                present.Add(file);
                try
                {
                    var info = new FileInfo(file);
                    Entry entry;
                    if (!cache.TryGetValue(file, out entry) || entry.Length != info.Length || entry.Modified != info.LastWriteTimeUtc.Ticks)
                    {
                        entry = new Entry { Length = info.Length, Modified = info.LastWriteTimeUtc.Ticks, Task = Parse(file, folder == "archived_sessions") };
                        cache[file] = entry;
                    }
                    var task = entry.Task;
                    string name;
                    if (names.TryGetValue(task.Id, out name)) task.Title = name;
                    TaskUsage old;
                    // The same thread can occur in both active and archived storage. Never sum copies.
                    if (!seen.TryGetValue(task.Id, out old) || task.Counts.Total > old.Counts.Total || (task.Counts.Total == old.Counts.Total && task.LastActivity > old.LastActivity)) seen[task.Id] = task;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { result.FailedFiles++; }
            }
        }
        foreach (string old in cache.Keys.Where(k => !present.Contains(k)).ToArray()) cache.Remove(old);
        result.Tasks = seen.Values.ToList();
        result.MalformedLines = result.Tasks.Sum(t => t.Malformed);
        result.Regressions = result.Tasks.Sum(t => t.Regressions);
        return result;
    }

    private static FileStream Open(string path) { return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); }

    private TaskUsage Parse(string path, bool archived)
    {
        var task = new TaskUsage { FilePath = path, Archived = archived, Id = Path.GetFileNameWithoutExtension(path), LastActivity = TimeZoneInfo.ConvertTimeFromUtc(File.GetLastWriteTimeUtc(path), Zone) };
        using (var stream = Open(path)) using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                // Avoid deserializing tool output and full prompt histories.
                if (!line.Contains("session_meta") && !line.Contains("token_count") && !(task.Model.Length == 0 && line.Contains("turn_context"))) continue;
                Dictionary<string, object> e;
                try { e = json.Deserialize<Dictionary<string, object>>(line); }
                catch { task.Malformed++; continue; }
                var payload = JsonValue.Object(e, "payload");
                if (payload == null) continue;
                string kind = JsonValue.Text(e, "type");
                if (kind == "session_meta")
                {
                    string id = JsonValue.Text(payload, "id");
                    if (id.Length > 0) task.Id = id;
                    task.Directory = JsonValue.Text(payload, "cwd");
                    object source;
                    if (payload.TryGetValue("source", out source)) task.Subagent = (source is Dictionary<string, object>) || Convert.ToString(source).IndexOf("subagent", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                if (kind == "turn_context") task.Model = JsonValue.Text(payload, "model");
                if (kind != "event_msg" || JsonValue.Text(payload, "type") != "token_count") continue;
                var info = JsonValue.Object(payload, "info");
                var usage = JsonValue.Object(info, "total_token_usage");
                if (usage == null) continue;
                var next = TokenCounts.Read(usage);
                // Use cumulative high-water values. Repeated snapshots and context compaction
                // must not charge the same tokens a second time.
                task.HasUsage = true;
                if (next.Total < task.Counts.Total) { task.Regressions++; continue; }
                var delta = next.Difference(task.Counts);
                DateTimeOffset time;
                if (DateTimeOffset.TryParse(JsonValue.Text(e, "timestamp"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out time))
                {
                    var day = TimeZoneInfo.ConvertTime(time, Zone).Date;
                    TokenCounts daily;
                    if (!task.Days.TryGetValue(day, out daily)) task.Days[day] = daily = new TokenCounts();
                    daily.Add(delta);
                }
                task.Counts = next;
            }
        }
        task.Title = "未命名任务 · " + task.Id.Substring(0, Math.Min(8, task.Id.Length));
        return task;
    }
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
