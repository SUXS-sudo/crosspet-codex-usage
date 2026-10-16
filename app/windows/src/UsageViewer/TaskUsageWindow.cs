using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using Microsoft.Win32;

namespace CodexUsageViewer;

internal sealed class TaskUsageWindow : Window
{
    private readonly Func<string> quotaText;
    private readonly Func<Task<TaskUsageResult>> load;
    
    private TaskUsageResult data;
    private readonly TextBox search = new TextBox { Width = 250, Height = 32, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(9, 0, 9, 0) };
    private readonly ComboBox period = new ComboBox { Width = 110, Height = 32 };
    private readonly ComboBox sort = new ComboBox { Width = 130, Height = 32 };
    private readonly DatePicker from = new DatePicker { Width = 126, Height = 32 };
    private readonly DatePicker to = new DatePicker { Width = 126, Height = 32 };
    private readonly CheckBox children = new CheckBox { Content = "包含子代理", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock status = Text("正在读取本地记录…", 12, "#697468");
    private readonly TextBlock detail = Text("选择任务查看明细", 12, "#5C635C");
    private readonly TextBlock quota = Text("", 13, "#5C635C");
    private readonly TextBlock totalValue = Text("—", 26, "#23262A"), inputValue = Text("—", 26, "#23262A"), outputValue = Text("—", 26, "#23262A"), cacheValue = Text("—", 26, "#539169");
    private readonly TextBlock taskCount = Text("", 12, "#697468");
    private readonly Grid chart = new Grid { Height = 80, Margin = new Thickness(0, 5, 0, 0) };
    private readonly DataGrid table = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false, CanUserResizeRows = false, RowHeight = 38, HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.None, BorderThickness = new Thickness(0), Background = Brushes.White, AlternatingRowBackground = Brush("#FAFAF7"), SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow, FontSize = 13, EnableRowVirtualization = true };
    private readonly Button refresh = Button("刷新数据");
    private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
    private bool busy, ready, closed;
    private string petAccent = "#7b5cd6", petName = "Gemini";
    internal TaskUsageResult Data { get { return data; } }

    public TaskUsageWindow(Func<string> quotaText, Func<Task<TaskUsageResult>> load)
    {
        this.quotaText = quotaText; this.load = load;
        Resources.MergedDictionaries.Add(UsageTheme.Create());
        Title = "任务 Token 统计 · CrossPet";
        Width = 1230; Height = Math.Min(950, SystemParameters.WorkArea.Height - 30); MinWidth = 1100; MinHeight = 730;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brush("#F6F6F1"); FontFamily = new FontFamily("Microsoft YaHei UI");
        var root = new Grid { Margin = new Thickness(24, 16, 24, 14) };
        foreach (var h in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto }) root.RowDefinitions.Add(new RowDefinition { Height = h });
        Content = root;
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var export = Button("导出 CSV"); export.Margin = new Thickness(8, 0, 0, 0); export.Click += delegate { Export(); };
        refresh.Background = Brush("#539169"); refresh.Foreground = Brushes.White; refresh.BorderThickness = new Thickness(0);
        refresh.Click += async delegate { await Reload(); };
        actions.Children.Add(refresh); actions.Children.Add(export); DockPanel.SetDock(actions, Dock.Right); header.Children.Add(actions);
        var heading = new StackPanel(); heading.Children.Add(Text("任务 Token 统计", 26, "#23262A"));
        quota.FontSize = 11; quota.Foreground = Brush("#5C6F58");
        heading.Children.Add(new Border { Background = Brush("#EAF0E4"), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Child = quota }); header.Children.Add(heading); root.Children.Add(header);

        var cards = new Grid { Margin = new Thickness(0, 0, 0, 12) }; Grid.SetRow(cards, 1);
        string[] labels = { "总 TOKEN", "输入 TOKEN", "输出 TOKEN", "缓存输入占比" };
        string[] hints = { "输入 + 输出", "包含缓存输入", "包含推理输出", "命中缓存的输入 / 全部输入" };
        TextBlock[] values = { totalValue, inputValue, outputValue, cacheValue };
        for (int i = 0; i < 4; i++)
        {
            cards.ColumnDefinitions.Add(new ColumnDefinition());
            var stack = new StackPanel(); var label = Text(labels[i], 11, "#697468"); stack.Children.Add(label); values[i].Margin = new Thickness(0, 7, 0, 4); values[i].FontFamily = new FontFamily("Segoe UI"); values[i].FontWeight = FontWeights.SemiBold; stack.Children.Add(values[i]); var hint = Text(hints[i], 11, "#828A7C"); stack.Children.Add(hint);
            var card = Card(stack); card.Margin = new Thickness(i == 0 ? 0 : 6, 0, i == 3 ? 0 : 6, 0); Grid.SetColumn(card, i); cards.Children.Add(card);
            if (i == 0) { card.Background = Brush("#539169"); card.BorderThickness = new Thickness(0); values[i].Foreground = Brushes.White; label.Foreground = Brush("#EAF3E9"); hint.Foreground = Brush("#E0EDDF"); }
            if (i == 3) { card.Background = Brush("#EDF3E8"); card.BorderBrush = Brush("#DFE8D7"); }
        }
        root.Children.Add(cards);
        var trend = new StackPanel(); trend.Children.Add(Text("每日用量", 14, "#23262A")); trend.Children.Add(chart);
        var trendCard = Card(trend); trendCard.Margin = new Thickness(0, 0, 0, 10); Grid.SetRow(trendCard, 2); root.Children.Add(trendCard);

        var filters = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) }; Grid.SetRow(filters, 3);
        foreach (string label in new[] { "全部时间", "今天", "最近 7 天", "最近 30 天", "自定义" }) period.Items.Add(label);
        period.SelectedIndex = 0;
        foreach (string label in new[] { "消耗从高到低", "最近活动" }) sort.Items.Add(label);
        sort.SelectedIndex = 1;
        search.ToolTip = "搜索任务名称、目录或 ID";
        AddFilter(filters, Text("搜索", 12, "#5C635C")); AddFilter(filters, search); AddFilter(filters, period);
        AddFilter(filters, from); AddFilter(filters, Text("至", 12, "#697468")); AddFilter(filters, to); AddFilter(filters, sort); AddFilter(filters, children);
        from.SelectedDate = TaskUsageStore.Now.Date.AddDays(-6); to.SelectedDate = TaskUsageStore.Now.Date; from.IsEnabled = to.IsEnabled = false;
        var filterCard = new Border { Background = Brush("#EEF1E8"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 8, 3, 3), Margin = new Thickness(0, 0, 0, 10), Child = filters }; filters.Margin = new Thickness(0); Grid.SetRow(filterCard, 3); root.Children.Add(filterCard);

        var panel = new Grid(); panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); panel.RowDefinitions.Add(new RowDefinition()); panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        taskCount.Margin = new Thickness(0, 0, 0, 10); panel.Children.Add(taskCount);
        AddColumn("任务名称", "Title", new DataGridLength(1, DataGridLengthUnitType.Star)); AddColumn("类型", "Kind", new DataGridLength(65));
        AddColumn("总 Token", "TotalText", new DataGridLength(115)); AddColumn("输入", "InputText", new DataGridLength(110)); AddColumn("输出", "OutputText", new DataGridLength(95)); AddColumn("缓存占比", "Cache", new DataGridLength(85)); AddColumn("最近活动", "Updated", new DataGridLength(106));
        var barTemplate = new DataTemplate(); var bar = new FrameworkElementFactory(typeof(ProgressBar));
        bar.SetValue(ProgressBar.MaximumProperty, 100.0); bar.SetValue(FrameworkElement.HeightProperty, 5.0); bar.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 8, 0)); bar.SetResourceReference(Control.ForegroundProperty, PetUsagePalette.Key("#539169")); bar.SetResourceReference(Control.BackgroundProperty, PetUsagePalette.Key("#E8EFE3")); bar.SetBinding(ProgressBar.ValueProperty, new Binding("Weight")); barTemplate.VisualTree = bar;
        table.Columns.Add(new DataGridTemplateColumn { Header = "相对消耗", CellTemplate = barTemplate, Width = 95, CanUserSort = false });
        table.SelectionChanged += delegate { UpdateDetail(); };
        Grid.SetRow(table, 1); panel.Children.Add(table);
        detail.TextWrapping = TextWrapping.Wrap; detail.Margin = new Thickness(0, 8, 0, 0); detail.MinHeight = 36; Grid.SetRow(detail, 2); panel.Children.Add(detail);
        var tableCard = Card(panel); Grid.SetRow(tableCard, 4); root.Children.Add(tableCard);

        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) }; Grid.SetRow(footer, 5);
        status.TextWrapping = TextWrapping.Wrap; footer.Children.Add(status);
        footer.Children.Add(Text("北京时间 · 缓存输入计入输入，推理输出计入输出；子代理单独列出。Token 数不等于套餐额度。仅统计本机留存记录。", 11, "#828A7C")); root.Children.Add(footer);
        search.TextChanged += delegate { Apply(); }; period.SelectionChanged += delegate { from.IsEnabled = to.IsEnabled = period.SelectedIndex == 4; Apply(); };
        sort.SelectionChanged += delegate { Apply(); }; from.SelectedDateChanged += delegate { Apply(); }; to.SelectedDateChanged += delegate { Apply(); };
        children.Checked += delegate { Apply(); }; children.Unchecked += delegate { Apply(); };
        Loaded += async delegate { ApplyPetTheme(); await Reload(); timer.Start(); };
        timer.Tick += async delegate { await Reload(); };
        Closed += delegate { closed = true; timer.Stop(); };
        ready = true;
    }

    private static SolidColorBrush Brush(string value) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); }
    private static TextBlock Text(string value, double size, string color) { return new TextBlock { Text = value, FontSize = size, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center }; }
    private static Border Card(UIElement child) { return new Border { Background = Brushes.White, BorderBrush = Brush("#E3E7DC"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 10, 16, 10), Child = child }; }
    private static Button Button(string text) { return new Button { Content = text, Padding = new Thickness(15, 8, 15, 8), Background = Brushes.White, Foreground = Brush("#355E43"), BorderBrush = Brush("#DCE3DA"), Cursor = System.Windows.Input.Cursors.Hand }; }
    private static void AddFilter(Panel panel, FrameworkElement child) { child.Margin = new Thickness(0, 0, 9, 5); child.VerticalAlignment = VerticalAlignment.Center; panel.Children.Add(child); }
    private void AddColumn(string title, string path, DataGridLength width, string format = null) { table.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(path) { StringFormat = format }, SortMemberPath = path.EndsWith("Text") ? path.Substring(0, path.Length - 4) : path == "Cache" ? "CacheRate" : path == "Updated" ? "Activity" : path, Width = width, ElementStyle = new Style(typeof(TextBlock)) { Setters = { new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center), new Setter(FrameworkElement.MarginProperty, new Thickness(6, 0, 4, 0)), new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis), new Setter(FrameworkElement.ToolTipProperty, new Binding(path)) } } }); }
    private DateTime? Start { get { if (period.SelectedIndex == 0) return null; if (period.SelectedIndex == 4) return from.SelectedDate; return TaskUsageStore.Now.Date.AddDays(period.SelectedIndex == 1 ? 0 : period.SelectedIndex == 2 ? -6 : -29); } }
    private DateTime End { get { return period.SelectedIndex == 4 ? (to.SelectedDate ?? TaskUsageStore.Now.Date).Date : TaskUsageStore.Now.Date; } }

    internal void SetPetTheme(string accent, string name) {
        petAccent = accent; petName = name;
        Title = "任务 Token 统计 · " + petName;
        ApplyPetTheme();
    }
    private void ApplyPetTheme() { PetUsagePalette.Apply(this, petAccent); }

    internal void UpdateQuota() { quota.Text = quotaText(); }

    internal async Task Reload()
    {
        if (busy || closed) return;
        busy = true; refresh.IsEnabled = false; status.Text = "正在扫描本地会话记录…";
        try { var result = await load(); if (closed) return; data = result; Apply(); }
        catch (Exception e) { status.Text = "读取失败：" + e.Message; CrossPet.Store.Log("任务用量读取失败: " + e.Message); }
        finally { busy = false; refresh.IsEnabled = true; }
    }

    private List<TaskUsageRow> rows = new List<TaskUsageRow>();
    private void Apply()
    {
        if (!ready || data == null) return;
        DateTime? start = Start; DateTime end = End;
        if (start.HasValue && start.Value > end) { status.Text = "开始日期不能晚于结束日期。"; return; }
        string query = search.Text.Trim();
        var eligible = data.Tasks.Where(t => (children.IsChecked == true || !t.Subagent) && (query.Length == 0 || (t.Title + " " + t.Directory + " " + t.Id).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
        rows = eligible.Select(t => new TaskUsageRow { Task = t, Counts = SelectCounts(t, start, end) }).Where(r => !start.HasValue || r.Total > 0).ToList();
        rows = sort.SelectedIndex == 1 ? rows.OrderByDescending(r => r.Task.LastActivity).ToList() : rows.OrderByDescending(r => r.Total).ToList();
        long max = Math.Max(1, rows.Select(r => r.Total).DefaultIfEmpty(0).Max());
        foreach (var r in rows) r.Weight = 100.0 * r.Total / max;
        string selected = (table.SelectedItem as TaskUsageRow)?.Task.Id;
        table.ItemsSource = rows; if (selected != null) table.SelectedItem = rows.FirstOrDefault(r => r.Task.Id == selected);
        var counts = new TokenCounts(); foreach (var r in rows) counts.Add(r.Counts);
        totalValue.Text = Compact(counts.Total); inputValue.Text = Compact(counts.Input); outputValue.Text = Compact(counts.Output); cacheValue.Text = counts.Input == 0 ? "—" : (100.0 * counts.Cached / counts.Input).ToString("F1") + "%";
        totalValue.ToolTip = counts.Total.ToString("N0"); inputValue.ToolTip = counts.Input.ToString("N0"); outputValue.ToolTip = counts.Output.ToString("N0");
        taskCount.Text = rows.Count + " 个任务 · " + (start.HasValue ? start.Value.ToString("yyyy-MM-dd") + " 至 " + end.ToString("yyyy-MM-dd") : "累计用量") + " · 点击列标题可排序";
        quota.Text = quotaText();
        DrawChart(eligible, start, end);
        int unknown = eligible.Count(t => !t.HasUsage);
        status.Text = data.MissingSessions ? "未找到 sessions 目录：" + data.Root : "已更新 " + TaskUsageStore.Now.ToString("HH:mm:ss") + " · 每 30 秒刷新 · " + data.Tasks.Count + " 份本地任务记录";
        if (unknown > 0) status.Text += " · " + unknown + " 个任务缺少用量记录（—）";
        if (data.FailedFiles + data.MalformedLines > 0) status.Text += " · 跳过 " + data.FailedFiles + " 个不可读文件 / " + data.MalformedLines + " 条不完整记录";
        if (data.Regressions > 0) status.Text += " · 检测到累计值回退，按最高累计值统计";
        if (children.IsChecked != true && data.Tasks.Any(t => t.Subagent)) status.Text += " · 子代理未计入";
        UpdateDetail();
        ApplyPetTheme();
    }

    internal static TokenCounts SelectCounts(TaskUsage t, DateTime? start, DateTime end) { if (!start.HasValue) return t.Counts; var c = new TokenCounts(); foreach (var d in t.Days) if (d.Key >= start.Value.Date && d.Key <= end) c.Add(d.Value); return c; }
    internal static string Compact(long n) { return n >= 1000000000 ? (n / 1000000000.0).ToString("0.##") + "B" : n >= 1000000 ? (n / 1000000.0).ToString("0.##") + "M" : n >= 1000 ? (n / 1000.0).ToString("0.##") + "K" : n.ToString("N0"); }
    private void DrawChart(List<TaskUsage> tasks, DateTime? start, DateTime end)
    {
        chart.Children.Clear(); chart.ColumnDefinitions.Clear();
        DateTime first = start ?? end.AddDays(-13); int count = (end - first).Days + 1;
        // Aggregate a long selected range into at most 14 continuous buckets.
        int bucket = Math.Max(1, (int)Math.Ceiling(count / 14.0));
        var bars = new List<Tuple<DateTime, DateTime, long>>();
        for (DateTime d = first; d <= end; d = d.AddDays(bucket))
        { DateTime stop = d.AddDays(bucket - 1); if (stop > end) stop = end; long n = tasks.Sum(t => t.Days.Where(p => p.Key >= d && p.Key <= stop).Sum(p => p.Value.Total)); bars.Add(Tuple.Create(d, stop, n)); }
        long max = Math.Max(1, bars.Select(b => b.Item3).DefaultIfEmpty(0).Max());
        for (int i = 0; i < bars.Count; i++)
        {
            chart.ColumnDefinitions.Add(new ColumnDefinition()); var value = bars[i];
            var stack = new Grid { Margin = new Thickness(4, 0, 4, 0), ToolTip = value.Item1.ToString("yyyy-MM-dd") + (bucket > 1 ? " ~ " + value.Item2.ToString("MM-dd") : "") + "\n" + value.Item3.ToString("N0") + " token" };
            stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(21) }); stack.RowDefinitions.Add(new RowDefinition()); stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
            var label = Text(Compact(value.Item3), 10, "#5C635C"); label.HorizontalAlignment = HorizontalAlignment.Center; stack.Children.Add(label);
            var bar = new Border { Background = Brush("#539169"), CornerRadius = new CornerRadius(3, 3, 0, 0), Height = Math.Max(2, 36.0 * value.Item3 / max), VerticalAlignment = VerticalAlignment.Bottom, MaxWidth = 54 }; Grid.SetRow(bar, 1); stack.Children.Add(bar);
            var date = Text(value.Item1.ToString("MM/dd"), 10, "#828A7C"); date.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetRow(date, 2); stack.Children.Add(date); Grid.SetColumn(stack, i); chart.Children.Add(stack);
        }
    }

    private void UpdateDetail()
    {
        var r = table.SelectedItem as TaskUsageRow;
        if (r == null) { detail.Text = rows.Count == 0 ? "没有符合条件的任务。" : "选择任务查看缓存、推理输出与目录。日期筛选按累计值增量归属到记录当天。"; return; }
        detail.Text = r.Task.Title + "\n" + (r.Task.HasUsage ? "缓存输入 " + r.Counts.Cached.ToString("N0") + " · 非缓存输入 " + Math.Max(0, r.Counts.Input - r.Counts.Cached).ToString("N0") + " · 推理输出 " + r.Counts.Reasoning.ToString("N0") : "此任务没有用量记录，不能视为零消耗") + "\n" + r.Task.Directory + "  |  " + r.Task.Id + (r.Task.Model.Length > 0 ? "  |  " + r.Task.Model : "");
    }

    private void Export()
    {
        var dialog = new SaveFileDialog { Filter = "CSV 表格|*.csv", FileName = "Codex任务Token-" + TaskUsageStore.Now.ToString("yyyyMMdd-HHmm") + ".csv" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var text = new StringBuilder("任务名称,类型,总Token,输入Token,缓存输入Token,输出Token,推理输出Token,用量记录,任务ID,目录\r\n");
            foreach (var r in rows) text.AppendLine(string.Join(",", new[] { Csv(r.Title), Csv(r.Kind), r.Task.HasUsage ? r.Total.ToString() : "", r.Task.HasUsage ? r.Input.ToString() : "", r.Task.HasUsage ? r.Counts.Cached.ToString() : "", r.Task.HasUsage ? r.Output.ToString() : "", r.Task.HasUsage ? r.Counts.Reasoning.ToString() : "", r.Task.HasUsage ? "有" : "缺失", Csv(r.Task.Id), Csv(r.Task.Directory) }));
            File.WriteAllText(dialog.FileName, text.ToString(), new UTF8Encoding(true)); status.Text = "已导出 " + rows.Count + " 个任务：" + dialog.FileName;
        }
        catch (Exception e) { MessageBox.Show(this, "导出失败：" + e.Message, Title); }
    }
    private static string Csv(string value) { value = value ?? ""; if (value.TrimStart().StartsWith("=") || value.TrimStart().StartsWith("+") || value.TrimStart().StartsWith("-") || value.TrimStart().StartsWith("@")) value = "'" + value; return "\"" + value.Replace("\"", "\"\"") + "\""; }

    internal void SavePreview(string path)
    {
        UpdateLayout(); var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(this);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(path)) png.Save(file);
    }
}
