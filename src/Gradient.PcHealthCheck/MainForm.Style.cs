using System.Drawing.Drawing2D;

namespace Gradient.PcHealthCheck;

public sealed partial class MainForm
{
    private static TabPage ProcessPage(string title, DataGridView grid)
    {
        var page = new TabPage(title) { BackColor = Color.White, Padding = new Padding(6) };
        ConfigureStandardGrid(grid);
        grid.Columns.Add("Process", "Процесс");
        grid.Columns.Add("Pid", "PID");
        grid.Columns.Add("Cpu", "CPU");
        grid.Columns.Add("Ram", "RAM");
        grid.Columns.Add("Io", "I/O");
        grid.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        page.Controls.Add(grid);
        return page;
    }

    private static Control MetricCard(string caption, Label main, Control sub)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Margin = new Padding(0, 0, 10, 0),
            Padding = new Padding(15)
        };
        panel.Paint += (_, e) => DrawBorder(e.Graphics, panel.ClientRectangle, 12);
        var cap = new Label
        {
            Text = caption,
            ForeColor = Muted,
            Font = new Font("Segoe UI Semibold", 8F),
            AutoSize = true,
            Location = new Point(15, 13)
        };
        panel.Controls.Add(cap);

        main.Font = new Font("Segoe UI Semibold", 21F);
        main.ForeColor = Navy;
        main.AutoSize = true;
        main.Location = new Point(13, 34);
        panel.Controls.Add(main);

        sub.Location = new Point(15, 76);
        panel.Controls.Add(sub);
        return panel;
    }

    private static Label Small(string text) => new()
    {
        Text = text,
        ForeColor = Muted,
        AutoSize = true,
        Font = new Font("Segoe UI", 8.5F)
    };

    private static void ConfigureActionsGrid(DataGridView grid)
    {
        ConfigureStandardGrid(grid);
        grid.ReadOnly = false;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Selected",
            HeaderText = "✓",
            Width = 42,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Kind", HeaderText = "Тип", Width = 105 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Action", HeaderText = "Действие", Width = 220 });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Reason",
            HeaderText = "Почему",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 260
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Admin", HeaderText = "Admin", Width = 62 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Risk", HeaderText = "Риск", Width = 78 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Verify", HeaderText = "Проверка", Width = 270 });
        foreach (DataGridViewColumn column in grid.Columns) column.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        for (var i = 1; i < grid.Columns.Count; i++) grid.Columns[i].ReadOnly = true;
    }

    private static void ConfigureFindingsGrid(DataGridView grid)
    {
        ConfigureStandardGrid(grid);
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        grid.Columns.Add("Severity", "Статус");
        grid.Columns.Add("Category", "Категория");
        grid.Columns.Add("Title", "Наблюдение");
        grid.Columns.Add("Value", "Значение");
        grid.Columns.Add("Recommendation", "Рекомендация");
        grid.Columns[0].Width = 70;
        grid.Columns[1].Width = 110;
        grid.Columns[2].Width = 250;
        grid.Columns[3].Width = 155;
        grid.Columns[4].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        foreach (DataGridViewColumn column in grid.Columns) column.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
    }

    private static void ConfigureStandardGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.None;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.ReadOnly = true;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.RowHeadersVisible = false;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(247, 250, 252);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Navy;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9F);
        grid.ColumnHeadersHeight = 34;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(225, 239, 248);
        grid.DefaultCellStyle.SelectionForeColor = Navy;
        grid.GridColor = Border;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
    }

    private static void StylePrimaryButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = Blue;
        button.ForeColor = Color.White;
        button.Font = new Font("Segoe UI Semibold", 9F);
        button.Cursor = Cursors.Hand;
    }

    private static void StyleSecondaryButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.BorderSize = 1;
        button.BackColor = Color.White;
        button.ForeColor = Navy;
        button.Font = new Font("Segoe UI Semibold", 9F);
        button.Cursor = Cursors.Hand;
    }

    private static void DrawBorder(Graphics graphics, Rectangle rect, int radius)
    {
        rect.Width -= 1;
        rect.Height -= 1;
        using var path = RoundedRect(rect, radius);
        using var pen = new Pen(Border, 1);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.DrawPath(pen, path);
    }

    private static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class ShieldLogo : Control
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var width = ClientSize.Width;
            var height = ClientSize.Height;

            PointF[] outer =
            [
                new(width * .5f, height * .03f), new(width * .94f, height * .18f),
                new(width * .90f, height * .58f), new(width * .76f, height * .78f),
                new(width * .50f, height * .97f), new(width * .24f, height * .78f),
                new(width * .10f, height * .58f), new(width * .06f, height * .18f)
            ];
            using var outerBrush = new SolidBrush(Navy);
            graphics.FillPolygon(outerBrush, outer);

            PointF[] inner =
            [
                new(width * .5f, height * .10f), new(width * .86f, height * .22f),
                new(width * .83f, height * .55f), new(width * .70f, height * .72f),
                new(width * .50f, height * .87f), new(width * .30f, height * .72f),
                new(width * .17f, height * .55f), new(width * .14f, height * .22f)
            ];
            using var innerBrush = new SolidBrush(Blue);
            graphics.FillPolygon(innerBrush, inner);

            using var font = new Font("Segoe UI", Math.Max(12, width * .48f), FontStyle.Bold, GraphicsUnit.Pixel);
            using var white = new SolidBrush(Color.White);
            var size = graphics.MeasureString("G", font);
            graphics.DrawString("G", font, white, (width - size.Width) / 2, height * .24f);

            using var pulse = new Pen(Color.FromArgb(180, 234, 255), Math.Max(2, width * .04f)) { LineJoin = LineJoin.Round };
            PointF[] points =
            [
                new(width * .26f, height * .70f), new(width * .39f, height * .70f),
                new(width * .45f, height * .62f), new(width * .51f, height * .80f),
                new(width * .59f, height * .66f), new(width * .65f, height * .70f),
                new(width * .76f, height * .70f)
            ];
            graphics.DrawLines(pulse, points);
        }
    }
}
