namespace G.PcHealthCheck;

public sealed partial class MainForm
{
    private ExecutionContextInfo? _executionContext;
    private readonly Label _contextBanner = new() { Name = "ExecutionContextBanner", AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(8) };
    private readonly Button _contextDetails = new() { Text = "Права и доступные действия…", AutoSize = true, Dock = DockStyle.Fill };

    private Control ExecutionContextPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 8) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.Controls.Add(_contextBanner, 0, 0); panel.Controls.Add(_contextDetails, 1, 0);
        _contextDetails.Click += (_, _) => { using var form = new ExecutionContextForm(_executionContext); form.ShowDialog(this); };
        RenderExecutionContext(null);
        return panel;
    }

    private void RenderExecutionContext(ExecutionContextInfo? context)
    {
        _executionContext = context;
        _contextBanner.Text = context is null ? "Права и пользователь сеанса: сведения ещё не получены."
            : ExecutionPolicy.ModeText(context) + "\nПроцесс: " + ExecutionPolicy.Value(context.ProcessAccount)
              + " · Сеанс " + context.SessionId + ": " + ExecutionPolicy.Value(context.SessionAccount);
        _contextBanner.ForeColor = context is null || !ExecutionPolicy.SameUser(context) ? Warn : Navy;
    }

    private ActionAvailability AvailabilityFor(ActionRecommendation action)
        => !action.CanAutomate ? new("Manual", "Ручная рекомендация; программа её не выполняет.", "—")
            : ExecutionPolicy.For(action.Id, _executionContext ?? new ExecutionContextInfo());

    private void RefreshActionAvailability()
    {
        foreach (DataGridViewRow row in _actions.Rows)
        {
            if (row.Tag is not ActionRecommendation action) continue;
            var availability = AvailabilityFor(action);
            row.Cells["Availability"].Value = ExecutionPolicy.StateText(availability.State);
            row.Cells["Availability"].ToolTipText = availability.Reason + "\nОбласть: " + availability.Scope;
            row.Cells["Selected"].ReadOnly = !availability.CanRequest;
            if (!availability.CanRequest) row.Cells["Selected"].Value = false;
        }
        UpdateApplyState();
    }

    internal static void StampExecutionContext(DiagnosticData data, ExecutionContextInfo context)
    {
        data.System.ExecutionContext = context;
        // Never relabel a console user's WMI value as the current RDP session user.
        data.System.UserName = ExecutionPolicy.Value(context.SessionAccount);
        data.System.IsAdministrator = context.HasAdministratorToken == true;
    }

    private static string ActionExecutionText(RemediationActionResult action)
        => action.Message + "\nОбласть: " + ExecutionPolicy.Value(action.TargetScope)
            + "\n" + ExecutionPolicy.Describe(action.ExecutionContext);
}
