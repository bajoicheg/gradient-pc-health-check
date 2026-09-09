namespace Gradient.PcHealthCheck;

public sealed partial class MainForm
{
    private Control BuildHeader()
    {
        var panel = Card();
        panel.Margin = new Padding(0, 0, 0, 10);
        var logo = new ShieldLogo { Location = new Point(13, 7), Size = new Size(62, 66), BackColor = Color.White };
        panel.Controls.Add(logo);
        panel.Controls.Add(new Label
        {
            Text = "Gradient PC Health Check",
            Font = new Font("Segoe UI Semibold", 18F),
            ForeColor = Navy,
            AutoSize = true,
            Location = new Point(88, 12)
        });
        panel.Controls.Add(new Label
        {
            Text = "Service Desk · диагностика и контролируемые действия для Windows 11",
            ForeColor = Muted,
            AutoSize = true,
            Location = new Point(90, 49)
        });
        _hostLabel.Font = new Font("Segoe UI Semibold", 10F);
        _hostLabel.ForeColor = Navy;
        _hostLabel.AutoSize = true;
        _hostLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        panel.Controls.Add(_hostLabel);
        _scanTimeLabel.ForeColor = Muted;
        _scanTimeLabel.AutoSize = true;
        _scanTimeLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        panel.Controls.Add(_scanTimeLabel);
        panel.Resize += (_, _) =>
        {
            _hostLabel.Location = new Point(Math.Max(680, panel.ClientSize.Width - _hostLabel.Width - 18), 20);
            _scanTimeLabel.Location = new Point(Math.Max(680, panel.ClientSize.Width - _scanTimeLabel.Width - 18), 46);
        };
        return panel;
    }

    private Control BuildMetrics()
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Margin = new Padding(0, 0, 0, 10) };
        for (var i = 0; i < 5; i++) table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        table.Controls.Add(Metric("ИНДЕКС", _scoreLabel, _statusLabel), 0, 0);
        table.Controls.Add(Metric("CPU", _cpuLabel, Small("текущая загрузка")), 1, 0);
        table.Controls.Add(Metric("RAM", _ramLabel, Small("использовано")), 2, 0);
        table.Controls.Add(Metric("СИСТЕМНЫЙ ДИСК", _diskLabel, Small("свободно")), 3, 0);
        table.Controls.Add(Metric("UPTIME", _uptimeLabel, Small("с последней загрузки")), 4, 0);
        return table;
    }

    private Control BuildTabs()
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
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 300 };
        ConfigureGrid(_actionsGrid, allowEdit: true);
        _actionsGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _actionsGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "✓", Width = 42 });
        _actionsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Kind", HeaderText = "Тип", Width = 105, ReadOnly = true });
        _actionsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Title", HeaderText = "Действие", Width = 220, ReadOnly = true });
        _actionsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Reason", HeaderText = "Почему", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 260, ReadOnly = true });
        _actionsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Admin", HeaderText = "Admin", Width = 62, ReadOnly = true });
        _actionsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Risk", HeaderText = "Риск", Width = 75, ReadOnly = true });
        _actionsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Verify", HeaderText = "Автопроверка", Width = 270, ReadOnly = true });
        foreach (DataGridViewColumn column in _actionsGrid.Columns) column.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _actionsGrid.CellContentClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == 0) _actionsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        split.Panel1.Controls.Add(_actionsGrid);

        ConfigureGrid(_findingsGrid);
        _findingsGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _findingsGrid.Columns.Add("Severity", "Статус");
        _findingsGrid.Columns.Add("Category", "Категория");
        _findingsGrid.Columns.Add("Title", "Наблюдение");
        _findingsGrid.Columns.Add("Value", "Значение");
        _findingsGrid.Columns.Add("Recommendation", "Рекомендация");
        _findingsGrid.Columns[0].Width = 70;
        _findingsGrid.Columns[1].Width = 105;
        _findingsGrid.Columns[2].Width = 250;
        _findingsGrid.Columns[3].Width = 150;
        _findingsGrid.Columns[4].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        foreach (DataGridViewColumn column in _findingsGrid.Columns) column.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        split.Panel2.Controls.Add(_findingsGrid);
        page.Controls.Add(split);
        return page;
    }

    private TabPage ProcessesTab()
    {
        var page = Page("Процессы");
        var inner = new TabControl { Dock = DockStyle.Fill };
        inner.Controls.Add(ProcessPage("TOP CPU", _cpuGrid));
        inner.Controls.Add(ProcessPage("TOP RAM", _memoryGrid));
        inner.Controls.Add(ProcessPage("TOP I/O", _ioGrid));
        page.Controls.Add(inner);
        return page;
    }

    private TabPage EventsTab()
    {
        var page = Page("События Windows");
        ConfigureGrid(_eventsGrid);
        _eventsGrid.Columns.Add("Log", "Журнал");
        _eventsGrid.Columns.Add("Provider", "Provider");
        _eventsGrid.Columns.Add("Id", "Event ID");
        _eventsGrid.Columns.Add("Count", "Количество");
        _eventsGrid.Columns.Add("Last", "Последнее");
        _eventsGrid.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        page.Controls.Add(_eventsGrid);
        return page;
    }

    private TabPage SystemTab()
    {
        var page = Page("Система");
        ConfigureGrid(_systemGrid);
        _systemGrid.Columns.Add("Property", "Параметр");
        _systemGrid.Columns.Add("Value", "Значение");
        _systemGrid.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        page.Controls.Add(_systemGrid);
        return page;
    }

    private TabPage CompareTab()
    {
        var page = Page("До / после");
        page.Name = "compare";
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 245 };
        ConfigureGrid(_compareGrid);
        _compareGrid.Columns.Add("Metric", "Показатель");
        _compareGrid.Columns.Add("Before", "До");
        _compareGrid.Columns.Add("After", "После");
        _compareGrid.Columns.Add("Delta", "Изменение");
        _compareGrid.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        split.Panel1.Controls.Add(_compareGrid);
        ConfigureGrid(_remediationGrid);
        _remediationGrid.Columns.Add("Action", "Действие");
        _remediationGrid.Columns.Add("Result", "Результат");
        _remediationGrid.Columns.Add("Code", "Exit code");
        _remediationGrid.Columns.Add("Details", "Подробности");
        _remediationGrid.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        split.Panel2.Controls.Add(_remediationGrid);
        page.Controls.Add(split);
        return page;
    }

    private Control BuildFooter()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
        SetupButton(_scanButton, "Повторить диагностику", 0, primary: false);
        _scanButton.Width = 175;
        _scanButton.Click += async (_, _) => await RunScanAsync();
        panel.Controls.Add(_scanButton);
        SetupButton(_applyButton, "Применить выбранное", 185, primary: true);
        _applyButton.Width = 190;
        _applyButton.Click += async (_, _) => await ApplySelectedAsync();
        panel.Controls.Add(_applyButton);
        SetupButton(_reportButton, "Открыть отчёт", 385, primary: false);
        _reportButton.Width = 125;
        _reportButton.Click += (_, _) => OpenLatestReport();
        panel.Controls.Add(_reportButton);
        SetupButton(_folderButton, "Папка отчётов", 520, primary: false);
        _folderButton.Width = 125;
        _folderButton.Click += (_, _) => OpenReportsFolder();
        panel.Controls.Add(_folderButton);
        _progress.Style = ProgressBarStyle.Marquee;
        _progress.MarqueeAnimationSpeed = 25;
        _progress.Visible = false;
        _progress.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _progress.Size = new Size(180, 12);
        panel.Controls.Add(_progress);
        _statusBar.AutoSize = true;
        _statusBar.ForeColor = Muted;
        _statusBar.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        panel.Controls.Add(_statusBar);
        panel.Resize += (_, _) =>
        {
            _progress.Location = new Point(Math.Max(700, panel.ClientSize.Width - 190), 9);
            _statusBar.Location = new Point(Math.Max(650, panel.ClientSize.Width - _statusBar.Width - 10), 27);
        };
        return panel;
    }
}
