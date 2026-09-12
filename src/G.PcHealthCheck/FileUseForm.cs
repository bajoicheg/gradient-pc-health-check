using System.Diagnostics;

namespace G.PcHealthCheck;

internal sealed class FileUseForm : Form
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly TextBox _target = new() { Name = "FileUseTarget", Dock = DockStyle.Fill, PlaceholderText = "Полный путь к одному локальному файлу…" };
    private readonly TextBox _search = new() { Name = "FileUseSearch", Width = 430, PlaceholderText = "Приложение, служба, PID или EXE…" };
    private readonly Button _browse = Button("Выбрать файл…", "FileUseBrowse");
    private readonly Button _start = Button("Проверить / повторить", "FileUseStart");
    private readonly Button _stop = Button("Остановить сбор", "FileUseStop");
    private readonly Button _copy = Button("Копировать сводку", "FileUseCopy");
    private readonly Button _export = Button("Сохранить HTML / JSON", "FileUseExport");
    private readonly Button _close = Button("Закрыть", "FileUseClose");
    private readonly DataGridView _grid = new() { Name = "FileUseEvidence", Dock = DockStyle.Fill, ReadOnly = true, RowHeadersVisible = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, BackgroundColor = SystemColors.Window };
    private readonly TextBox _overview = TextArea();
    private readonly TextBox _detail = TextArea();
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = true };
    private readonly ProgressBar _progress = new() { Style = ProgressBarStyle.Marquee, Width = 110, Visible = false };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private readonly Stopwatch _elapsed = new();
    private FileUseSnapshot? _current;
    private FileUseSnapshot? _previous;
    private CancellationTokenSource? _cancellation;
    private bool _busy;
    private bool _exporting;
    private string _stage = "Выберите файл. Сбор ещё не запускался.";

    public FileUseForm()
    {
        Name = "FileUseReview"; Text = "G PC Health Check — кто использует файл";
        AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 9F); Size = new Size(1230, 850); MinimumSize = new Size(980, 700); StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 8 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 4; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 125)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 57)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 43)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);
        root.Controls.Add(new Label { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 0, 0, 8), Text = "КТО ИСПОЛЬЗУЕТ ФАЙЛ — запрос Windows Restart Manager, без закрытия приложений и изменения файла.\nОдин обычный локальный файл. Это не полный список дескрипторов; отсутствие результатов не гарантирует отсутствие блокировки." }, 0, 0);
        var target = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 }; target.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); target.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); target.Controls.Add(_target, 0, 0); target.Controls.Add(_browse, 1, 0); root.Controls.Add(target, 0, 1);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true }; tools.Controls.AddRange([_start, _stop, _copy, _export, _close, _progress]); root.Controls.Add(tools, 0, 2);
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true }; filters.Controls.Add(_search); root.Controls.Add(filters, 0, 3);
        root.Controls.Add(_overview, 0, 4); root.Controls.Add(_grid, 0, 5); root.Controls.Add(_detail, 0, 6); root.Controls.Add(_status, 0, 7);
        Column("Pid", "PID", typeof(uint), 80); Column("Application", "Приложение (RM)", typeof(string), 200); Column("Service", "Служба (RM)", typeof(string), 155);
        Column("Type", "Тип", typeof(string), 140); Column("Session", "Сеанс (RM)", typeof(uint), 85); Column("Exe", "EXE (проверен)", typeof(string), 190); Column("Identity", "Проверка процесса", typeof(string), 230);
        _grid.Columns["Application"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; _grid.Columns["Application"].MinimumWidth = 170;
        _overview.Text = "Проверка начинается только по кнопке. Права используются текущие; повышение автоматически не запрашивается. Перед новой проверкой экспортируйте нужный предыдущий результат.";
        _detail.Text = FileUseReport.NextSteps; _status.Text = _stage;
        _browse.Click += (_, _) => Browse(); _start.Click += async (_, _) => await CollectAsync();
        _stop.Click += (_, _) => { _cancellation?.Cancel(); _stage = "Запрошена остановка. Текущий вызов Windows может задержать возврат; завершённые данные сохранятся в памяти."; UpdateButtons(); };
        _copy.Click += (_, _) => { if (_current is { } current) TryUi(() => Clipboard.SetText(FileUseReport.Summary(current, _previous))); };
        _export.Click += async (_, _) => await ExportAsync(); _close.Click += (_, _) => Close(); _search.TextChanged += (_, _) => RenderRows();
        _target.TextChanged += (_, _) => { UpdateButtons(); if (_current is not null && !_busy) _status.Text = "Поле пути изменено; показанные результаты относятся к пути в сводке. Для нового пути запустите проверку."; };
        _grid.SelectionChanged += (_, _) => { if (_grid.CurrentRow?.Tag is FileUseProcess row) _detail.Text = FileUseReport.Detail(row); };
        _timer.Tick += (_, _) => { if (_busy) _status.Text = $"{_stage} · {_elapsed.Elapsed.TotalSeconds:0.0} с"; };
        FormClosing += (_, e) => { if (_exporting) { e.Cancel = true; _stage = "Дождитесь окончания сохранения файлов."; } else _cancellation?.Cancel(); };
        AcceptButton = _start; CancelButton = _close; UpdateButtons();
    }
    private void Browse()
    {
        using var dialog = new OpenFileDialog { Title = "Выберите один локальный файл", CheckFileExists = true, Multiselect = false, Filter = "Все файлы (*.*)|*.*", DereferenceLinks = false };
        if (dialog.ShowDialog(this) == DialogResult.OK) _target.Text = dialog.FileName;
    }
    private async Task CollectAsync()
    {
        if (_busy) return;
        string path;
        try { path = FileUseCore.NormalizeTarget(_target.Text); }
        catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, "Проверьте путь", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation; _busy = true; _stage = "Ожидаю освобождения сборщика…"; _elapsed.Restart(); _timer.Start(); UpdateButtons();
        try
        {
            await Gate.WaitAsync(cancellation.Token);
            FileUseSnapshot result;
            try
            {
                var progress = new Progress<string>(text => { if (!IsDisposed) _stage = text; });
                result = await Task.Run(() => FileUseService.Collect(new FileUseWindowsSource(), path, ExecutionContextService.Capture(), cancellation.Token, progress), cancellation.Token);
            }
            finally { Gate.Release(); }
            if (IsDisposed) return;
            _previous = _current; _current = result; _overview.Text = FileUseReport.Summary(result, _previous); RenderRows();
        }
        catch (OperationCanceledException) { if (!IsDisposed) _status.Text = "Сбор не начат; предыдущие результаты сохранены."; }
        catch (Exception ex) { if (!IsDisposed) _status.Text = "Сбор не завершён; предыдущие результаты сохранены. " + ex.GetType().Name + ": " + ex.Message; }
        finally { _cancellation = null; _busy = false; if (!IsDisposed) { _timer.Stop(); UpdateButtons(); } }
    }
    private void RenderRows()
    {
        _grid.Rows.Clear(); _detail.Text = FileUseReport.NextSteps;
        if (_current is null) return;
        var rows = _current.Processes.Where(x => FileUseCore.Matches(x, _search.Text)).ToList();
        foreach (var row in rows)
        {
            uint? session = row.ApplicationType is 3 or 1000 || row.SessionId == uint.MaxValue ? null : row.SessionId;
            var i = _grid.Rows.Add(row.Pid, row.ApplicationName, row.ServiceName, FileUseCore.TypeText(row.ApplicationType), session, row.ProcessName, FileUseCore.IdentityText(row.IdentityState));
            _grid.Rows[i].Tag = row;
        }
        _status.Text = $"{FileUseCore.StateText(_current.State)} · {_current.FinishedAt:HH:mm:ss} · показано {rows.Count}/{_current.Processes.Count}. Экспорт сохраняет обе попытки целиком.";
    }
    private async Task ExportAsync()
    {
        if (_busy || _current is not { } current) return;
        using var dialog = new FolderBrowserDialog { Description = "Отчёт содержит пути, приложения и аккаунт. Выберите папку для отдельного комплекта HTML/JSON.", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var previous = _previous; var parent = dialog.SelectedPath;
        _busy = _exporting = true; _stage = "Сохраняю результаты…"; _elapsed.Restart(); _timer.Start(); UpdateButtons();
        try { var folder = await Task.Run(() => FileUseReport.Save(current, previous, parent)); if (!IsDisposed) _status.Text = "Сохранено: " + folder; }
        catch (Exception ex) { if (!IsDisposed) MessageBox.Show(this, "Сохранение не завершено полностью. " + ex.Message, "Экспорт", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { _busy = _exporting = false; if (!IsDisposed) { _timer.Stop(); UpdateButtons(); } }
    }
    private void UpdateButtons()
    {
        _start.Enabled = !_busy && !string.IsNullOrWhiteSpace(_target.Text); _browse.Enabled = _target.Enabled = _search.Enabled = !_busy;
        _stop.Enabled = _busy && _cancellation is { IsCancellationRequested: false }; _copy.Enabled = _export.Enabled = !_busy && _current is not null; _progress.Visible = _busy;
    }
    private void Column(string name, string text, Type type, int width) => _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = text, ValueType = type, Width = width, SortMode = DataGridViewColumnSortMode.Automatic, DefaultCellStyle = new DataGridViewCellStyle { NullValue = "—" } });
    private static TextBox TextArea() => new() { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private static Button Button(string text, string name) => new() { Text = text, Name = name, AutoSize = true, Padding = new Padding(5, 2, 5, 2) };
    private void TryUi(Action action) { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Действие не завершено", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
    protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }
}
internal static class FileUseMenu
{
    public static void Attach(Form main)
    {
        ArgumentNullException.ThrowIfNull(main); ReadOnlyReviewMenu.Attach(main);
        var group = (ToolStripMenuItem)main.MainMenuStrip!.Items.Find("ReadOnlyInspections", false).Single();
        if (group.DropDownItems.Find("FileUseOpen", false).Length > 0) return;
        var item = new ToolStripMenuItem("Кто использует файл…") { Name = "FileUseOpen" };
        item.Click += (_, _) => { using var window = new FileUseForm(); window.ShowDialog(main); }; group.DropDownItems.Add(item);
    }
}
