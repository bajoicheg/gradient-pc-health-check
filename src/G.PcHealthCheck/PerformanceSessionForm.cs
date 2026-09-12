using System.Text;

namespace G.PcHealthCheck;

internal sealed class PerformanceSessionForm : Form
{
    // The gate stays held until the actual provider returns, including after closing a window.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly ComboBox _duration = Choice([30, 60, 120, 300, 600], 120);
    private readonly ComboBox _interval = Choice([1, 2, 5], 2);
    private readonly ComboBox _metric = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 235 };
    private readonly Button _start = MakeButton("Начать наблюдение", "SessionStart");
    private readonly Button _stop = MakeButton("Остановить и сохранить замеры", "SessionStop");
    private readonly Button _mark = MakeButton("Отметить симптом", "SessionMark");
    private readonly Button _copy = MakeButton("Копировать сводку", "SessionCopy");
    private readonly Button _export = MakeButton("Сохранить HTML / JSON", "SessionExport");
    private readonly TextBox _note = new() { Text = "Зависание / задержка", MaxLength = 160, Width = 260 };
    private readonly Label _state = new() { AutoSize = true, Dock = DockStyle.Fill, Text = "Готов к запуску. Сеанс не начат." };
    private readonly Label _stats = new() { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 6) };
    private readonly ProgressBar _progress = new() { Width = 120, Style = ProgressBarStyle.Marquee, Visible = false };
    private readonly DataGridView _samples = Grid();
    private readonly ListBox _markers = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true };
    private readonly TextBox _detail = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly PerformanceTimeline _chart = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private PerformanceSessionSnapshot? _current;
    private PerformanceSessionSnapshot? _live;
    private readonly List<PerformanceMarker> _markerData = [];
    private MonotonicPerformanceClock? _clock;
    private CancellationTokenSource? _cancellation;
    private bool _busy;

    public PerformanceSessionForm()
    {
        Text = "G PC Health Check — сеанс производительности";
        Size = new Size(1220, 850); MinimumSize = new Size(980, 680);
        StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(12) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);
        root.Controls.Add(new Label { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 8), Text = "Начните наблюдение, воспроизведите проблему и отметьте симптом. Нагрузка искусственно не создаётся.\nГрафик использует время получения замеров; пробелы — отсутствие данных. Совпадение с отметкой не устанавливает причину сбоя." }, 0, 0);
        var toolbar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        toolbar.Controls.AddRange([Caption("Длительность, с"), _duration, Caption("Интервал, с"), _interval, _start, _stop, _progress]);
        toolbar.SetFlowBreak(_progress, true);
        toolbar.Controls.AddRange([_metric, _note, _mark, _copy, _export]);
        root.Controls.Add(toolbar, 0, 1); root.Controls.Add(_stats, 0, 2); root.Controls.Add(_chart, 0, 3);
        foreach (var metric in Enum.GetValues<SessionMetric>()) _metric.Items.Add(PerformanceSessionReport.Name(metric));
        _metric.SelectedIndex = 0;
        AddColumn("Offset", "От начала, с", typeof(double), 95, "0.000");
        AddColumn("CPU", "CPU, %", typeof(double), 85);
        AddColumn("RAM", "RAM, %", typeof(double), 85);
        AddColumn("Busy", "Диски, %", typeof(double), 90);
        AddColumn("Queue", "Очередь", typeof(double), 85);
        AddColumn("Duration", "Сбор, мс", typeof(long), 90, "0");
        _samples.Columns.Add(new DataGridViewTextBoxColumn { Name = "Warning", HeaderText = "Предупреждения", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _samples.SelectionChanged += (_, _) => ShowDetail();
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var samplesPage = new TabPage("Измерения"); samplesPage.Controls.Add(_samples); tabs.TabPages.Add(samplesPage);
        var markersPage = new TabPage("Отметки симптома"); markersPage.Controls.Add(_markers); tabs.TabPages.Add(markersPage);
        var detailsPage = new TabPage("Подробности и методика"); detailsPage.Controls.Add(_detail); tabs.TabPages.Add(detailsPage);
        root.Controls.Add(tabs, 0, 4); root.Controls.Add(_state, 0, 5);
        _start.Click += async (_, _) => await StartAsync();
        _stop.Click += (_, _) => { _cancellation?.Cancel(); _stop.Enabled = _mark.Enabled = false; _state.Text = "Остановка запрошена; ожидается завершение текущего системного вызова."; };
        _mark.Click += (_, _) => Mark();
        _metric.SelectedIndexChanged += (_, _) => RefreshChart();
        _copy.Click += (_, _) => { if (_current is { } s) TryUi(() => Clipboard.SetText(PerformanceSessionReport.Summary(s))); };
        _export.Click += (_, _) => Export();
        _timer.Tick += (_, _) =>
        {
            if (_clock is not { } clock || _live is not { } live) return;
            live.ElapsedMs = clock.ElapsedMs;
            if (_cancellation?.IsCancellationRequested != true)
                _state.Text = $"Наблюдение: {clock.ElapsedMs / 1000d:0.0} / {live.Options.DurationSeconds} с; замеров: {live.Samples.Count}; отметок: {_markerData.Count}.";
            RefreshChart();
        };
        FormClosing += (_, _) => _cancellation?.Cancel();
        _detail.Text = PerformanceSessionReport.Boundary;
        UpdateButtons();
    }

    private async Task StartAsync()
    {
        if (_busy || IsDisposed) return;
        if (!Gate.Wait(0)) { _state.Text = "Предыдущий сеанс ещё завершает системный вызов. Дождитесь его окончания."; return; }
        var options = new PerformanceSessionOptions((int)_duration.SelectedItem!, (int)_interval.SelectedItem!);
        var cancellation = new CancellationTokenSource();
        var clock = new MonotonicPerformanceClock();
        _cancellation = cancellation; _clock = clock; _busy = true;
        var live = new PerformanceSessionSnapshot { Options = options, StartedAt = clock.Now, Outcome = "Running" };
        _live = live; _current = null; _markerData.Clear(); _markers.Items.Clear(); _samples.Rows.Clear();
        _stats.Text = "Ожидаю первый замер…"; _timer.Start(); UpdateButtons();
        try
        {
            var progress = new Progress<PerformanceSample>(sample =>
            {
                if (IsDisposed || !ReferenceEquals(_live, live) || !_busy) return;
                live.Samples.Add(sample); live.ElapsedMs = clock.ElapsedMs;
                AddSample(sample); UpdateStatistics(live); RefreshChart();
            });
            var result = await Task.Run(async () =>
            {
                using var source = new WindowsPerformanceSessionSource();
                return await new PerformanceSessionService().RunAsync(options, source, clock, progress, cancellation.Token);
            });
            if (IsDisposed) return;
            result.Markers = _markerData.ToList(); _current = result; _live = null;
            _samples.Rows.Clear(); foreach (var sample in result.Samples) AddSample(sample);
            UpdateStatistics(result); RefreshChart();
            _state.Text = $"{PerformanceSessionReport.Outcome(result.Outcome)}: {result.ElapsedMs / 1000d:0.0} с; замеров {result.Samples.Count}; пропущено интервалов {result.MissedSlots}. {PerformanceStatistics.Completeness(result)}.";
            ShowDetail();
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            live.Outcome = cancellation.IsCancellationRequested ? "Stopped" : "Failed";
            live.ElapsedMs = clock.ElapsedMs; live.FinishedAt = clock.Now; live.Markers = _markerData.ToList();
            live.Warnings.Add($"Не удалось завершить сеанс: {ex.GetType().Name}, 0x{ex.HResult:X8}.");
            _current = live; _live = null; _state.Text = "Сеанс прерван; доступные данные можно сохранить.";
            UpdateStatistics(live); RefreshChart(); ShowDetail();
        }
        finally
        {
            Gate.Release(); cancellation.Dispose(); _cancellation = null; _clock = null; _busy = false;
            if (!IsDisposed) { _timer.Stop(); UpdateButtons(); }
        }
    }

    private void Mark()
    {
        if (!_busy || _clock is null || _cancellation?.IsCancellationRequested == true) return;
        if (!PerformanceStatistics.AddMarker(_markerData, _clock.ElapsedMs, _note.Text))
        { _state.Text = "Нужна непустая заметка до 160 символов. Максимум 100 отметок на сеанс."; return; }
        var marker = _markerData[^1]; _markers.Items.Add($"+{marker.OffsetMs / 1000d:0.000} с — {marker.Note}");
        if (_live is not null) _live.Markers = _markerData.ToList();
        RefreshChart();
    }

    private void AddSample(PerformanceSample s)
    {
        var i = _samples.Rows.Add(s.OffsetMs / 1000d, s.Reading.CpuPercent, s.Reading.MemoryUsedPercent, s.Reading.DiskBusyPercent,
            s.Reading.DiskQueueLength, s.CollectionMs, string.Join("; ", s.Reading.Warnings));
        _samples.Rows[i].Tag = s;
    }

    private void UpdateStatistics(PerformanceSessionSnapshot snapshot)
    {
        _stats.Text = string.Join(Environment.NewLine, Enum.GetValues<SessionMetric>().Select(metric =>
        {
            var s = PerformanceStatistics.For(snapshot, metric);
            return $"{PerformanceSessionReport.Name(metric)}: медиана {PerformanceSessionReport.F(s.Median)} · P95 {PerformanceSessionReport.F(s.P95)} · max {PerformanceSessionReport.F(s.Maximum)} · доступно {s.Valid}/{s.Total} замеров";
        }));
    }

    private void RefreshChart()
    {
        if ((_live ?? _current) is { } snapshot && _metric.SelectedIndex >= 0) _chart.Display(snapshot, (SessionMetric)_metric.SelectedIndex);
    }
    private void ShowDetail()
    {
        var snapshot = _live ?? _current;
        var sample = _samples.CurrentRow?.Tag as PerformanceSample;
        _detail.Text = (sample is null ? "" : $"Получено: {sample.CollectedAt:O}; от начала {sample.OffsetMs} мс; сбор {sample.CollectionMs} мс.\r\n{string.Join("\r\n", sample.Reading.Warnings)}\r\n\r\n")
            + (snapshot is null ? "" : string.Join("\r\n", snapshot.Warnings) + "\r\n\r\n") + PerformanceSessionReport.Boundary;
    }
    private void UpdateButtons()
    {
        _start.Enabled = _duration.Enabled = _interval.Enabled = !_busy;
        _stop.Enabled = _mark.Enabled = _note.Enabled = _busy && _cancellation?.IsCancellationRequested != true;
        _copy.Enabled = _export.Enabled = !_busy && _current is not null;
        _progress.Visible = _busy;
    }
    private void Export()
    {
        if (_busy || _current is not { } snapshot) return;
        using var dialog = new FolderBrowserDialog { Description = "Сохранить весь сеанс. Заметки могут содержать чувствительные сведения.", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) TryUi(() => _state.Text = "Сохранено: " + PerformanceSessionExport.Save(snapshot, dialog.SelectedPath));
    }
    private void TryUi(Action action) { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Действие не завершено", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
    private void AddColumn(string name, string title, Type type, int width, string format = "0.##")
        => _samples.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = title, Width = width, ValueType = type, SortMode = DataGridViewColumnSortMode.Automatic, DefaultCellStyle = new DataGridViewCellStyle { Format = format, NullValue = "—" } });
    private static DataGridView Grid() => new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, BackgroundColor = Color.White };
    private static ComboBox Choice(int[] values, int selected) { var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 75 }; foreach (var value in values) box.Items.Add(value); box.SelectedItem = selected; return box; }
    private static Button MakeButton(string text, string name) => new() { Text = text, Name = name, AutoSize = true, Padding = new Padding(4, 2, 4, 2) };
    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(4, 7, 4, 3) };
    protected override void Dispose(bool disposing) { if (disposing) { _timer.Dispose(); _cancellation?.Cancel(); } base.Dispose(disposing); }
}

internal static class PerformanceSessionMenu
{
    public static void Attach(Form main)
    {
        ArgumentNullException.ThrowIfNull(main);
        IncidentReviewMenu.Attach(main);
        var menu = main.MainMenuStrip!;
        if (menu.Items.Find("PerformanceSession", true).Length > 0) return;
        var group = (ToolStripMenuItem)menu.Items.Find("ReadOnlyInspections", false).Single();
        var item = new ToolStripMenuItem("Сеанс производительности…") { Name = "PerformanceSession" };
        item.Click += (_, _) => { using var form = new PerformanceSessionForm(); form.ShowDialog(main); };
        group.DropDownItems.Add(item);
    }
}

internal static class PerformanceSessionExport
{
    public static string Save(PerformanceSessionSnapshot snapshot, string parent)
    {
        var json = PerformanceSessionReport.Json(snapshot); var html = PerformanceSessionReport.Html(snapshot);
        var directory = Path.Combine(parent, $"G-PC-Performance_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        Write(Path.Combine(directory, "session.json"), json); Write(Path.Combine(directory, "report.html"), html);
        return directory;
    }
    private static void Write(string path, string text)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false, true)); writer.Write(text);
    }
}
