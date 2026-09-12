using System.Diagnostics;
using System.Text;

namespace G.PcHealthCheck;

internal sealed class ResourceProbeForm : Form
{
    private readonly ResourceProbeService _service;
    private readonly TextBox _host = new() { Name = "ProbeHost", Width = 280, PlaceholderText = "Имя узла или IP, без https://", MaxLength = 255 };
    private readonly NumericUpDown _port = new() { Name = "ProbePort", Minimum = 1, Maximum = 65535, Value = 443, Width = 85 };
    private readonly NumericUpDown _timeout = new() { Minimum = 1, Maximum = 10, Value = 3, Width = 55 };
    private readonly ComboBox _presets = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145 };
    private readonly CheckBox _consent = new() { Name = "ProbeConsent", AutoSize = true, Text = "Разрешаю DNS/TCP-обращения к указанной цели. Настройки сети не изменяются." };
    private readonly Button _run = MakeButton("Проверить / повторить", "ProbeRun");
    private readonly Button _cancel = MakeButton("Отменить", "ProbeCancel");
    private readonly Button _copy = MakeButton("Копировать сводку", "ProbeCopy");
    private readonly Button _export = MakeButton("Сохранить HTML / JSON", "ProbeExport");
    private readonly Label _validation = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly Label _snapshotLabel = new() { AutoSize = true, Dock = DockStyle.Fill, Text = "Проверка ещё не запускалась." };
    private readonly Label _status = new() { AutoSize = true };
    private readonly Label _elapsed = new() { AutoSize = true };
    private readonly ProgressBar _progress = new() { Width = 90, Height = 16, Style = ProgressBarStyle.Marquee, Visible = false };
    private readonly DataGridView _grid = new();
    private readonly TextBox _detail = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private readonly Stopwatch _watch = new();
    private ResourceProbeSnapshot? _current;
    private ResourceProbeSnapshot? _previous;
    private CancellationTokenSource? _cancellation;
    private bool _busy;

    public ResourceProbeForm() : this(new SystemResourceProbeNetwork()) { }
    public ResourceProbeForm(IResourceProbeNetwork network)
    {
        _service = new(network);
        Text = "G PC Health Check — доступность ресурса";
        Size = new Size(1150, 790); MinimumSize = new Size(900, 680); AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F); StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 8 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 5; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 45)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);
        root.Controls.Add(new Label { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 0, 0, 10), Text = ResourceProbeReport.Boundary }, 0, 0);
        var inputs = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        inputs.Controls.AddRange([Label("Узел:"), _host, Label("TCP-порт:"), _port, _presets, Label("TCP-тайм-аут, с:"), _timeout]);
        _presets.Items.AddRange(["Порт вручную", "HTTPS · 443", "HTTP · 80", "SMB · 445", "RDP · 3389", "SMTP · 25"]); _presets.SelectedIndex = 0;
        _presets.SelectedIndexChanged += (_, _) => { var ports = new[] { 0, 443, 80, 445, 3389, 25 }; if (_presets.SelectedIndex > 0) _port.Value = ports[_presets.SelectedIndex]; };
        root.Controls.Add(inputs, 0, 1); root.Controls.Add(_validation, 0, 2); root.Controls.Add(_consent, 0, 3);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        buttons.Controls.AddRange([_run, _cancel, _copy, _export]); root.Controls.Add(buttons, 0, 4);
        var results = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        results.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); results.RowStyles.Add(new RowStyle(SizeType.AutoSize)); results.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        results.Controls.Add(_snapshotLabel, 0, 0);
        _grid.Dock = DockStyle.Fill; _grid.ReadOnly = true; _grid.AllowUserToAddRows = false; _grid.AllowUserToDeleteRows = false; _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; _grid.MultiSelect = false; _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Этап", Width = 65 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Имя / удалённый адрес", Width = 250 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Результат", Width = 195 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "мс", Width = 85, ValueType = typeof(double), DefaultCellStyle = new DataGridViewCellStyle { Format = "0.##" } });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Исходный IP", Width = 150 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Код", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 100 });
        _grid.SelectionChanged += (_, _) => ShowDetail(); results.Controls.Add(_grid, 0, 1); root.Controls.Add(results, 0, 5); root.Controls.Add(_detail, 0, 6);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true }; footer.Controls.AddRange([_progress, _elapsed, _status]); root.Controls.Add(footer, 0, 7);
        _host.TextChanged += (_, _) => TargetChanged(); _port.ValueChanged += (_, _) => TargetChanged(); _timeout.ValueChanged += (_, _) => TargetChanged(); _consent.CheckedChanged += (_, _) => UpdateButtons();
        _run.Click += async (_, _) => await RunAsync();
        _cancel.Click += (_, _) => { _cancellation?.Cancel(); _cancel.Enabled = false; _status.Text = "Запрошена отмена текущей проверки…"; };
        _copy.Click += (_, _) => { if (_current is { } c) TryUi(() => Clipboard.SetText(ResourceProbeReport.Summary(c, _previous))); };
        _export.Click += (_, _) => Export();
        _timer.Tick += (_, _) => _elapsed.Text = $"{_watch.Elapsed.TotalSeconds:0.0} с";
        FormClosing += (_, _) => _cancellation?.Cancel(); FormClosed += (_, _) => _timer.Dispose();
        UpdateButtons(); // Intentionally no Shown/Load scan: opening makes no requests.
    }
    private void TargetChanged() { _consent.Checked = false; UpdateButtons(); }
    private void UpdateButtons()
    {
        var valid = false;
        try { var target = ResourceTargetParser.Parse(_host.Text, (int)_port.Value); _validation.Text = $"Цель: {target.Host}; TCP {target.Port}. DNS ≤5 с; до 8 адресов, TCP ≤{_timeout.Value} с на адрес. Предустановка порта не проверяет протокол."; valid = true; }
        catch (ArgumentException) { _validation.Text = "Укажите одно имя или IP. URL, пути и списки адресов не принимаются."; }
        _run.Enabled = !_busy && valid && _consent.Checked; _cancel.Enabled = _busy && _cancellation is { IsCancellationRequested: false };
        _host.Enabled = _port.Enabled = _presets.Enabled = _timeout.Enabled = _consent.Enabled = !_busy;
        _copy.Enabled = _export.Enabled = !_busy && _current is not null; _progress.Visible = _busy;
    }
    private async Task RunAsync()
    {
        if (_busy || !_consent.Checked) return;
        ResourceTarget target;
        try { target = ResourceTargetParser.Parse(_host.Text, (int)_port.Value); } catch (ArgumentException ex) { _status.Text = ex.Message; return; }
        var options = new ResourceProbeOptions(5000, (int)_timeout.Value * 1000, 8);
        _busy = true; _cancellation = new(); var cancellation = _cancellation; var started = DateTimeOffset.Now;
        _grid.Rows.Clear(); _detail.Clear(); _snapshotLabel.Text = $"Выполняется новая попытка: {target.Host}, TCP {target.Port}, начало {started:HH:mm:ss}.";
        _watch.Restart(); _timer.Start(); UpdateButtons();
        ResourceProbeSnapshot snapshot;
        try
        {
            var progress = new Progress<string>(text => { if (!IsDisposed && ReferenceEquals(_cancellation, cancellation) && !cancellation.IsCancellationRequested) _status.Text = text; });
            snapshot = await _service.RunAsync(target, options, progress, cancellation.Token);
        }
        catch (Exception ex)
        {
            snapshot = new ResourceProbeSnapshot { Target = target, Options = options, StartedAt = started, FinishedAt = DateTimeOffset.Now, Outcome = cancellation.IsCancellationRequested ? "Cancelled" : "Failed" };
            snapshot.Warnings.Add($"Проверка не завершена: {ex.GetType().Name} (0x{ex.HResult:X8}).");
        }
        finally
        {
            cancellation.Dispose(); _cancellation = null; _busy = false; _watch.Stop();
            if (!IsDisposed) _timer.Stop();
        }
        if (IsDisposed) return;
        _previous = _current; _current = snapshot;
        foreach (var step in snapshot.Steps)
        {
            var row = _grid.Rows.Add(step.Stage, step.Endpoint, ResourceProbeReport.OutcomeText(step.Outcome), step.ElapsedMs, step.LocalAddress, step.ErrorCode); _grid.Rows[row].Tag = step;
        }
        _snapshotLabel.Text = $"{snapshot.StartedAt:HH:mm:ss} — {snapshot.Target.Host} · TCP {snapshot.Target.Port}: {ResourceProbeReport.OutcomeText(snapshot.Outcome)}. Адреса: {string.Join(", ", snapshot.Addresses)}";
        _status.Text = ResourceProbeReport.OutcomeText(snapshot.Outcome) + ". Подробности — в сводке и отчёте.";
        _elapsed.Text = $"{_watch.Elapsed.TotalSeconds:0.0} с"; _consent.Checked = false; ShowDetail(); UpdateButtons();
    }
    private void ShowDetail()
    {
        if (_current is null) return;
        var step = _grid.CurrentRow?.Tag as ResourceProbeStep;
        _detail.Text = (step is null ? "" : $"{step.Stage} {step.Endpoint}\r\n{step.Detail}\r\n\r\n") + string.Join("\r\n", _current.Warnings) + "\r\n\r\n" + ResourceProbeReport.Boundary;
    }
    private void Export()
    {
        if (_busy || _current is not { } current) return;
        using var dialog = new FolderBrowserDialog { Description = "Отчёт содержит имя цели и IP-адреса. Не публикуйте его без проверки.", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) TryUi(() => _status.Text = "Отчёт сохранён: " + ResourceProbeExport.Save(current, _previous, dialog.SelectedPath));
    }
    private void TryUi(Action action) { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Действие не завершено", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
    private static Button MakeButton(string text, string name) => new() { Text = text, Name = name, AutoSize = true, Padding = new Padding(5, 2, 5, 2) };
    private static Label Label(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(4, 7, 4, 3) };
}

internal static class ResourceProbeMenu
{
    public static void Attach(Form main)
    {
        ArgumentNullException.ThrowIfNull(main); ReadOnlyReviewMenu.Attach(main);
        var menu = main.MainMenuStrip!;
        if (menu.Items.Find("ResourceProbe", true).Length > 0) return;
        var group = (ToolStripMenuItem)menu.Items.Find("ReadOnlyInspections", false).Single();
        var item = new ToolStripMenuItem("Проверить доступность ресурса (DNS/TCP)…") { Name = "ResourceProbe" };
        item.Click += (_, _) => { using var form = new ResourceProbeForm(); form.ShowDialog(main); }; group.DropDownItems.Add(item);
    }
}

internal static class ResourceProbeExport
{
    public static string Save(ResourceProbeSnapshot current, ResourceProbeSnapshot? previous, string parent)
    {
        var json = ResourceProbeReport.Json(current, previous); var html = ResourceProbeReport.Html(current, previous);
        var directory = Path.Combine(parent, $"G-PC-Resource_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"); Directory.CreateDirectory(directory);
        Write(Path.Combine(directory, "snapshot.json"), json); Write(Path.Combine(directory, "report.html"), html); return directory;
    }
    private static void Write(string path, string content)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false, true)); writer.Write(content);
    }
}
