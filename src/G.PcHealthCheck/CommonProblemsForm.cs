using System.Text;

namespace G.PcHealthCheck;

internal sealed class CommonProblemsForm : Form
{
    private readonly CommonProblemsCollector _collector = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _detail = new();
    private readonly Label _status = new();
    private readonly ProgressBar _progress = new() { Style = ProgressBarStyle.Marquee, Width = 120, Visible = false };
    private readonly Button _scan = Button("Проверить / повторить");
    private readonly Button _cancel = Button("Отменить сбор");
    private readonly Button _settings = Button("Открыть параметры Windows");
    private readonly Button _copy = Button("Копировать сводку");
    private readonly Button _export = Button("Сохранить HTML / JSON");
    private readonly Button _dns = Button("DNS-кэш: отдельное действие");
    private CommonProblemSnapshot? _current;
    private CommonProblemSnapshot? _previous;
    private RemediationBatchResult? _lastCommand;
    private CancellationTokenSource? _scanCancellation;
    private bool _busy;
    private bool _commandRunning;

    public CommonProblemsForm()
    {
        Text = "G PC Health Check — типовые проблемы";
        Size = new Size(1180, 780); MinimumSize = new Size(880, 620);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 5 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);
        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 0, 0, 10),
            Text = "Дополнительные проверки сети, печати и устройств. Основной индекс здоровья не изменяется.\nСбор не выполняет исправлений. INFO — ограниченная информация; UNKNOWN — недостаток данных. Параметры Windows открываются для ручной работы инженера."
        }, 0, 0);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        toolbar.Controls.AddRange([_scan, _cancel, _settings, _copy, _export, _dns, _progress]);
        root.Controls.Add(toolbar, 0, 1);
        _grid.Dock = DockStyle.Fill; _grid.ReadOnly = true; _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false; _grid.AllowUserToDeleteRows = false; _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; _grid.MultiSelect = false;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(CommonProblemFinding.Status), HeaderText = "Статус", Width = 85 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(CommonProblemFinding.Title), HeaderText = "Проверка", Width = 320 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(CommonProblemFinding.Evidence), HeaderText = "Наблюдаемые данные", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 220 });
        _grid.SelectionChanged += (_, _) => ShowDetail();
        root.Controls.Add(_grid, 0, 2);
        _detail.Dock = DockStyle.Fill; _detail.Multiline = true; _detail.ReadOnly = true; _detail.ScrollBars = ScrollBars.Vertical;
        root.Controls.Add(_detail, 0, 3);
        _status.Dock = DockStyle.Fill; _status.AutoSize = true; _status.Text = "Готов к проверке.";
        root.Controls.Add(_status, 0, 4);
        _scan.Click += async (_, _) => await ScanAsync();
        _cancel.Click += (_, _) => { _scanCancellation?.Cancel(); _cancel.Enabled = false; _status.Text = "Запрошена отмена; ожидается возврат текущего поставщика WMI."; };
        _settings.Click += (_, _) =>
        {
            if (Selected() is not { } row) return;
            TryUi(() => CommonProblemTools.Open(row.Topic));
        };
        _copy.Click += (_, _) =>
        {
            var current = _current;
            if (current is not null) TryUi(() => Clipboard.SetText(CommonProblemsReport.Summary(current, _previous, _lastCommand)));
        };
        _export.Click += (_, _) => Export();
        _dns.Click += async (_, _) => await FlushDnsAsync();
        Shown += async (_, _) => await ScanAsync();
        FormClosing += (_, e) =>
        {
            if (_commandRunning)
            {
                e.Cancel = true;
                _status.Text = "Выполняется подтверждённая команда; закрытие доступно после получения результата.";
            }
            else _scanCancellation?.Cancel();
        };
        UpdateButtons();
    }

    private async Task ScanAsync()
    {
        if (_busy || IsDisposed) return;
        _scanCancellation = new CancellationTokenSource();
        var cancellation = _scanCancellation;
        _busy = true; UpdateButtons();
        try
        {
            var data = await _collector.CollectAsync(Progress(), cancellation.Token);
            if (IsDisposed || cancellation.IsCancellationRequested) return;
            _previous = _current; _current = data;
            _grid.DataSource = CommonProblemsAssessment.Assess(data);
            _status.Text = $"Снимок {data.CollectedAt:HH:mm:ss}. Повторная проверка не заменяет подтверждение симптома пользователем." + CommandStatus();
            ShowDetail();
        }
        catch (OperationCanceledException) { if (!IsDisposed) _status.Text = "Сбор отменён. Предыдущие результаты не заменены." + CommandStatus(); }
        catch (Exception ex) { if (!IsDisposed) _status.Text = "Не удалось завершить сбор: " + ex.Message + CommandStatus(); }
        finally
        {
            cancellation.Dispose(); _scanCancellation = null; _busy = false;
            if (!IsDisposed) UpdateButtons();
        }
    }

    private async Task FlushDnsAsync()
    {
        var current = _current;
        if (_busy || current is null || !CommonProblemsAssessment.CanOfferDnsFlush(current)) return;
        if (MessageBox.Show(this,
            "Используйте только при симптомах разрешения имён. Будет очищен локальный DNS-кэш; DNS-серверы, IP, proxy и VPN не меняются. Это не исправляет DHCP, отсутствие DNS или недоступность сервера. После команды будет повторно прочитана конфигурация, а обращение к проблемному ресурсу необходимо проверить отдельно. Продолжить?",
            "Подтвердите очистку DNS-кэша", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        _busy = true; _commandRunning = true; _lastCommand = null; UpdateButtons();
        var startedAt = DateTime.Now;
        try
        {
            _lastCommand = await RemediationWorker.ExecuteFromGuiAsync(
                [new ActionRecommendation { Id = "FlushDns", CanAutomate = true, RequiresAdmin = false }], 3, Progress());
        }
        catch (Exception ex)
        {
            _lastCommand = CommonProblemCommandResult.UnconfirmedDnsFlush(startedAt, DiagnosticsService.IsAdministrator(), ex);
        }
        finally
        {
            _busy = false; _commandRunning = false;
            if (!IsDisposed) { _status.Text = CommandStatus(); UpdateButtons(); }
        }
        if (!IsDisposed) await ScanAsync();
    }

    private string CommandStatus() => _lastCommand is null ? "" : " Последняя команда: " + string.Join("; ", _lastCommand.Actions.Select(x =>
        $"{x.Id}: {(x.Success ? "код выполнения успешный" : "ошибка / результат не подтверждён")}; {x.Message}"));
    private IProgress<string> Progress() => new Progress<string>(message => { if (!IsDisposed) _status.Text = message; });
    private CommonProblemFinding? Selected() => _grid.CurrentRow?.DataBoundItem as CommonProblemFinding;
    private void ShowDetail()
    {
        if (Selected() is { } row) _detail.Text = $"{row.Title}\r\n\r\nФакты:\r\n{row.Evidence.Replace("\n", "\r\n")}\r\n\r\nШаги инженера:\r\n{row.Resolution}";
        UpdateButtons();
    }
    private void UpdateButtons()
    {
        _scan.Enabled = !_busy;
        _cancel.Enabled = _busy && !_commandRunning && _scanCancellation is { IsCancellationRequested: false };
        _copy.Enabled = _export.Enabled = !_busy && _current is not null;
        _settings.Enabled = !_busy && Selected() is { } row && CommonProblemTools.UriForTopic(row.Topic) is not null;
        _dns.Enabled = !_busy && _current is not null && CommonProblemsAssessment.CanOfferDnsFlush(_current);
        _progress.Visible = _busy;
    }
    private void Export()
    {
        var current = _current;
        if (current is null || _busy) return;
        using var dialog = new FolderBrowserDialog { Description = "Выберите локальную папку. Отчёты содержат имена устройств/IP; не публикуйте их без проверки.", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        TryUi(() =>
        {
            var stem = Path.Combine(dialog.SelectedPath, $"CommonProblems_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");
            WriteNew(stem + ".json", CommonProblemsReport.Json(current, _previous, _lastCommand));
            WriteNew(stem + ".html", CommonProblemsReport.Html(current, _previous, _lastCommand));
            _status.Text = "Сохранены: " + stem + ".html и .json";
        });
    }
    private static void WriteNew(string path, string text)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(text);
    }
    private void TryUi(Action action)
    {
        try { action(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Действие не выполнено полностью", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    private static Button Button(string text) => new() { Text = text, AutoSize = true, Padding = new Padding(5, 2, 5, 2) };
}
