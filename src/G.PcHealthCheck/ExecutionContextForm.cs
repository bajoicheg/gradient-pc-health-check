namespace G.PcHealthCheck;

// This is a context/availability view, not an elevated repair launcher.
internal sealed class ExecutionContextForm : Form
{
    private readonly TextBox _facts = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
    private readonly DataGridView _matrix = new() { Name = "Availability", Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells };
    private readonly TextBox _detail = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };

    public ExecutionContextForm(ExecutionContextInfo? context)
    {
        Text = "G PC Health Check — права и контекст";
        Size = new Size(1080, 780); MinimumSize = new Size(820, 620);
        StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root); root.Controls.Add(_facts, 0, 0); root.Controls.Add(_matrix, 0, 1); root.Controls.Add(_detail, 0, 2);
        _facts.Text = ExecutionPolicy.Describe(context);
        _matrix.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _matrix.Columns.Add(new DataGridViewTextBoxColumn { Name = "Operation", HeaderText = "Проверка / действие", Width = 240 });
        _matrix.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", HeaderText = "Доступность", Width = 155 });
        _matrix.Columns.Add(new DataGridViewTextBoxColumn { Name = "Scope", HeaderText = "Чьи данные / область", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        foreach (var (id, title) in new[]
        {
            ("Diagnostics", "Диагностика компьютера"), ("TempPreview", "Предпросмотр Temp пользователя сеанса"),
            ("StartupReview", "Автозагрузка аккаунта процесса"), ("CleanTemp", "Удаление старых файлов Temp"),
            ("FlushDns", "Очистка DNS-кэша"), ("Dism", "DISM RestoreHealth"), ("Sfc", "SFC /scannow")
        })
        {
            var availability = ExecutionPolicy.For(id, context ?? new ExecutionContextInfo());
            var row = _matrix.Rows[_matrix.Rows.Add(title, ExecutionPolicy.StateText(availability.State), availability.Scope)];
            row.Tag = availability;
        }
        _matrix.SelectionChanged += (_, _) => ShowDetail();
        var copy = new Button { Text = "Копировать сведения", AutoSize = true };
        copy.Click += (_, _) =>
        {
            try
            {
                var lines = _matrix.Rows.Cast<DataGridViewRow>().Select(x => $"{x.Cells[0].Value}: {x.Cells[1].Value}; {x.Cells[2].Value}\r\n{((ActionAvailability)x.Tag!).Reason}");
                Clipboard.SetText(_facts.Text + "\r\n\r\n" + string.Join("\r\n", lines));
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Копирование не выполнено", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        root.Controls.Add(copy, 0, 3); ShowDetail();
    }
    private void ShowDetail() => _detail.Text = (_matrix.CurrentRow?.Tag as ActionAvailability)?.Reason
        ?? "Выберите строку. Доступность показана по сохранённому контексту; перед применением программа проверяет его заново. Политики Windows могут отклонить запрос UAC или доступ к данным.";
}
