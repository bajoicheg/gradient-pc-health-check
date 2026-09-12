using System.Diagnostics;
using System.Text;

namespace G.PcHealthCheck;

internal sealed class IncidentReviewForm : Form
{
    private readonly bool _eventsMode;
    private readonly IncidentEvents _events = new(new WindowsIncidentEventSource());
    private readonly ProcessReview _processes = new(new WindowsProcessReviewSource());
    private readonly DataGridView _grid = new() { Name = "IncidentGrid", Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoGenerateColumns = false };
    private readonly TextBox _detail = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, MaxLength = 0 };
    private readonly TextBox _search = new() { Name = "IncidentSearch", Width = 280, MaxLength = 256, PlaceholderText = "Поиск в полученном снимке" };
    private readonly DateTimePicker _from = DatePicker();
    private readonly DateTimePicker _to = DatePicker();
    private readonly NumericUpDown _id = new() { Minimum = -1, Maximum = 65535, Value = -1, Width = 80 };
    private readonly ComboBox _log = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 135 };
    private readonly CheckBox _warningsOnly = new() { Text = "Только Critical / Error / Warning", AutoSize = true };
    private readonly Button _run = Button("Собрать / повторить", "IncidentRun");
    private readonly Button _cancel = Button("Отменить", "IncidentCancel");
    private readonly Button _owner = Button("Проверить владельца", "IncidentOwner");
    private readonly Button _copy = Button("Копировать сводку", "IncidentCopy");
    private readonly Button _export = Button("Сохранить HTML / JSON", "IncidentExport");
    private readonly Label _snapshot = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly Label _status = new() { AutoSize = true };
    private readonly ProgressBar _progress = new() { Style = ProgressBarStyle.Marquee, Width = 100, Visible = false };
    private readonly Stopwatch _watch = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private object? _current;
    private CancellationTokenSource? _cancellation;
    private bool _busy;

    public IncidentReviewForm(bool eventsMode)
    {
        _eventsMode = eventsMode; Text = eventsMode ? "G PC Health Check — события за время сбоя" : "G PC Health Check — подробности процессов";
        Size = new Size(1240, 820); MinimumSize = new Size(900, 650); AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F); StartPosition = FormStartPosition.CenterParent;
        _to.Value = DateTime.Now; _from.Value = _to.Value.AddHours(-1);
        _log.Items.AddRange(["Все журналы", "Application", "System"]); _log.SelectedIndex = 0;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(12) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 5; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 45)); Controls.Add(root);
        root.Controls.Add(new Label { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 8), Text = eventsMode
            ? "Локальные Application и System. Укажите интервал в местном времени Windows (до 7 суток).\nСбор только по кнопке; максимум 1000 записей на журнал. Поиск и фильтры работают по уже полученным данным."
            : "Текущий снимок процессов: PID, время запуска, путь, команда и память. Владелец запрашивается отдельно для выбранной строки.\nНичего не запускается и не завершается. Недоступные сведения отмечаются; отсутствие пути не означает вредоносность." }, 0, 0);
        var dates = Flow(); dates.Visible = eventsMode; dates.Controls.AddRange([Label("С:"), _from, Label("По:"), _to]);
        var recent = Button("Последний час", "IncidentRecent"); recent.Click += (_, _) => { _to.Value = DateTime.Now; _from.Value = _to.Value.AddHours(-1); }; dates.Controls.Add(recent); root.Controls.Add(dates, 0, 1);
        var filters = Flow(); filters.Controls.Add(_search);
        if (eventsMode) filters.Controls.AddRange([_log, Label("Event ID (-1 = все):"), _id, _warningsOnly]);
        root.Controls.Add(filters, 0, 2);
        var tools = Flow(); tools.Controls.AddRange([_run, _cancel, _copy, _export]); if (!eventsMode) tools.Controls.Add(_owner);
        tools.Controls.AddRange([_progress, _status]); root.Controls.Add(tools, 0, 3);
        _snapshot.Text = "Снимок ещё не собран."; root.Controls.Add(_snapshot, 0, 4); root.Controls.Add(_grid, 0, 5); root.Controls.Add(_detail, 0, 6);
        ConfigureColumns();
        _search.TextChanged += (_, _) => Render(); _log.SelectedIndexChanged += (_, _) => Render(); _id.ValueChanged += (_, _) => Render(); _warningsOnly.CheckedChanged += (_, _) => Render();
        _from.ValueChanged += (_, _) => IntervalChanged(); _to.ValueChanged += (_, _) => IntervalChanged();
        _grid.SelectionChanged += (_, _) => ShowDetail();
        _run.Click += async (_, _) => await CollectAsync(); _owner.Click += async (_, _) => await OwnerAsync();
        _cancel.Click += (_, _) => { _cancellation?.Cancel(); _cancel.Enabled = false; _status.Text = "Отмена запрошена; ожидается возврат поставщика данных."; };
        _copy.Click += (_, _) => { if (_current is { } snapshot) TryUi(() => Clipboard.SetText(IncidentReport.Summary(snapshot, Filter()))); };
        _export.Click += (_, _) => Export();
        _timer.Tick += (_, _) => _status.Text = (_cancellation?.IsCancellationRequested == true ? "Отмена запрошена… " : "Ожидание / сбор… ") + $"{_watch.Elapsed.TotalSeconds:0.0} с";
        FormClosing += (_, _) => _cancellation?.Cancel(); FormClosed += (_, _) => _timer.Dispose(); UpdateButtons();
    }
    private void ConfigureColumns()
    {
        void Add(string name, string caption, Type type, int width, string? format = null) => _grid.Columns.Add(new DataGridViewTextBoxColumn
        { Name = name, HeaderText = caption, ValueType = type, Width = width, SortMode = DataGridViewColumnSortMode.Automatic, DefaultCellStyle = new DataGridViewCellStyle { NullValue = "—", Format = format ?? "" } });
        if (_eventsMode)
        {
            Add("Timestamp", "Время (местное)", typeof(DateTime), 165, "dd.MM.yyyy HH:mm:ss"); Add("Log", "Журнал", typeof(string), 100);
            Add("EventId", "Event ID", typeof(int), 75); Add("Level", "Уровень", typeof(string), 135);
            Add("Provider", "Источник", typeof(string), 270); Add("EmitterPid", "PID издателя", typeof(int), 95); Add("RecordId", "Record ID", typeof(long), 100);
        }
        else
        {
            Add("Pid", "PID", typeof(uint), 75); Add("Name", "Процесс", typeof(string), 185);
            Add("CreatedAt", "Начало (местное)", typeof(DateTime), 165, "dd.MM.yyyy HH:mm:ss");
            Add("MemoryMiB", "RAM, MiB", typeof(double), 90, "N1"); Add("ParentPid", "PPID", typeof(uint), 75);
            Add("Owner", "Владелец / состояние", typeof(string), 210); Add("Executable", "Путь", typeof(string), 300);
        }
    }
    private object Filter() => _eventsMode ? new IncidentFilter(_search.Text, _log.SelectedIndex <= 0 ? "" : _log.Text, _id.Value < 0 ? null : (int)_id.Value, _warningsOnly.Checked) : _search.Text;
    private IncidentWindow Window()
    {
        static DateTimeOffset Local(DateTime value)
        {
            var time = DateTime.SpecifyKind(value, DateTimeKind.Unspecified); var zone = TimeZoneInfo.Local;
            if (zone.IsInvalidTime(time) || zone.IsAmbiguousTime(time)) throw new ArgumentException("Выбрано неоднозначное время перехода часового пояса; уточните границы интервала.");
            return new DateTimeOffset(time, zone.GetUtcOffset(time));
        }
        var result = new IncidentWindow(Local(_from.Value), Local(_to.Value)); IncidentQueries.Validate(result); return result;
    }
    private void IntervalChanged()
    {
        if (_current is IncidentSnapshot s) _snapshot.Text = $"Границы изменены. Показан прежний снимок {s.StartedAt:HH:mm:ss}, интервал {s.Window.From:O} — {s.Window.To:O}. Для нового сбора нажмите кнопку.";
    }
    private async Task CollectAsync()
    {
        if (_busy) return; IncidentWindow? window = null;
        try { if (_eventsMode) window = Window(); } catch (ArgumentException ex) { _status.Text = ex.Message; return; }
        var started = DateTimeOffset.Now; var cancellation = Begin(); _grid.Rows.Clear(); _detail.Clear(); _snapshot.Text = "Выполняется новый сбор…";
        try
        {
            var next = await IncidentOperations.RunAsync<object>(ct => _eventsMode ? _events.Collect(window!, ct) : _processes.Collect(1024, ct), cancellation.Token);
            if (!IsDisposed) _current = next;
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                var state = cancellation.IsCancellationRequested ? "Cancelled" : "Unavailable";
                _current = _eventsMode ? new IncidentSnapshot { Window = window!, StartedAt = started, FinishedAt = DateTimeOffset.Now, State = state,
                    Logs = [new IncidentLogResult { Log = "Application/System", State = state, Warnings = [IncidentQueries.Error(ex)] }] }
                    : new ProcessReviewSnapshot { StartedAt = started, FinishedAt = DateTimeOffset.Now, State = state, Warnings = [IncidentQueries.Error(ex)] };
            }
        }
        finally { End(cancellation); }
        if (!IsDisposed) Render();
    }
    private async Task OwnerAsync()
    {
        if (_busy || _current is not ProcessReviewSnapshot snapshot || _grid.CurrentRow?.Tag is not ProcessReviewEntry selected) return;
        var cancellation = Begin();
        ProcessOwnerEvidence evidence;
        try { evidence = await IncidentOperations.RunAsync(ct => _processes.Owner(selected, ct), cancellation.Token); }
        catch (Exception ex) { evidence = new(selected.Pid, selected.CreationKey, cancellation.IsCancellationRequested ? "Cancelled" : "Unavailable", "", IncidentQueries.Error(ex), DateTimeOffset.Now); }
        finally { End(cancellation); }
        if (IsDisposed) return;
        snapshot.OwnerChecks.Add(evidence); Render(selected);
    }
    private CancellationTokenSource Begin()
    {
        _busy = true; _cancellation = new(); _watch.Restart(); _timer.Start(); UpdateButtons(); return _cancellation;
    }
    private void End(CancellationTokenSource cancellation)
    {
        cancellation.Dispose(); _cancellation = null; _busy = false; _watch.Stop();
        if (!IsDisposed) { _timer.Stop(); _status.Text = $"Завершено за {_watch.Elapsed.TotalSeconds:0.0} с"; UpdateButtons(); }
    }
    private void Render(ProcessReviewEntry? preserve = null)
    {
        if (_current is null || _busy || IsDisposed) { UpdateButtons(); return; }
        _grid.Rows.Clear(); var filter = Filter();
        if (_current is IncidentSnapshot events)
        {
            var rows = IncidentQueries.Events(events, (IncidentFilter)filter);
            foreach (var e in rows) { var i = _grid.Rows.Add(e.Timestamp?.LocalDateTime, e.Log, e.EventId, IncidentReport.LevelText(e.Level), e.Provider, e.EmitterPid, e.RecordId); _grid.Rows[i].Tag = e; }
            _snapshot.Text = $"Снимок {events.StartedAt:dd.MM HH:mm:ss}: {IncidentReport.StateText(events.State)}. Интервал {events.Window.From:O} — {events.Window.To:O}.\n" +
                string.Join("; ", events.Logs.Select(x => $"{x.Log}: {IncidentReport.StateText(x.State)}, {x.Events.Count} записей")) + $". Видно по фильтру: {rows.Count}.";
        }
        else if (_current is ProcessReviewSnapshot processes)
        {
            var rows = IncidentQueries.Processes(processes, (string)filter);
            foreach (var p in rows)
            {
                var owner = processes.OwnerChecks.LastOrDefault(x => x.Pid == p.Pid && x.CreationKey == p.CreationKey);
                double? memory = p.WorkingSetBytes is ulong bytes ? bytes / 1048576d : null;
                var i = _grid.Rows.Add(p.Pid, p.Name, p.CreatedAt?.LocalDateTime, memory, p.ParentPid, owner is null ? "Не запрошен" : owner.State == "Verified" ? owner.Owner : IncidentReport.StateText(owner.State), p.Executable);
                _grid.Rows[i].Tag = p;
                if (preserve is not null && p.Pid == preserve.Pid && p.CreationKey == preserve.CreationKey) { _grid.CurrentCell = _grid.Rows[i].Cells[0]; _grid.Rows[i].Selected = true; }
            }
            _snapshot.Text = $"Снимок {processes.StartedAt:dd.MM HH:mm:ss}: {IncidentReport.StateText(processes.State)}. Сохранено {processes.Processes.Count}; видно {rows.Count}; строк с замечаниями {processes.Processes.Count(x => x.Warnings.Count > 0)}.\nВремя и владелец относятся к текущему процессу, а не к историческому событию.";
        }
        ShowDetail(); UpdateButtons();
    }
    private void ShowDetail()
    {
        if (_current is null || _busy) return;
        var text = _grid.CurrentRow?.Tag switch
        {
            IncidentEvent e => $"{e.Timestamp:O} | {e.Log} | {e.Provider} | Event ID {e.EventId} | Record ID {e.RecordId}\r\n{IncidentReport.LevelText(e.Level)} | PID издателя: {e.EmitterPid}\r\n\r\n{e.Message}\r\nТекст: {e.MessageState}\r\n\r\n",
            ProcessReviewEntry p => $"PID {p.Pid} | {p.Name} | начало {p.CreatedAt:O}\r\nPPID {p.ParentPid}; session {p.SessionId}; threads {p.Threads}; handles {p.Handles}; working set {p.WorkingSetBytes?.ToString() ?? "—"} байт\r\n\r\nПуть:\r\n{p.Executable}\r\nКоманда (не исполняется):\r\n{p.CommandLine}\r\n\r\n{string.Join("\r\n", p.Warnings)}\r\n\r\n",
            _ => ""
        };
        _detail.Text = text + IncidentReport.Summary(_current, Filter()); UpdateButtons();
    }
    private void UpdateButtons()
    {
        if (IsDisposed) return; _run.Enabled = !_busy; _cancel.Enabled = _busy && _cancellation is { IsCancellationRequested: false };
        _copy.Enabled = _export.Enabled = !_busy && _current is not null;
        _owner.Enabled = !_busy && !_eventsMode && _grid.CurrentRow?.Tag is ProcessReviewEntry;
        _search.Enabled = _from.Enabled = _to.Enabled = _id.Enabled = _log.Enabled = _warningsOnly.Enabled = _grid.Enabled = !_busy;
        _progress.Visible = _busy;
    }
    private void Export()
    {
        if (_busy || _current is not { } snapshot) return;
        using var dialog = new FolderBrowserDialog { Description = "Отчёт содержит сообщения событий, имена, пути и команды процессов. Не публикуйте его без проверки.", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) TryUi(() => _status.Text = "Сохранено: " + IncidentExport.Save(snapshot, Filter(), dialog.SelectedPath));
    }
    private void TryUi(Action action) { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Действие не завершено полностью", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
    private static DateTimePicker DatePicker() => new() { Width = 185, Format = DateTimePickerFormat.Custom, CustomFormat = "dd.MM.yyyy HH:mm:ss" };
    private static FlowLayoutPanel Flow() => new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
    private static Label Label(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(4, 6, 4, 3) };
    private static Button Button(string text, string name) => new() { Text = text, Name = name, AutoSize = true, Padding = new Padding(4, 2, 4, 2) };
}

internal static class IncidentOperations
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static async Task<T> RunAsync<T>(Func<CancellationToken, T> work, CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try { return await Task.Run(() => work(ct), ct).ConfigureAwait(false); }
        finally { Gate.Release(); }
    }
}
internal static class IncidentReviewMenu
{
    public static void Attach(Form main)
    {
        ArgumentNullException.ThrowIfNull(main); ResourceProbeMenu.Attach(main);
        var menu = main.MainMenuStrip!; var group = (ToolStripMenuItem)menu.Items.Find("ReadOnlyInspections", false).Single();
        foreach (var eventsMode in new[] { true, false })
        {
            var name = eventsMode ? "IncidentEventReview" : "IncidentProcessReview";
            if (menu.Items.Find(name, true).Length > 0) continue;
            var item = new ToolStripMenuItem(eventsMode ? "События за время сбоя…" : "Подробности процессов…") { Name = name };
            var captured = eventsMode; item.Click += (_, _) => { using var form = new IncidentReviewForm(captured); form.ShowDialog(main); }; group.DropDownItems.Add(item);
        }
    }
}
internal static class IncidentExport
{
    public static string Save(object snapshot, object filter, string parent)
    {
        var json = IncidentReport.Json(snapshot, filter); var html = IncidentReport.Html(snapshot, filter);
        var directory = Path.Combine(parent, $"G-PC-Incident_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"); Directory.CreateDirectory(directory);
        Write(Path.Combine(directory, "snapshot.json"), json); Write(Path.Combine(directory, "report.html"), html); return directory;
    }
    private static void Write(string path, string text)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); using var writer = new StreamWriter(stream, new UTF8Encoding(false, true)); writer.Write(text);
    }
}
