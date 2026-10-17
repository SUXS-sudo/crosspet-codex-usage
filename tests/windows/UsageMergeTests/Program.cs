using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using CodexUsageViewer;
using CrossPet;
class Program {
static void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); }
static string Snapshot(string time, long n) => "{\"timestamp\":\""+time+"\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":"+(n-10)+",\"output_tokens\":10,\"total_tokens\":"+n+"}}}}";
static string Meta(string id, bool child=false) => "{\"type\":\"session_meta\",\"payload\":{\"id\":\""+id+"\",\"source\":\""+(child?"subagent":"cli")+"\"}}";
static void Main(string[] args) {
var root=args[0]; Directory.CreateDirectory(root+"/sessions"); Directory.CreateDirectory(root+"/archived_sessions");
var lines=new[]{Meta("parent"),Snapshot("2026-10-15T15:59:00Z",100),Snapshot("2026-10-15T16:01:00Z",150),Snapshot("2026-10-15T16:02:00Z",150)};
File.WriteAllLines(root+"/sessions/parent.jsonl",lines); File.WriteAllLines(root+"/archived_sessions/copy.jsonl",lines);
File.WriteAllLines(root+"/sessions/child.jsonl", new[]{Meta("child",true),Snapshot("2026-10-16T01:00:00Z",40)});
File.WriteAllLines(root+"/sessions/missing.jsonl",new[]{Meta("missing")});
var store=new TaskUsageStore(); var r=store.Load(root);
Check(r.Tasks.Count==3 && r.Tasks.Sum(t=>t.Counts.Total)==190,"active/archive deduplication and cumulative snapshot high water");
Check(r.Tasks.SelectMany(t=>t.Days).Where(d=>d.Key==new DateTime(2026,10,16)).Sum(d=>d.Value.Total)==90,"Beijing midnight and today includes subagents");
Check(!r.Tasks.Single(t=>t.Id=="missing").HasUsage,"missing usage stays unknown");
File.AppendAllLines(root+"/sessions/child.jsonl",new[]{Snapshot("2026-10-16T02:00:00Z",60)});
Check(store.Load(root).Tasks.Single(t=>t.Id=="child").Counts.Total==60,"incremental cache sees file changes");
var day = new DateTime(2026,10,16);
var today = UsageMetrics.Sum(r.Tasks.Select(t => UsageMetrics.SelectCounts(t, day, day)));
Check(today.Total==90 && today.Input==80 && today.Output==10,"footer and list share Beijing daily increments");
Check(UsageMetrics.SelectCounts(r.Tasks.Single(t=>t.Id=="parent"),null,day).Total==150,"all-time selection preserves cumulative totals");
Check(UsageMetrics.SelectCounts(r.Tasks.Single(t=>t.Id=="parent"),day.AddDays(1),day.AddDays(1)).Total==0,"empty date range has zero usage");
Check(UsageMetrics.Sum(new TokenCounts[0]).Total==0 && UsageMetrics.Compact(1500)==(1.5).ToString("0.##")+"K","empty aggregation and compact token display");
var limits=new Dictionary<string,object>{{"primary",new Dictionary<string,object>{{"usedPercent",23},{"windowDurationMins",300}}},{"secondary",new Dictionary<string,object>{{"usedPercent",105},{"windowDurationMins",10080}}}};
Check(UsageSummary.Quota(limits)=="5小时剩余 77% · 本周剩余 0%","remaining quota labels and bounds");
Check(UsageSummary.Quota(new Dictionary<string,object>())=="暂无可用额度数据","unavailable quota does not become 100 percent");
var left=UsageSummary.Place(1000,300,1260,662,0,0,1920,1080,232,144,8);
var right=UsageSummary.Place(0,300,260,662,0,0,1920,1080,232,144,8);
var edge=UsageSummary.Place(-200,300,60,662,-1920,0,0,1080,232,144,8);
Check(left.x==760 && right.x==268 && edge.x>=-1920 && edge.x+232<=0,"card placement switches side and handles negative monitor coordinates");
Console.WriteLine("All merge checks passed.");
}
}
