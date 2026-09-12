using System.Diagnostics;

namespace G.PcHealthCheck;

internal sealed class EndpointReviewForm : Form
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly Button _start = Button("Собрать / повторить", "EndpointStart");
    private readonly Button _stop = Button("Остановить сбор", "EndpointStop");
    private readonly Button _copy = Button("Копировать сводку", "EndpointCopy");
    private readonly Button _export = Button("Сохранить HTML / JSON", "EndpointExport");
    private readonly Button _close = Button("Закрыть", "EndpointClose");
    private readonly TextBox _search = new() { Name = "EndpointSearch", Width = 350, PlaceholderText = "Процесс, PID, IP, порт или состояние…" };
    private readonly ComboBox _filter = new() { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _overview = TextArea();
    private readonly TextBox _detail = TextArea();
    private readonly DataGridView _grid = new() { Name = "EndpointEvidence", Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, BackgroundColor = SystemColors.Window };
    private readonly ProgressBar _progress = new() { Width = 110, Style = ProgressBarStyle.Marquee, Visible = false };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = true };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private EndpointSnapshot? _current;
    private EndpointSnapshot? _previous;
    private List<EndpointObservation> _rows = [];
    private CancellationTokenSource? _cancellation;
    private bool _busy;
    private bool _exporting;
    private readonly Stopwatch _elapsed = new();
    private string _stage = "Готов к сбору.";

    public EndpointReviewForm()
    {
        Name = "EndpointReview"; Text = "G PC Health Check — сетевые соединения и порты";
        AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 9F); Size = new Size(1320, 850); MinimumSize = new Size(1000, 680); StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 7 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 65)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 35)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);
        root.Controls.Add(new Label { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 0, 0, 8), Text = "ЛОКАЛЬНЫЕ TCP/UDP — только чтение, без проверки удалённых портов и обратного DNS.\nUDP — локальные привязки; LISTEN не доказывает доступность извне. Сравнение снимков не является трассировкой событий." }, 0, 0);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true }; tools.Controls.AddRange([_start, _stop, _copy, _export, _close, _progress]); root.Controls.Add(tools, 0, 1);
        _filter.Items.AddRange(["Все записи", "TCP", "UDP", "TCP: LISTEN", "TCP: ESTABLISHED", "Изменения между снимками"]); _filter.SelectedIndex = 0;
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true }; filters.Controls.AddRange([_filter, _search]); root.Controls.Add(filters, 0, 2);
        root.Controls.Add(_overview, 0, 3); root.Controls.Add(_grid, 0, 4); root.Controls.Add(_detail, 0, 5); root.Controls.Add(_status, 0, 6);
        Column("Change", "Наблюдение", typeof(string), 190); Column("Table", "Протокол", typeof(string), 70); Column("PID", "PID", typeof(uint), 70);
        Column("Process", "Процесс", typeof(string), 160); Column("Local", "Локальный IP", typeof(string), 170); Column("LocalPort", "Порт", typeof(int), 65);
        Column("Remote", "Удалённый IP", typeof(string), 170); Column("RemotePort", "Порт", typeof(int), 65); Column("State", "Состояние", typeof(string), 130);
        _grid.Columns["Process"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _grid.Columns["Process"].MinimumWidth = 140;
        _overview.Text = "Снимок ещё не собран. Нажмите «Собрать / повторить». Новая проверка сдвигает текущий снимок в предыдущий; экспортируйте нужные данные заранее.";
        _detail.Text = "Выберите запись. «Больше не наблюдается» относится к предыдущему снимку, а не к текущему состоянию.";
        _start.Click += async (_, _) => await CollectAsync();
        _stop.Click += (_, _) => { _cancellation?.Cancel(); _stage = "Запрошена остановка; завершённые таблицы будут сохранены в памяти."; UpdateButtons(); };
        _export.Click += async (_, _) => await ExportAsync();
        _copy.Click += (_, _) => { if (_current is { } current) TryUi(() => Clipboard.SetText(EndpointReviewReport.Summary(current, _previous))); };
        _close.Click += (_, _) => Close(); _search.TextChanged += (_, _) => RenderRows(); _filter.SelectedIndexChanged += (_, _) => RenderRows();
        _grid.SelectionChanged += (_, _) => { if (_grid.CurrentRow?.Tag is EndpointObservation row) _detail.Text = EndpointReviewReport.Detail(row); };
        _timer.Tick += (_, _) => { if (_busy) _status.Text = $"{_stage} · {_elapsed.Elapsed.TotalSeconds:0.0} с"; };
        FormClosing += (_, e) => { if (_exporting) { e.Cancel = true; _stage = "Дождитесь окончания сохранения файлов."; } else _cancellation?.Cancel(); };
        AcceptButton = _start; CancelButton = _close; _status.Text = _stage; UpdateButtons();
    }
    private async Task CollectAsync()
    {
        if (_busy) return;
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation; _busy = true; _stage = "Ожидаю освобождения сборщика…"; _elapsed.Restart(); _timer.Start(); UpdateButtons();
        try
        {
            await Gate.WaitAsync(cancellation.Token);
            EndpointSnapshot result;
            try
            {
                var progress = new Progress<string>(text => { if (!IsDisposed) _stage = text; });
                result = await Task.Run(() => EndpointReviewService.Collect(new EndpointWindowsSource(), ExecutionContextService.Capture(), cancellation.Token, progress), cancellation.Token);
            }
            finally { Gate.Release(); }
            if (IsDisposed) return;
            _previous = _current; _current = result; _rows = EndpointReviewCore.Compare(result, _previous);
            _overview.Text = EndpointReviewReport.Summary(result, _previous); RenderRows();
        }
        catch (OperationCanceledException) { if (!IsDisposed) _status.Text = "Сбор не начат / отменён; прошлый снимок сохранён."; }
        catch (Exception ex) { if (!IsDisposed) _status.Text = "Сбор не завершён; прошлый снимок сохранён. " + EndpointReviewService.Describe(ex); }
        finally { _cancellation = null; _busy = false; if (!IsDisposed) { _timer.Stop(); UpdateButtons(); } }
    }
    private void RenderRows()
    {
        if (_filter.SelectedIndex < 0) return;
        var filter = new[] { "All", "TCP", "UDP", "LISTEN", "ESTABLISHED", "Changes" }[_filter.SelectedIndex];
        var matches = _rows.Where(x => EndpointReviewCore.Matches(x, _search.Text, filter)).ToList();
        _grid.Rows.Clear(); _detail.Clear();
        foreach (var item in matches.Take(2000))
        {
            var r = item.Row;
            var i = _grid.Rows.Add(EndpointReviewCore.ChangeText(item.Change), r.Table, r.Pid, r.ProcessName.Length == 0 ? "—" : r.ProcessName, r.LocalAddress, r.LocalPort, r.RemoteAddress, r.RemotePort, r.State);
            _grid.Rows[i].Tag = item;
            _grid.Rows[i].Cells["Process"].ToolTipText = EndpointReviewCore.ProcessText(r.ProcessEvidence);
            if (item.Change == "NotObserved") _grid.Rows[i].DefaultCellStyle.ForeColor = SystemColors.GrayText;
        }
        _status.Text = _current is null ? "Снимок ещё не собран." : $"{EndpointReviewCore.CollectionText(_current.State)} · {_current.FinishedAt:HH:mm:ss} · совпадений {matches.Count}, показано {_grid.Rows.Count}. Экспорт включает оба снимка целиком.";
    }
    private async Task ExportAsync()
    {
        if (_busy || _current is not { } current) return;
        using var dialog = new FolderBrowserDialog { Description = "Отчёты содержат сетевые адреса, имена процессов и аккаунтов. Выберите папку для отдельного комплекта HTML/JSON.", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var previous = _previous; var target = dialog.SelectedPath;
        _busy = _exporting = true; _stage = "Сохраняю оба снимка…"; _elapsed.Restart(); _timer.Start(); UpdateButtons();
        try { var folder = await Task.Run(() => EndpointReviewReport.Save(current, previous, target)); if (!IsDisposed) _status.Text = "Сохранено: " + folder; }
        catch (Exception ex) { if (!IsDisposed) MessageBox.Show(this, "Сохранение не завершено полностью. " + ex.Message, "Экспорт", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { _busy = _exporting = false; if (!IsDisposed) { _timer.Stop(); UpdateButtons(); } }
    }
    private void UpdateButtons()
    {
        _start.Enabled = !_busy; _stop.Enabled = _busy && _cancellation is { IsCancellationRequested: false };
        _copy.Enabled = _export.Enabled = !_busy && _current is not null; _search.Enabled = _filter.Enabled = !_busy; _progress.Visible = _busy;
    }
    private void Column(string name, string text, Type type, int width) => _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = text, ValueType = type, Width = width, SortMode = DataGridViewColumnSortMode.Automatic, DefaultCellStyle = new DataGridViewCellStyle { NullValue = "—" } });
    private static Button Button(string text, string name) => new() { Text = text, Name = name, AutoSize = true, Padding = new Padding(5, 2, 5, 2) };
    private static TextBox TextArea() => new() { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private void TryUi(Action action) { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Действие не завершено", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
    protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }
}
internal static class EndpointReviewMenu
{
    public static void Attach(Form main)
    {
        ArgumentNullException.ThrowIfNull(main); ReadOnlyReviewMenu.Attach(main);
        var group = (ToolStripMenuItem)main.MainMenuStrip!.Items.Find("ReadOnlyInspections", false).Single();
        if (group.DropDownItems.Find("EndpointReviewOpen", false).Length > 0) return;
        var item = new ToolStripMenuItem("Сетевые соединения и порты (TCP/UDP)…") { Name = "EndpointReviewOpen" };
        item.Click += (_, _) => { using var window = new EndpointReviewForm(); window.ShowDialog(main); };
        group.DropDownItems.Add(item);
    }
}
