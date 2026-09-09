namespace Gradient.PcHealthCheck;

public sealed partial class MainForm : Form
{
    private readonly DiagnosticsService _diagnostics = new();
    private readonly AssessmentService _assessment = new();
    private readonly ReportService _reports = new();

    private ScanResult? _current;
    private string? _latestReport;

    private readonly Label _scoreLabel = new();
    private readonly Label _statusLabel = new();
    private readonly Label _hostLabel = new();
    private readonly Label _cpuLabel = new();
    private readonly Label _ramLabel = new();
    private readonly Label _diskLabel = new();
    private readonly Label _uptimeLabel = new();
    private readonly Label _scanTimeLabel = new();
    private readonly Label _statusBar = new();
    private readonly ProgressBar _progress = new();
    private readonly DataGridView _actionsGrid = new();
    private readonly DataGridView _findingsGrid = new();
    private readonly DataGridView _cpuGrid = new();
    private readonly DataGridView _memoryGrid = new();
    private readonly DataGridView _ioGrid = new();
    private readonly DataGridView _eventsGrid = new();
    private readonly DataGridView _systemGrid = new();
    private readonly DataGridView _compareGrid = new();
    private readonly DataGridView _remediationGrid = new();
    private readonly Button _scanButton = new();
    private readonly Button _applyButton = new();
    private readonly Button _reportButton = new();
    private readonly Button _folderButton = new();
    private readonly TabControl _tabs = new();

    private static readonly Color Navy = Color.FromArgb(21, 52, 79);
    private static readonly Color Blue = Color.FromArgb(35, 134, 192);
    private static readonly Color Light = Color.FromArgb(244, 247, 250);
    private static readonly Color Border = Color.FromArgb(220, 229, 237);
    private static readonly Color Muted = Color.FromArgb(104, 122, 139);
    private static readonly Color Ok = Color.FromArgb(23, 122, 75);
    private static readonly Color Warn = Color.FromArgb(165, 106, 0);
    private static readonly Color Crit = Color.FromArgb(181, 54, 54);

    public MainForm()
    {
        Text = "Gradient PC Health Check";
        Width = 1320;
        Height = 840;
        MinimumSize = new Size(1120, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Light;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        var appIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath);
        if (appIcon is not null) Icon = appIcon;

        BuildUi();
        Shown += async (_, _) => await RunScanAsync();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 1,
            BackColor = Light,
            Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        Controls.Add(root);

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildMetrics(), 0, 1);
        root.Controls.Add(BuildTabs(), 0, 2);
        root.Controls.Add(BuildFooter(), 0, 3);
    }
}
