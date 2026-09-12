using System.Drawing.Drawing2D;

namespace G.PcHealthCheck;

internal sealed class PerformanceTimeline : Control
{
    private PerformanceSessionSnapshot? _snapshot;
    private SessionMetric _metric;
    public PerformanceTimeline()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.White;
        MinimumSize = new Size(400, 150);
        AccessibleName = "График измерений по времени";
    }

    public void Display(PerformanceSessionSnapshot snapshot, SessionMetric metric)
    {
        _snapshot = snapshot; _metric = metric; Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var snapshot = _snapshot;
        if (snapshot is null) { e.Graphics.DrawString("Запустите сеанс для наблюдения за нагрузкой.", Font, Brushes.DimGray, 14, 24); return; }
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(64, 28, Math.Max(1, Width - 92), Math.Max(1, Height - 62));
        var segments = PerformanceStatistics.Segments(snapshot, _metric);
        var xmax = Math.Max(1000d, Math.Max(snapshot.ElapsedMs, snapshot.Samples.Select(x => x.OffsetMs).DefaultIfEmpty(0).Max()));
        var ymax = _metric == SessionMetric.DiskQueue ? Math.Max(1, segments.SelectMany(x => x).Select(x => x.Value).DefaultIfEmpty(1).Max()) : 100;
        float X(long ms) => bounds.Left + (float)(ms / xmax * bounds.Width);
        float Y(double value) => bounds.Bottom - (float)(value / ymax * bounds.Height);
        using var line = new Pen(Color.FromArgb(35, 134, 192), 2);
        using var grid = new Pen(Color.FromArgb(220, 229, 237));
        using var markerPen = new Pen(Color.FromArgb(165, 106, 0)) { DashStyle = DashStyle.Dash };
        g.DrawString(PerformanceSessionReport.Name(_metric), Font, Brushes.Black, 10, 5);
        for (var i = 0; i <= 4; i++)
        {
            var y = Y(ymax * i / 4);
            g.DrawLine(grid, bounds.Left, y, bounds.Right, y);
            g.DrawString((ymax * i / 4).ToString("0.##"), Font, Brushes.DimGray, 4, y - Font.Height / 2f);
        }
        foreach (var marker in snapshot.Markers.Where(x => x.OffsetMs <= xmax))
            g.DrawLine(markerPen, X(marker.OffsetMs), bounds.Top, X(marker.OffsetMs), bounds.Bottom);
        foreach (var segment in segments)
        {
            var points = segment.Select(x => new PointF(X(x.OffsetMs), Y(x.Value))).ToArray();
            if (points.Length > 1) g.DrawLines(line, points);
            foreach (var point in points) g.FillEllipse(Brushes.SteelBlue, point.X - 2, point.Y - 2, 4, 4);
        }
        if (segments.Count == 0) g.DrawString("Нет доступных значений — это не нулевая нагрузка.", Font, Brushes.DimGray, bounds.Left + 12, bounds.Top + 20);
        g.DrawString("0 с", Font, Brushes.DimGray, bounds.Left, bounds.Bottom + 8);
        var end = $"{xmax / 1000:0.0} с";
        g.DrawString(end, Font, Brushes.DimGray, bounds.Right - g.MeasureString(end, Font).Width, bounds.Bottom + 8);
    }
}
