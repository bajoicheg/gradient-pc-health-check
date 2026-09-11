using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace G.PcHealthCheck;

public sealed class MainForm : Form
{
    private readonly DiagnosticsService _diagnostics = new();
    private readonly AssessmentService _assessment = new();
    private readonly ReportService _reports = new();
    private ScanResult? _current;
    private string? _latestReport;
    private bool _isBusy;

    private readonly Label _score = new();
    private readonly Label _state = new();
    private readonly Label _host = new();
    private readonly Label _cpu = new();
    private readonly Label _ram = new();
    private readonly Label _disk = new();
    private readonly Label _uptime = new();
    private readonly Label _coverage = new();
    private readonly Label _triageTitle = new();
    private readonly Label _triageDetail = new();
    private readonly Label _status = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _scan = new();
    private readonly Button _apply = new();
    private readonly Button _openReport = new();
    private readonly Button _openFolder = new();
    private readonly Button _copySummary = new();
    private readonly DataGridView _actions = new();
    private readonly DataGridView _findings = new();
    private readonly DataGridView _topCpu = new();
    private readonly DataGridView _topRam = new();
    private readonly DataGridView _topIo = new();
    private readonly DataGridView _events = new();
    private readonly DataGridView _system = new();
    private readonly DataGridView _compare = new();
    private readonly DataGridView _remediation = new();
    private readonly TabControl _tabs = new();

    private static readonly Color Navy = Color.FromArgb(21, 52, 79);
    private static readonly Color Blue = Color.FromArgb(35, 134, 192);
    private static readonly Color Bg = Color.FromArgb(244, 247, 250);
    private static readonly Color Line = Color.FromArgb(220, 229, 237);
    private static readonly Color Muted = Color.FromArgb(104, 122, 139);
    private static readonly Color Ok = Color.FromArgb(23, 122, 75);
    private static readonly Color Warn = Color.FromArgb(165, 106, 0);
    private static readonly Color Crit = Color.FromArgb(181, 54, 54);

    public MainForm()
    {
        Text = "G PC Health Check";
        Width = 1320;
        Height = 840;
        MinimumSize = new Size(1080, 680);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Bg;
        Font = new Font("Segoe UI", 9F);
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath); } catch { }
        BuildUi();
        UpdateApplyState();
        Shown += async (_, _) => await ScanAsync();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(16), BackColor = Bg };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        Controls.Add(root);
        root.Controls.Add(Header(), 0, 0);
        root.Controls.Add(Metrics(), 0, 1);
        root.Controls.Add(Tabs(), 0, 2);
        root.Controls.Add(Footer(), 0, 3);
    }

    private Control Header()
    {
        var p = Card();
        p.Margin = new Padding(0, 0, 0, 10);
        var logo = new PictureBox
        {
            Location = new Point(13, 8),
            Size = new Size(58, 64),
            BackColor = Color.White,
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = BrandAssets.LoadShield()
        };
        p.Controls.Add(logo);
        p.Controls.Add(new Label { Text = "G PC Health Check", Font = new Font("Segoe UI Semibold", 18F), ForeColor = Navy, AutoSize = true, Location = new Point(86, 13) });
        p.Controls.Add(new Label { Text = "Service Desk · диагностика и контролируемые действия для Windows 11", ForeColor = Muted, AutoSize = true, Location = new Point(88, 49) });
        _host.Font = new Font("Segoe UI Semibold", 10F); _host.ForeColor = Navy; _host.AutoSize = true; _host.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        p.Controls.Add(_host);
        p.Resize += (_, _) => _host.Location = new Point(Math.Max(650, p.ClientSize.Width - _host.Width - 18), 27);
        return p;
    }

    private Control Metrics()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, Margin = new Padding(0, 0, 0, 10) };
        for (var i = 0; i < 6; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 6F));
        t.Controls.Add(Metric("ИНДЕКС", _score, _state), 0, 0);
        t.Controls.Add(Metric("ПОКРЫТИЕ", _coverage, Small("полнота диагностики")), 1, 0);
        t.Controls.Add(Metric("CPU", _cpu, Small("текущая загрузка")), 2, 0);
        t.Controls.Add(Metric("RAM", _ram, Small("использовано")), 3, 0);
        t.Controls.Add(Metric("СИСТЕМНЫЙ ДИСК", _disk, Small("свободно")), 4, 0);
        t.Controls.Add(Metric("UPTIME", _uptime, Small("с последней загрузки")), 5, 0);
        return t;
    }

    private Control Tabs()
    {
        _tabs.Dock = DockStyle.Fill;
        _tabs.Padding = new Point(18, 7);
        _tabs.Controls.Add(RecommendationsTab());
        _tabs.Controls.Add(ProcessesTab());
        _tabs.Controls.Add(EventsTab());
        _tabs.Controls.Add(SystemTab());
        _tabs.Controls.Add(CompareTab());
        return _tabs;
    }

    private TabPage RecommendationsTab()
    {
        var page = Page("Рекомендации");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var triage = Card();
        triage.Margin = new Padding(0, 0, 0, 8);
        _triageTitle.Font = new Font("Segoe UI Semibold", 11F);
        _triageTitle.ForeColor = Navy;
        _triageTitle.AutoSize = true;
        _triageTitle.Location = new Point(14, 12);
        triage.Controls.Add(_triageTitle);
        _triageDetail.ForeColor = Muted;
        _triageDetail.AutoEllipsis = true;
        _triageDetail.Location = new Point(14, 39);
        _triageDetail.Size = new Size(900, 22);
        _triageDetail.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        triage.Controls.Add(_triageDetail);
        triage.Resize += (_, _) => _triageDetail.Width = Math.Max(100, triage.ClientSize.Width - 28);
        layout.Controls.Add(triage, 0, 0);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 300 };
        ConfigureGrid(_actions, false);
        _actions.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _actions.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "✓", Width = 42 });
        _actions.Columns.Add(new DataGridViewTextBoxColumn { Name = "Kind", HeaderText = "Тип", Width = 105, ReadOnly = true });
        _actions.Columns.Add(new DataGridViewTextBoxColumn { Name = "Title", HeaderText = "Действие", Width = 220, ReadOnly = true });
        _actions.Columns.Add(new DataGridViewTextBoxColumn { Name = "Reason", HeaderText = "Почему", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 260, ReadOnly = true });
        _actions.Columns.Add(new DataGridViewTextBoxColumn { Name = "Admin", HeaderText = "Admin", Width = 62, ReadOnly = true });
        _actions.Columns.Add(new DataGridViewTextBoxColumn { Name = "Risk", HeaderText = "Риск", Width = 75, ReadOnly = true });
        _actions.Columns.Add(new DataGridViewTextBoxColumn { Name = "Verify", HeaderText = "Автопроверка", Width = 270, ReadOnly = true });
        foreach (DataGridViewColumn c in _actions.Columns) c.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _actions.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_actions.IsCurrentCellDirty && _actions.CurrentCell is DataGridViewCheckBoxCell)
                _actions.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _actions.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == _actions.Columns["Selected"].Index) UpdateApplyState();
        };
        split.Panel1.Controls.Add(_actions);

        ConfigureGrid(_findings);
        _findings.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _findings.Columns.Add("Severity", "Статус");
        _findings.Columns.Add("Category", "Категория");
        _findings.Columns.Add("Title", "Наблюдение");
        _findings.Columns.Add("Value", "Значение");
        _findings.Columns.Add("Recommendation", "Рекомендация");
        _findings.Columns[0].Width = 70; _findings.Columns[1].Width = 105; _findings.Columns[2].Width = 250; _findings.Columns[3].Width = 150; _findings.Columns[4].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        foreach (DataGridViewColumn c in _findings.Columns) c.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        split.Panel2.Controls.Add(_findings);
        layout.Controls.Add(split, 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage ProcessesTab()
    {
        var page = Page("Процессы");
        var inner = new TabControl { Dock = DockStyle.Fill };
        inner.Controls.Add(ProcessPage("TOP CPU", _topCpu));
        inner.Controls.Add(ProcessPage("TOP RAM", _topRam));
        inner.Controls.Add(ProcessPage("TOP I/O", _topIo));
        page.Controls.Add(inner); return page;
    }

    private TabPage EventsTab()
    {
        var page = Page("События Windows");
        ConfigureGrid(_events);
        _events.Columns.Add(new DataGridViewTextBoxColumn { Name = "Log", HeaderText = "Журнал", ValueType = typeof(string) });
        _events.Columns.Add(new DataGridViewTextBoxColumn { Name = "Provider", HeaderText = "Provider", ValueType = typeof(string), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _events.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "Event ID", ValueType = typeof(int), SortMode = DataGridViewColumnSortMode.Automatic });
        _events.Columns.Add(new DataGridViewTextBoxColumn { Name = "Count", HeaderText = "Количество", ValueType = typeof(int), SortMode = DataGridViewColumnSortMode.Automatic });
        _events.Columns.Add(new DataGridViewTextBoxColumn { Name = "Last", HeaderText = "Последнее", ValueType = typeof(DateTime), SortMode = DataGridViewColumnSortMode.Automatic, DefaultCellStyle = new DataGridViewCellStyle { Format = "dd.MM HH:mm:ss", NullValue = "" } });
        page.Controls.Add(_events);
        return page;
    }

    private TabPage SystemTab()
    {
        var page = Page("Система"); ConfigureGrid(_system);
        _system.Columns.Add("Property", "Параметр"); _system.Columns.Add("Value", "Значение"); _system.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; page.Controls.Add(_system); return page;
    }

    private TabPage CompareTab()
    {
        var page = Page("До / после");
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 245 };
        ConfigureGrid(_compare); _compare.Columns.Add("Metric", "Показатель"); _compare.Columns.Add("Before", "До"); _compare.Columns.Add("After", "После"); _compare.Columns.Add("Delta", "Изменение"); _compare.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; split.Panel1.Controls.Add(_compare);
        ConfigureGrid(_remediation); _remediation.Columns.Add("Action", "Действие"); _remediation.Columns.Add("Result", "Результат"); _remediation.Columns.Add("Code", "Exit code"); _remediation.Columns.Add("Details", "Подробности"); _remediation.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; split.Panel2.Controls.Add(_remediation);
        page.Controls.Add(split); return page;
    }

    private Control Footer()
    {
        var p = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
        SetupButton(_scan, "Повторить диагностику", 0, false); _scan.Width = 175; _scan.Click += async (_, _) => await ScanAsync(); p.Controls.Add(_scan);
        SetupButton(_apply, "Применить выбранное", 185, true); _apply.Width = 205; _apply.Click += async (_, _) => await ApplyAsync(); p.Controls.Add(_apply);
        SetupButton(_openReport, "Открыть отчёт", 400, false); _openReport.Width = 125; _openReport.Click += (_, _) => OpenReport(); p.Controls.Add(_openReport);
        SetupButton(_openFolder, "Папка отчётов", 535, false); _openFolder.Width = 125; _openFolder.Click += (_, _) => OpenFolder(); p.Controls.Add(_openFolder);
        SetupButton(_copySummary, "Копировать сводку", 670, false); _copySummary.Width = 155; _copySummary.Click += (_, _) => CopySummary(); p.Controls.Add(_copySummary);
        _progress.Style = ProgressBarStyle.Marquee; _progress.MarqueeAnimationSpeed = 25; _progress.Visible = false; _progress.Anchor = AnchorStyles.Top | AnchorStyles.Right; _progress.Size = new Size(180, 12); p.Controls.Add(_progress);
        _status.AutoSize = true; _status.ForeColor = Muted; _status.Anchor = AnchorStyles.Top | AnchorStyles.Right; p.Controls.Add(_status);
        p.Resize += (_, _) => { _progress.Location = new Point(Math.Max(840, p.ClientSize.Width - 190), 9); _status.Location = new Point(Math.Max(790, p.ClientSize.Width - _status.Width - 10), 27); };
        return p;
    }

    private async Task ScanAsync()
    {
        var started = Stopwatch.StartNew();
        try
        {
            Busy(true, "Запускаю диагностику…");
            var progress = new Progress<string>(s => _status.Text = s);
            var data = await _diagnostics.CollectAsync(progress);
            _current = _assessment.Assess(data);
            var saved = _reports.SaveScan(_current);
            _latestReport = saved.Html;
            Populate(_current);
            _status.Text = $"Готово · {DateTime.Now:HH:mm:ss} · {started.Elapsed.TotalSeconds:0.0} с";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Ошибка диагностики", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { Busy(false); }
    }

    private async Task ApplyAsync()
    {
        if (_current is null) return;
        _actions.EndEdit();
        var selected = SelectedAutomatableActions();
        if (selected.Count == 0)
        {
            UpdateApplyState();
            return;
        }

        var summary = string.Join(Environment.NewLine, selected.Select(a => "• " + a.Title + (a.RequiresAdmin ? " [Admin]" : "")));
        if (MessageBox.Show(this, "Будут выполнены выбранные действия:\n\n" + summary + "\n\nПосле выполнения программа автоматически повторит диагностику.", "Подтвердите действия", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;

        var before = _current;
        try
        {
            Busy(true, "Выполняю выбранные действия…");
            var progress = new Progress<string>(s => _status.Text = s);
            var batch = await RemediationWorker.ExecuteFromGuiAsync(selected, _assessment.Thresholds.TempOlderThanDays, progress);
            _status.Text = "Повторная диагностика после remediation…";
            var afterData = await _diagnostics.CollectAsync(progress);
            var after = _assessment.Assess(afterData);
            var verification = new VerificationResult { Before = before, After = after, Remediation = batch };
            var saved = _reports.SaveVerification(verification);
            _latestReport = saved.Html;
            _current = after;
            Populate(after);
            PopulateVerification(verification);
            _tabs.SelectedIndex = 4;
            var ok = batch.Actions.Count(x => x.Success);
            MessageBox.Show(this, $"Выполнено: {ok}/{batch.Actions.Count}. Автопроверка завершена.\nИндекс: {before.Assessment.Score} → {after.Assessment.Score}.", "G PC Health Check", MessageBoxButtons.OK, batch.Actions.All(x => x.Success) ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (OperationCanceledException ex) { MessageBox.Show(this, ex.Message, "Операция отменена", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Ошибка remediation", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { Busy(false); }
    }

    private List<ActionRecommendation> SelectedAutomatableActions()
    {
        var selected = new List<ActionRecommendation>();
        foreach (DataGridViewRow row in _actions.Rows)
        {
            if (row.Tag is not ActionRecommendation action || !action.CanAutomate) continue;
            if (Convert.ToBoolean(row.Cells["Selected"].Value ?? false)) selected.Add(action);
        }
        return selected;
    }

    private void UpdateApplyState()
    {
        var count = SelectedAutomatableActions().Count;
        _apply.Text = count > 0 ? $"Применить выбранное ({count})" : "Применить выбранное";
        _apply.Enabled = !_isBusy && count > 0;
        _apply.Cursor = _apply.Enabled ? Cursors.Hand : Cursors.Default;
    }

    private void Populate(ScanResult scan)
    {
        var d = scan.Data;
        _score.Text = scan.Assessment.Score.ToString(); _score.ForeColor = scan.Assessment.Status == "OK" ? Ok : scan.Assessment.Status == "WARN" ? Warn : Crit;
        _state.Text = scan.Assessment.Status == "OK" ? "норма" : scan.Assessment.Status == "WARN" ? "внимание" : "критично"; _state.ForeColor = _score.ForeColor;
        _host.Text = $"{d.System.ComputerName} · {d.System.UserName}";
        _cpu.Text = F(d.Performance.CpuPercent, "%"); _ram.Text = F(d.Performance.MemoryUsedPercent, "%");
        var sd = d.LogicalDisks.FirstOrDefault(x => string.Equals(x.Drive, "C:", StringComparison.OrdinalIgnoreCase)); _disk.Text = sd is null ? "—" : $"{sd.FreeGB:0.#} GB";
        _uptime.Text = $"{d.System.UptimeDays:0.#} дн.";
        _coverage.Text = $"{scan.Assessment.CoveragePercent}%";
        _coverage.ForeColor = scan.Assessment.CoverageStatus == "HIGH" ? Ok : scan.Assessment.CoverageStatus == "MEDIUM" ? Warn : Crit;
        var triage = TriageSummary.Build(scan);
        _triageTitle.Text = triage.Title;
        _triageTitle.ForeColor = scan.Assessment.Status == "OK" ? Ok : scan.Assessment.Status == "WARN" ? Warn : Crit;
        _triageDetail.Text = triage.Detail;
        _tabs.TabPages[0].Text = triage.SignificantCount > 0 ? $"Рекомендации ({triage.SignificantCount})" : "Рекомендации";

        _actions.Rows.Clear();
        foreach (var a in scan.Actions)
        {
            var i = _actions.Rows.Add(a.Preselected, a.Kind, a.Title, a.Reason, a.RequiresAdmin ? "Да" : "Нет", a.Risk, a.Verification);
            var row = _actions.Rows[i]; row.Tag = a;
            if (!a.CanAutomate)
            {
                row.Cells[0].ReadOnly = true;
                row.Cells[0].Style.BackColor = Color.FromArgb(240, 243, 246);
                row.Cells[0].Value = false;
                row.DefaultCellStyle.ForeColor = Color.FromArgb(85, 98, 110);
            }
            else if (a.Kind == "Рекомендуется")
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(240, 249, 244);
            }
        }
        UpdateApplyState();

        _findings.Rows.Clear();
        foreach (var f in scan.Assessment.Findings.OrderBy(x => x.Severity == "CRIT" ? 0 : x.Severity == "WARN" ? 1 : 2))
        {
            var i = _findings.Rows.Add(f.Severity, f.Category, f.Title, f.Value, f.Recommendation);
            var row = _findings.Rows[i];
            row.Cells[0].Style.ForeColor = f.Severity == "CRIT" ? Crit : f.Severity == "WARN" ? Warn : Ok;
            row.Cells[0].Style.Font = new Font(Font, FontStyle.Bold);
            if (f.Severity == "CRIT") row.DefaultCellStyle.BackColor = Color.FromArgb(255, 242, 242);
            else if (f.Severity == "WARN") row.DefaultCellStyle.BackColor = Color.FromArgb(255, 249, 235);
        }

        FillProcesses(_topCpu, d.TopCpu); FillProcesses(_topRam, d.TopMemory); FillProcesses(_topIo, d.TopIo);
        _events.Rows.Clear();
        foreach (var e in d.Events.Top) _events.Rows.Add(e.Log, e.Provider, e.EventId, e.Count, e.LastSeen);
        PopulateSystem(scan);
    }

    private void PopulateSystem(ScanResult scan)
    {
        var d = scan.Data;
        _system.Rows.Clear(); void Add(string k, string v) => _system.Rows.Add(k, v);
        Add("Компьютер", d.System.ComputerName); Add("Пользователь", d.System.UserName); Add("Производитель / модель", (d.System.Manufacturer + " " + d.System.Model).Trim());
        Add("Windows", $"{d.System.OS} {d.System.OSVersion} build {d.System.BuildNumber}"); Add("CPU", d.System.Cpu); Add("RAM", $"{d.System.TotalMemoryGB:0.#} GB");
        Add("Последняя загрузка", d.System.LastBoot.ToString("dd.MM.yyyy HH:mm:ss")); Add("Права процесса", d.System.IsAdministrator ? "Administrator" : "Standard user");
        Add("Покрытие диагностики", $"{scan.Assessment.CoveragePercent}% ({scan.Assessment.CoverageStatus})");
        Add("Недоступные сигналы", scan.Assessment.MissingSignals.Count == 0 ? "Нет" : string.Join("; ", scan.Assessment.MissingSignals));
        Add("Pending reboot", d.PendingReboot.Pending ? "Да — " + string.Join("; ", d.PendingReboot.Reasons) : "Нет"); Add("Windows Update", $"wuauserv={d.Updates.Wuauserv}; BITS={d.Updates.Bits}");
        Add("Защита", d.SecurityProducts.Count == 0 ? "Не удалось определить" : string.Join("; ", d.SecurityProducts.Select(x => $"{x.Name} [{x.State}]")));
        Add("Физические диски", d.PhysicalDisks.Count == 0 ? "Не удалось определить" : string.Join("; ", d.PhysicalDisks.Select(x => $"{x.Name}: {x.HealthStatus}")));
        Add("Автозагрузка", d.StartupItems.Count + " элементов"); if (d.CollectionWarnings.Count > 0) Add("Предупреждения сбора", string.Join(" | ", d.CollectionWarnings));
    }

    private void PopulateVerification(VerificationResult v)
    {
        _compare.Rows.Clear(); foreach (var r in ReportService.CompareRows(v.Before, v.After)) _compare.Rows.Add(r.Name, r.Before, r.After, r.Delta);
        _remediation.Rows.Clear(); foreach (var r in v.Remediation.Actions) { var i = _remediation.Rows.Add(r.Id, r.Success ? "Успешно" : "Ошибка", r.ExitCode?.ToString() ?? "—", r.Message); _remediation.Rows[i].Cells[1].Style.ForeColor = r.Success ? Ok : Crit; }
    }

    private void CopySummary()
    {
        if (_current is null) return;
        try
        {
            Clipboard.SetText(SupportSummary.Build(_current));
            _status.Text = $"Сводка скопирована · {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Не удалось скопировать сводку", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenReport()
    {
        if (string.IsNullOrWhiteSpace(_latestReport) || !File.Exists(_latestReport)) { MessageBox.Show(this, "Отчёт ещё не сформирован."); return; }
        try { Process.Start(new ProcessStartInfo(_latestReport) { UseShellExecute = true }); } catch (Exception ex) { MessageBox.Show(this, ex.Message); }
    }

    private void OpenFolder()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", _reports.ReportsDirectory) { UseShellExecute = true }); } catch (Exception ex) { MessageBox.Show(this, ex.Message); }
    }

    private void Busy(bool value, string? text = null)
    {
        _isBusy = value;
        _scan.Enabled = !value;
        _openReport.Enabled = !value;
        _openFolder.Enabled = !value;
        _copySummary.Enabled = !value && _current is not null;
        _progress.Visible = value;
        if (text is not null) _status.Text = text;
        // Do not switch the form-wide cursor for background work. DataGridView/native child
        // handles can retain an inherited busy cursor after async work has completed.
        UpdateApplyState();
    }

    private static void FillProcesses(DataGridView g, IEnumerable<ProcessInfo> items)
    {
        g.Rows.Clear();
        foreach (var p in items) g.Rows.Add(p.Name, p.Pid, p.CpuPercent, p.MemoryMB, p.IoMBPerSec);
    }

    private static TabPage ProcessPage(string title, DataGridView grid)
    {
        var p = Page(title);
        ConfigureGrid(grid);
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Process", HeaderText = "Процесс", ValueType = typeof(string), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Pid", HeaderText = "PID", ValueType = typeof(int), Width = 80, SortMode = DataGridViewColumnSortMode.Automatic });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Cpu", HeaderText = "CPU", ValueType = typeof(double), Width = 90, SortMode = DataGridViewColumnSortMode.Automatic, DefaultCellStyle = new DataGridViewCellStyle { Format = "0.#'%'", Alignment = DataGridViewContentAlignment.MiddleRight } });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ram", HeaderText = "RAM", ValueType = typeof(double), Width = 110, SortMode = DataGridViewColumnSortMode.Automatic, DefaultCellStyle = new DataGridViewCellStyle { Format = "0.# 'MB'", Alignment = DataGridViewContentAlignment.MiddleRight } });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Io", HeaderText = "I/O", ValueType = typeof(double), Width = 110, SortMode = DataGridViewColumnSortMode.Automatic, DefaultCellStyle = new DataGridViewCellStyle { Format = "0.## 'MB/s'", Alignment = DataGridViewContentAlignment.MiddleRight } });
        p.Controls.Add(grid);
        return p;
    }

    private static TabPage Page(string text) => new(text) { BackColor = Color.White, Padding = new Padding(8) };
    private static Label Small(string text) => new() { Text = text, ForeColor = Muted, AutoSize = true, Font = new Font("Segoe UI", 8.5F) };
    private static string F(double? x, string suffix) => x is null ? "—" : $"{x:0.#}{suffix}";

    private static Panel Card()
    {
        var p = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(14) };
        p.Paint += (_, e) => { var r = p.ClientRectangle; r.Width--; r.Height--; using var path = Rounded(r, 12); using var pen = new Pen(Line); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.DrawPath(pen, path); };
        return p;
    }

    private static Control Metric(string caption, Label value, Control sub)
    {
        var p = Card(); p.Margin = new Padding(0, 0, 10, 0); p.Controls.Add(new Label { Text = caption, ForeColor = Muted, Font = new Font("Segoe UI Semibold", 8F), AutoSize = true, Location = new Point(14, 12) });
        value.Font = new Font("Segoe UI Semibold", 21F); value.ForeColor = Navy; value.AutoSize = true; value.Location = new Point(12, 33); p.Controls.Add(value); sub.Location = new Point(14, 74); p.Controls.Add(sub); return p;
    }

    private static void ConfigureGrid(DataGridView g, bool readOnly = true)
    {
        g.Dock = DockStyle.Fill; g.BackgroundColor = Color.White; g.BorderStyle = BorderStyle.None; g.AllowUserToAddRows = false; g.AllowUserToDeleteRows = false; g.AllowUserToResizeRows = false; g.ReadOnly = readOnly; g.SelectionMode = DataGridViewSelectionMode.FullRowSelect; g.MultiSelect = false; g.RowHeadersVisible = false; g.EnableHeadersVisualStyles = false; g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(247, 250, 252); g.ColumnHeadersDefaultCellStyle.ForeColor = Navy; g.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9F); g.ColumnHeadersHeight = 34; g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(225, 239, 248); g.DefaultCellStyle.SelectionForeColor = Navy; g.GridColor = Line; g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
    }

    private static void SetupButton(Button b, string text, int x, bool primary)
    {
        b.Text = text; b.Location = new Point(x, 8); b.Height = 34; b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = primary ? 0 : 1; b.FlatAppearance.BorderColor = Line; b.BackColor = primary ? Blue : Color.White; b.ForeColor = primary ? Color.White : Navy; b.Font = new Font("Segoe UI Semibold", 9F); b.Cursor = Cursors.Hand;
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var d = radius * 2; var p = new GraphicsPath(); p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90); p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
    }

    private sealed class ShieldLogo : Control
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var w = ClientSize.Width;
            var h = ClientSize.Height;
            var outer = new PointF[] { new(w*.5f,h*.03f),new(w*.94f,h*.18f),new(w*.90f,h*.58f),new(w*.76f,h*.78f),new(w*.5f,h*.97f),new(w*.24f,h*.78f),new(w*.10f,h*.58f),new(w*.06f,h*.18f) };
            using var ob = new SolidBrush(Navy); g.FillPolygon(ob, outer);
            var inner = new PointF[] { new(w*.5f,h*.10f),new(w*.86f,h*.22f),new(w*.83f,h*.55f),new(w*.70f,h*.72f),new(w*.5f,h*.87f),new(w*.30f,h*.72f),new(w*.17f,h*.55f),new(w*.14f,h*.22f) };
            using var ib = new SolidBrush(Blue); g.FillPolygon(ib, inner);
            using var f = new Font("Segoe UI", Math.Max(12, w*.48f), FontStyle.Bold, GraphicsUnit.Pixel);
            using var wb = new SolidBrush(Color.White);
            var sz = g.MeasureString("G", f);
            g.DrawString("G", f, wb, (w-sz.Width)/2, h*.24f);
        }
    }
}
