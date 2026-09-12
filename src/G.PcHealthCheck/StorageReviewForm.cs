using System.Diagnostics;

namespace G.PcHealthCheck;

internal sealed class StorageReviewForm : Form
{
    // Closing/reopening must not accumulate background provider work.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly bool _folder;
    private readonly Button _scan = Button("Проверить / повторить", "StorageStart");
    private readonly Button _cancel = Button("Остановить сбор", "StorageCancel");
    private readonly Button _copy = Button("Копировать сводку", "StorageCopy");
    private readonly Button _export = Button("Сохранить HTML / JSON", "StorageExport");
    private readonly Button _browse = Button("Выбрать папку…", "StorageBrowse");
    private readonly Button _close = Button("Закрыть", "StorageClose");
    private readonly TextBox _root = new() { Width = 660, Name = "StorageRoot" };
    private readonly TextBox _search = new() { Width = 360, Name = "StorageSearch", PlaceholderText = "Поиск по пути, имени или идентификатору…" };
    private readonly ComboBox _view = new() { Width = 255, Name = "StorageView", DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _overview = new() { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly TextBox _detail = new() { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill };
    private readonly DataGridView _grid = new() { Name = "StorageEvidence" };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = true };
    private readonly ProgressBar _progress = new() { Style = ProgressBarStyle.Marquee, Width = 110, Visible = false };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private CancellationTokenSource? _cancellation;
    private Stopwatch? _elapsed;
    private object? _snapshot;
    private string _stage = "Готов к сбору.";

    public StorageReviewForm(bool folder)
    {
        _folder = folder;
        Text = "G PC Health Check — " + (folder ? "место по папкам" : "подробности накопителей");
        Name = folder ? "FolderUsage" : "DiskDetails";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F);
        Size = new Size(1260, 850); MinimumSize = new Size(960, 700);
        StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 8 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 4; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 115));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 63));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 37));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);
        layout.Controls.Add(new Label
        {
            AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 8),
            Text = folder
                ? "АНАЛИЗ ПАПКИ — только метаданные, без удаления. Размеры логические, не физически занятое место.\nСсылки исключаются. Сетевой/облачный путь может вызвать обращения соответствующего поставщика; выбирайте область осознанно."
                : "НАКОПИТЕЛИ — сведения локального Windows Storage WMI. Это не полный SMART и не тест поверхности.\nНеизвестное значение отмечено «—». Результат сбора и состояние устройства — разные понятия."
        }, 0, 0);
        var target = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Visible = folder };
        _root.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        target.Controls.AddRange([new Label { Text = "Папка:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) }, _root, _browse]);
        layout.Controls.Add(target, 0, 1);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        tools.Controls.AddRange([_scan, _cancel, _copy, _export, _close, _progress]);
        layout.Controls.Add(tools, 0, 2);
        var filter = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        _view.Items.AddRange(["Все папки (включая вложенные)", "Папки первого уровня", "Крупнейшие файлы"]);
        _view.SelectedIndex = 0; _view.Visible = folder;
        filter.Controls.AddRange([_view, _search]); layout.Controls.Add(filter, 0, 3);
        layout.Controls.Add(_overview, 0, 4);
        _grid.Dock = DockStyle.Fill; _grid.ReadOnly = true; _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false; _grid.AllowUserToDeleteRows = false; _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; _grid.MultiSelect = false;
        _grid.BackgroundColor = SystemColors.Window; _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None; _grid.RowTemplate.Height = 27;
        layout.Controls.Add(_grid, 0, 5); layout.Controls.Add(_detail, 0, 6); layout.Controls.Add(_status, 0, 7);
        _overview.Text = "Снимок ещё не собран. Проверка начинается только по кнопке.";
        _detail.Text = "Выберите строку для подробностей. Экспорт сохраняет весь снимок независимо от поиска.";
        _status.Text = _stage;
        _scan.Click += async (_, _) => await ScanAsync();
        _cancel.Click += (_, _) =>
        {
            _cancellation?.Cancel();
            _stage = "Отмена запрошена; ожидается возврат текущего вызова. Завершённые записи будут сохранены.";
            UpdateButtons();
        };
        _browse.Click += (_, _) => Browse();
        _copy.Click += (_, _) =>
        {
            if (_snapshot is { } snapshot)
                TryUi(() => { Clipboard.SetText(StorageReviewReport.Summary(snapshot)); _status.Text = "Сводка всего снимка скопирована."; });
        };
        _export.Click += (_, _) => Export(); _close.Click += (_, _) => Close();
        _view.SelectedIndexChanged += (_, _) => RenderRows(); _search.TextChanged += (_, _) => RenderRows();
        _grid.SelectionChanged += (_, _) => RenderDetail();
        _timer.Tick += (_, _) =>
        {
            if (_cancellation is not null && !IsDisposed) _status.Text = $"{_stage} · {_elapsed?.Elapsed.TotalSeconds:0.0} с";
        };
        FormClosing += (_, _) => _cancellation?.Cancel();
        AcceptButton = _scan; CancelButton = _close;
        UpdateButtons();
    }

    private async Task ScanAsync()
    {
        if (_cancellation is not null || IsDisposed) return;
        var root = _root.Text; var options = new FolderUsageOptions();
        if (_folder)
        {
            try { root = FolderUsageService.Validate(root, options); }
            catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, "Проверьте путь", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        }
        var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        _elapsed = Stopwatch.StartNew();
        _stage = "Ожидается доступ к сборщику. Предыдущий снимок пока остаётся на экране.";
        _timer.Start(); UpdateButtons();
        var progress = new Progress<string>(text =>
        {
            if (!IsDisposed && ReferenceEquals(_cancellation, cancellation) && !cancellation.IsCancellationRequested) _stage = text;
        });
        try
        {
            await Gate.WaitAsync(cancellation.Token);
            object snapshot;
            try
            {
                _stage = _folder ? "Обхожу выбранную папку…" : "Читаю сведения и связанные счётчики накопителей…";
                snapshot = await Task.Run<object>(() => _folder
                    ? FolderUsageService.Collect(root, options, new WindowsFolderUsageSource(), cancellation.Token, progress)
                    : DiskDetailsService.Collect(new WindowsDiskDetailsSource(), cancellation.Token), cancellation.Token);
            }
            finally { Gate.Release(); }
            if (IsDisposed || Disposing) return;
            // Stopped snapshots keep completed records and explicitly describe incomplete data.
            DisplaySnapshot(snapshot);
            var outcome = snapshot is FolderUsageSnapshot f ? f.Outcome : ((DiskDetailsSnapshot)snapshot).Outcome;
            _status.Text = $"{StorageReviewReport.OutcomeText(outcome)} · {_elapsed.Elapsed.TotalSeconds:0.0} с. Для файлов отчёта нажмите «Сохранить HTML / JSON».";
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed) _status.Text = "Ожидание/запуск сбора отменён. Предыдущий снимок не заменён; смотрите его дату.";
        }
        catch (Exception ex)
        {
            if (!IsDisposed) _status.Text = $"Сбор не завершён: {ex.GetType().Name}, 0x{ex.HResult:X8}. Предыдущий снимок не заменён.";
        }
        finally
        {
            _cancellation = null; cancellation.Dispose();
            if (!IsDisposed && !Disposing) { _timer.Stop(); UpdateButtons(); }
        }
    }

    internal void DisplaySnapshot(object snapshot)
    {
        if ((_folder && snapshot is not FolderUsageSnapshot) || (!_folder && snapshot is not DiskDetailsSnapshot))
            throw new ArgumentException("Тип снимка не соответствует окну.", nameof(snapshot));
        _snapshot = snapshot;
        _overview.Text = StorageReviewReport.Summary(snapshot).ReplaceLineEndings("\r\n");
        RenderRows(); UpdateButtons();
    }
    private void RenderRows()
    {
        if (IsDisposed) return;
        _grid.Rows.Clear(); _grid.Columns.Clear();
        var query = _search.Text.Trim();
        bool Matches(string text) => query.Length == 0 || text.Contains(query, StringComparison.OrdinalIgnoreCase);
        var matched = 0;
        if (_snapshot is FolderUsageSnapshot f)
        {
            var total = f.Folders.FirstOrDefault()?.Bytes ?? 0;
            if (_view.SelectedIndex == 2)
            {
                Column("Bytes", "Байт", 135, typeof(long), "N0");
                Column("Modified", "Изменён", 185, typeof(DateTimeOffset), "dd.MM.yyyy HH:mm:ss zzz");
                Column("Path", "Полный путь", 600, typeof(string), fill: true);
                var rows = f.LargestFiles.Where(x => Matches(x.Path)).ToList(); matched = rows.Count;
                foreach (var row in rows) _grid.Rows[_grid.Rows.Add(row.Bytes, row.Modified, row.Path)].Tag = row;
            }
            else
            {
                Column("Bytes", "Всего байт", 155, typeof(decimal), "N0");
                Column("Files", "Файлов", 95, typeof(int), "N0");
                Column("Percent", "Доля учтённого, %", 120, typeof(decimal), "0.0");
                Column("Scope", "Полнота", 130, typeof(string));
                Column("Path", "Папка (размер включает подпапки)", 550, typeof(string), fill: true);
                var rows = f.Folders.Where(x => (_view.SelectedIndex != 1 || x.ParentIndex == 0) && Matches(x.Path))
                    .OrderByDescending(x => x.Bytes).ThenBy(x => x.Path, StringComparer.Ordinal).ToList(); matched = rows.Count;
                foreach (var row in rows.Take(2000))
                {
                    var percent = total > 0 ? row.Bytes / total * 100m : 0m;
                    _grid.Rows[_grid.Rows.Add(row.Bytes, row.Files, percent, row.Incomplete ? "Неполная" : "В рамках обхода", row.Path)].Tag = row;
                }
            }
        }
        else if (_snapshot is DiskDetailsSnapshot d)
        {
            Column("Attention", "Внимание", 95, typeof(string)); Column("Id", "DeviceId", 85, typeof(string));
            Column("Name", "Накопитель", 240, typeof(string), fill: true); Column("Health", "HealthStatus Windows", 195, typeof(string));
            Column("Size", "Размер, GiB", 110, typeof(decimal), "0.0"); Column("Temperature", "°C", 65, typeof(int));
            Column("Wear", "Износ, %", 90, typeof(int)); Column("Hours", "Наработка, ч", 100, typeof(ulong), "N0");
            Column("Counters", "Связанные данные", 120, typeof(string));
            var rows = d.Disks.Where(x => Matches(x.Name + " " + x.DeviceId + " " + x.Firmware + " " + DiskDetailsService.BusText(x.BusType)))
                .OrderBy(x => StorageReviewReport.Rank(DiskDetailsService.Attention(x))).ToList(); matched = rows.Count;
            foreach (var disk in rows)
            {
                var attention = DiskDetailsService.Attention(disk);
                var i = _grid.Rows.Add(attention, disk.DeviceId, disk.Name, DiskDetailsService.HealthText(disk.Health),
                    disk.Size is ulong size ? (decimal)size / 1073741824m : null,
                    disk.Reliability?.Temperature, disk.Reliability?.Wear, disk.Reliability?.PowerOnHours, disk.CounterState);
                _grid.Rows[i].Tag = disk;
                if (attention == "CRIT") _grid.Rows[i].DefaultCellStyle.BackColor = Color.MistyRose;
                else if (attention == "WARN") _grid.Rows[i].DefaultCellStyle.BackColor = Color.LemonChiffon;
            }
        }
        if (_snapshot is not null && _cancellation is null)
            _status.Text = $"Показано {_grid.Rows.Count:N0} из {matched:N0} совпадений. Таблица ограничена 2000 строками; поиск — по всему снимку, экспорт — без фильтра.";
        RenderDetail();
    }
    private void RenderDetail()
    {
        _detail.Text = _grid.CurrentRow?.Tag switch
        {
            FolderUsageRow f => $"{f.Path}\r\nВсего: {StorageReviewReport.Bytes(f.Bytes)}; файлов: {f.Files:N0}.\r\nНепосредственно в папке: {StorageReviewReport.Bytes(f.OwnBytes)}; файлов: {f.OwnFiles:N0}.\r\nПолнота: {(f.Incomplete ? "неполные данные" : "обработано в заявленной области")}.\r\n{StorageReviewReport.FolderScope}",
            LargeFolderFile f => $"{f.Path}\r\n{StorageReviewReport.Bytes(f.Bytes)}\r\nИзменён: {f.Modified?.ToString("O") ?? "—"}\r\nБольшой размер не является рекомендацией удалить файл.",
            PhysicalDiskDetail d => DiskDetailsService.Describe(d).ReplaceLineEndings("\r\n"),
            _ => "Выберите строку для полного пути и подробностей."
        };
    }
    private void Column(string name, string caption, int width, Type type, string format = "", bool fill = false)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name, HeaderText = caption, Width = width, ValueType = type,
            SortMode = DataGridViewColumnSortMode.Automatic, MinimumWidth = 60,
            AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
            DefaultCellStyle = new DataGridViewCellStyle { Format = format, NullValue = "—" }
        });
    }
    private void Browse()
    {
        if (_cancellation is not null) return;
        using var dialog = new FolderBrowserDialog { Description = "Выберите папку для анализа метаданных. Файлы не удаляются.", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) _root.Text = dialog.SelectedPath;
    }
    private void Export()
    {
        if (_snapshot is not { } snapshot || _cancellation is not null) return;
        using var dialog = new FolderBrowserDialog { Description = "Отчёт содержит пути и идентификаторы. Выберите папку; перед передачей проверьте данные.", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        TryUi(() => { _status.Text = "HTML и JSON сохранены: " + StorageReviewReport.Save(snapshot, dialog.SelectedPath); });
    }
    private void UpdateButtons()
    {
        var busy = _cancellation is not null;
        _scan.Enabled = !busy; _cancel.Enabled = busy && !_cancellation!.IsCancellationRequested;
        _copy.Enabled = _export.Enabled = !busy && _snapshot is not null;
        _root.Enabled = _browse.Enabled = !busy; _progress.Visible = busy;
    }
    private void TryUi(Action action)
    {
        try { action(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Действие не завершено полностью", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _cancellation?.Cancel(); _timer.Dispose(); }
        base.Dispose(disposing);
    }
    private static Button Button(string title, string name) => new() { Text = title, Name = name, AutoSize = true, Padding = new Padding(5, 2, 5, 2) };
}

internal static class StorageReviewMenu
{
    public static void Attach(Form main)
    {
        ArgumentNullException.ThrowIfNull(main);
        ReadOnlyReviewMenu.Attach(main);
        var group = (ToolStripMenuItem)main.MainMenuStrip!.Items.Find("ReadOnlyInspections", false).Single();
        if (group.DropDownItems.Find("StorageFolderAnalysis", false).Length > 0) return;
        var folder = new ToolStripMenuItem("Место по папкам…") { Name = "StorageFolderAnalysis" };
        folder.Click += (_, _) => { using var window = new StorageReviewForm(true); window.ShowDialog(main); };
        var disk = new ToolStripMenuItem("Подробности накопителей…") { Name = "StorageDiskDetails" };
        disk.Click += (_, _) => { using var window = new StorageReviewForm(false); window.ShowDialog(main); };
        group.DropDownItems.Add(folder); group.DropDownItems.Add(disk);
    }
}
