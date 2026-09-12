using System.Text;

namespace G.PcHealthCheck;

internal sealed class ReadOnlyReviewForm : Form
{
    // Closing/reopening a window cannot accumulate concurrent filesystem/WMI work.
    private static readonly SemaphoreSlim CollectionGate = new(1, 1);
    private readonly bool _temp;
    private readonly int _days;
    private readonly Button _scan = MakeButton("Проверить / повторить");
    private readonly Button _cancel = MakeButton("Отменить сбор");
    private readonly Button _copy = MakeButton("Копировать снимок");
    private readonly Button _export = MakeButton("Сохранить HTML / JSON");
    private readonly Button _close = MakeButton("Закрыть");
    private readonly ProgressBar _progress = new() { Style = ProgressBarStyle.Marquee, Width = 110, Visible = false };
    private readonly TextBox _search = new() { Width = 420, PlaceholderText = "Название, команда, область, источник…" };
    private readonly TextBox _overview = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly TextBox _detail = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, WordWrap = false };
    private readonly DataGridView _grid = new();
    private readonly Label _status = new() { AutoSize = true, Dock = DockStyle.Fill };
    private object? _snapshot;
    private CancellationTokenSource? _cancellation;

    public ReadOnlyReviewForm(bool temp, int days)
    {
        if (days is < 1 or > 30) throw new ArgumentOutOfRangeException(nameof(days));
        _temp = temp; _days = days;
        Text = "G PC Health Check 0.6.0 — " + (temp ? "предпросмотр очистки Temp" : "разбор автозагрузки");
        Name = temp ? "TempPreview" : "StartupReview";
        AutoScaleMode = AutoScaleMode.Dpi; Size = new Size(1180, 820); MinimumSize = new Size(900, 660);
        StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 9F);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 7 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 0, 0, 8),
            Text = temp ? "ПРЕДПРОСМОТР TEMР — только чтение. Удаление выполняется отдельно через рекомендации главного окна."
                : "АВТОЗАГРУЗКА — только чтение. Команды не выполняются, записи не отключаются; включение и влияние на загрузку не определяются." }, 0, 0);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        toolbar.Controls.AddRange([_scan, _cancel, _copy, _export, _close, _progress]); layout.Controls.Add(toolbar, 0, 1);
        var filter = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Visible = !temp };
        filter.Controls.Add(new Label { Text = "Поиск:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) }); filter.Controls.Add(_search);
        layout.Controls.Add(filter, 0, 2); layout.Controls.Add(_overview, 0, 3);
        _grid.Name = "Evidence"; _grid.Dock = DockStyle.Fill; _grid.ReadOnly = true; _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false; _grid.AllowUserToDeleteRows = false; _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; _grid.MultiSelect = false;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None; _grid.RowTemplate.Height = 26;
        _grid.BackgroundColor = SystemColors.Window;
        if (temp)
        {
            Column("Bytes", "Размер, байт", 130, typeof(long), "N0");
            Column("Modified", "Изменён", 175, typeof(DateTime), "dd.MM.yyyy HH:mm:ss");
            Column("Path", "Полный путь — крупнейшие кандидаты", 580, typeof(string), fill: true);
        }
        else
        {
            Column("Name", "Название", 180, typeof(string)); Column("Command", "Команда / путь к записи", 360, typeof(string), fill: true);
            Column("Scope", "Область / аккаунт", 160, typeof(string)); Column("Source", "Источник", 260, typeof(string));
            Column("State", "Включение", 135, typeof(string));
        }
        layout.Controls.Add(_grid, 0, 4); layout.Controls.Add(_detail, 0, 5); layout.Controls.Add(_status, 0, 6);
        _scan.Click += async (_, _) => await ScanAsync();
        _cancel.Click += (_, _) => { _cancellation?.Cancel(); _status.Text = "Отмена запрошена; ожидается завершение текущего вызова поставщика данных."; UpdateButtons(); };
        _copy.Click += (_, _) => { if (_snapshot is { } snapshot) TryUi(() => { Clipboard.SetText(ReviewReport.Summary(snapshot)); _status.Text = "Скопирован весь сохранённый снимок, независимо от поиска."; }); };
        _export.Click += (_, _) => Export(); _close.Click += (_, _) => Close();
        _search.TextChanged += (_, _) => RenderRows(); _grid.SelectionChanged += (_, _) => RenderDetail();
        Shown += async (_, _) => await ScanAsync(); FormClosing += (_, _) => _cancellation?.Cancel();
        AcceptButton = _scan; CancelButton = _close;
        _overview.Text = "Снимок ещё не собран."; _status.Text = "Готов к проверке."; UpdateButtons();
    }

    private void Column(string name, string caption, int width, Type type, string? format = null, bool fill = false)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = caption, Width = width,
            ValueType = type, SortMode = DataGridViewColumnSortMode.Automatic,
            AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
            MinimumWidth = 100, DefaultCellStyle = new DataGridViewCellStyle { Format = format ?? "", NullValue = "—" } });
    }
    private async Task ScanAsync()
    {
        if (_cancellation is not null || IsDisposed) return;
        var cancellation = new CancellationTokenSource(); _cancellation = cancellation; UpdateButtons();
        var progress = new Progress<string>(text => { if (!IsDisposed && !cancellation.IsCancellationRequested) _status.Text = text; });
        try
        {
            _status.Text = "Собираю новый снимок; предыдущий пока остаётся на экране…";
            await CollectionGate.WaitAsync(cancellation.Token);
            object result;
            try
            {
                result = await Task.Run<object>(() => _temp
                    ? TempPreviewService.CollectUser(_days, cancellation.Token, progress)
                    : StartupReviewService.Collect(cancellation.Token, progress), cancellation.Token);
            }
            finally { CollectionGate.Release(); }
            if (IsDisposed) return;
            cancellation.Token.ThrowIfCancellationRequested();
            DisplaySnapshot(result);
            _status.Text = "Сбор завершён. Состояние и ограничения указаны над таблицей; снимок не является результатом исправления.";
        }
        catch (OperationCanceledException) { if (!IsDisposed) _status.Text = "Сбор отменён. Предыдущий снимок не заменён; его дата указана выше."; }
        catch (Exception ex) { if (!IsDisposed) _status.Text = $"Сбор не завершён: {ex.GetType().Name}, 0x{ex.HResult:X8}. Предыдущий снимок не заменён."; }
        finally { _cancellation = null; cancellation.Dispose(); if (!IsDisposed) UpdateButtons(); }
    }
    internal void DisplaySnapshot(object snapshot)
    {
        if ((_temp && snapshot is not TempPreviewSnapshot) || (!_temp && snapshot is not StartupReviewSnapshot))
            throw new ArgumentException("Тип снимка не соответствует окну.", nameof(snapshot));
        _snapshot = snapshot; _overview.Text = ReviewReport.Overview(snapshot).ReplaceLineEndings("\r\n"); RenderRows(); UpdateButtons();
    }
    private void RenderRows()
    {
        _grid.Rows.Clear();
        if (_snapshot is TempPreviewSnapshot t)
            foreach (var row in t.LargestFiles) _grid.Rows[_grid.Rows.Add(row.Bytes, row.LastWriteTime, row.Path)].Tag = row;
        else if (_snapshot is StartupReviewSnapshot s)
        {
            var rows = StartupReviewService.Filter(s, _search.Text);
            foreach (var row in rows) _grid.Rows[_grid.Rows.Add(row.Name, row.Command, row.Scope, row.Source, row.State)].Tag = row;
            if (_cancellation is null) _status.Text = $"Показано {rows.Count} из {s.Entries.Count}. Экспорт и копирование включают весь снимок.";
        }
        RenderDetail();
    }
    private void RenderDetail()
    {
        _detail.Text = _grid.CurrentRow?.Tag switch
        {
            TempCandidate t => $"{t.Path}\r\nРазмер: {ReviewReport.Bytes(t.Bytes)}\r\nИзменён: {t.LastWriteTime:O}\r\nКандидат по возрасту, не гарантированно удаляемый файл.",
            StartupReviewEntry s => $"{s.Name}\r\nОбласть: {s.Scope}\r\nИсточник: {s.Source}\r\nВключение: {s.State}\r\nКоманда / ссылка на файл (как прочитана):\r\n{s.Command}",
            _ => "Выберите строку для просмотра полного пути или команды."
        };
    }
    private void UpdateButtons()
    {
        var busy = _cancellation is not null;
        _scan.Enabled = !busy; _cancel.Enabled = busy && !_cancellation!.IsCancellationRequested;
        _copy.Enabled = _export.Enabled = !busy && _snapshot is not null;
        _search.Enabled = !busy; _progress.Visible = busy;
    }
    private void Export()
    {
        if (_snapshot is not { } snapshot || _cancellation is not null) return;
        using var dialog = new FolderBrowserDialog { Description = "Отчёты содержат пути, аккаунты и команды. Выберите папку; не публикуйте без проверки.", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        TryUi(() => { var path = ReviewExport.Save(snapshot, dialog.SelectedPath); _status.Text = "HTML и JSON сохранены: " + path; });
    }
    private void TryUi(Action action)
    {
        try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Действие не завершено полностью", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    private static Button MakeButton(string title) => new() { Text = title, AutoSize = true, Padding = new Padding(5, 2, 5, 2) };
}

internal static class ReviewExport
{
    public static string Save(object snapshot, string parent)
    {
        // Build both documents before writing; never overwrite earlier evidence.
        var json = ReviewReport.Json(snapshot); var html = ReviewReport.Html(snapshot);
        var path = Path.Combine(parent, $"G-PC-Review_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        Write(Path.Combine(path, "snapshot.json"), json); Write(Path.Combine(path, "report.html"), html);
        return path;
    }
    private static void Write(string path, string text)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false, true)); writer.Write(text);
    }
}

internal static class ReadOnlyReviewMenu
{
    public static void Attach(Form main)
    {
        ArgumentNullException.ThrowIfNull(main);
        CommonProblemsMenu.Attach(main);
        var menu = main.MainMenuStrip!;
        if (menu.Items.Find("ReadOnlyInspections", false).Length > 0) return;
        var group = new ToolStripMenuItem("Анализ") { Name = "ReadOnlyInspections" };
        foreach (var temp in new[] { true, false })
        {
            var captured = temp;
            var item = new ToolStripMenuItem(temp ? "Предпросмотр очистки Temp…" : "Разбор автозагрузки…");
            item.Click += (_, _) => { using var form = new ReadOnlyReviewForm(captured, new Thresholds().TempOlderThanDays); form.ShowDialog(main); };
            group.DropDownItems.Add(item);
        }
        menu.Items.Add(group);
    }
}
