using System.Text;

namespace AutodeskAIBridge.Diagnostics;

public sealed class MainForm : Form
{
    private readonly TextBox _status = new();
    private readonly DiagnosticsScanner _scanner = new();

    public MainForm()
    {
        Text = "Autodesk AI Bridge Manager";
        Width = 720;
        Height = 460;
        MinimumSize = new Size(560, 360);
        StartPosition = FormStartPosition.CenterScreen;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);

        layout.Controls.Add(new Label { Text = "Autodesk AI Bridge", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
        _status.Multiline = true;
        _status.ReadOnly = true;
        _status.ScrollBars = ScrollBars.Vertical;
        _status.Dock = DockStyle.Fill;
        layout.Controls.Add(_status, 0, 1);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        buttons.Controls.Add(Button("Run Diagnostics", (_, _) => RefreshStatus()));
        buttons.Controls.Add(Button("Repair Integration", (_, _) => RepairIntegration()));
        buttons.Controls.Add(Button("Open Logs", (_, _) => OpenLogs()));
        buttons.Controls.Add(Button("Copy Diagnostics", (_, _) => CopyDiagnostics()));
        layout.Controls.Add(buttons, 0, 2);
        Shown += (_, _) => RefreshStatus();
    }

    private static Button Button(string text, EventHandler click)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += click;
        return button;
    }

    private void RefreshStatus()
    {
        var builder = new StringBuilder();
        var hostPath = Path.Combine(AppContext.BaseDirectory, "..", "Host", "AutodeskAIBridge.Host.exe");
        builder.AppendLine(IntegrationService.CreateDiagnosticReport(hostPath));
        builder.AppendLine();
        var products = _scanner.Scan();
        foreach (var item in products)
            builder.AppendLine($"{item.Product} {item.Version}: {(item.Installed ? (item.BridgeInstalled ? "Integration installed" : "Product detected") : "Not detected")} - {item.Detail}");
        if (!products.Any(item => item.Installed))
            builder.AppendLine("No supported Revit or AutoCAD installation was detected. Autodesk integrations can be repaired later.");
        builder.AppendLine();
        builder.AppendLine("Status: READY when Host, AntiGravity, and at least one Autodesk product show OK or Detected.");
        _status.Text = builder.ToString();
    }

    private void RepairIntegration()
    {
        var hostPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Host", "AutodeskAIBridge.Host.exe"));
        var result = IntegrationService.Configure(hostPath);
        MessageBox.Show(result.Error is null ? "Integration repaired. Restart AntiGravity and Autodesk applications if they are open." : result.Error, "Autodesk AI Bridge", MessageBoxButtons.OK, result.Error is null ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        RefreshStatus();
    }

    private static void OpenLogs()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutodeskAIBridge", "Logs");
        Directory.CreateDirectory(path);
        MessageBox.Show($"Logs directory:\n{path}", "Autodesk AI Bridge", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void CopyDiagnostics()
    {
        var hostPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Host", "AutodeskAIBridge.Host.exe"));
        Clipboard.SetText(IntegrationService.CreateDiagnosticReport(hostPath));
        MessageBox.Show("Diagnostics copied.", "Autodesk AI Bridge", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
