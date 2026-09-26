using Agex.Core.Orchestration;
using Agex.Core.Settings;
using Agex.Desktop.Ui;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;

namespace Agex.Desktop.Pages;

/// <summary>Approval mode and what agents may do, in plain words. Used in Home (composer) and Settings.</summary>
public static class PermissionsView
{
    public static (string Label, string Help, Func<PermissionSettings, bool> Get, Action<PermissionSettings, bool> Set)[] Items { get; } =
    [
        ("Read files", "Agents may open and read files in the project.", p => p.ReadFiles, (p, v) => p.ReadFiles = v),
        ("Write project files", "Agents may create and change files inside the project. AGEX saves a snapshot first when the project uses Git.", p => p.WriteProject, (p, v) => p.WriteProject = v),
        ("Run commands", "Agents may run programs such as tests, builds and local servers.", p => p.RunCommands, (p, v) => p.RunCommands = v),
        ("Browser", "Agents may open pages in a real browser, click and type there (your own local pages included).", p => p.Browser, (p, v) => p.Browser = v),
        ("Computer control", "An agent may see the screen and use the mouse and keyboard. You can take over or stop at any time.", p => p.ComputerControl, (p, v) => p.ComputerControl = v),
        ("Network", "Agents may use the internet and servers on this computer.", p => p.Network, (p, v) => p.Network = v),
        ("MCP tools", "Agents may use connected tools (browser, GitHub, documentation and others you connected).", p => p.McpTools, (p, v) => p.McpTools = v),
        ("External communication", "Agents may send emails or messages. Even when on, AGEX asks before each one.", p => p.ExternalCommunication, (p, v) => p.ExternalCommunication = v),
        ("Destructive actions", "Agents may delete significant data. Even when on, AGEX asks before each one.", p => p.DestructiveActions, (p, v) => p.DestructiveActions = v),
    ];

    /// <summary>The approval mode choice and the permission switches, saved as they change.</summary>
    public static Control Build(Workspace workspace, Action? changed = null)
    {
        var group = Guid.NewGuid().ToString("N");
        var modes = Kit.Column(6);
        foreach (var mode in new[] { ApprovalMode.AskEveryTime, ApprovalMode.Smart, ApprovalMode.TrustSession })
        {
            var description = Kit.Text(ApprovalRules.ModeDescription(mode), "caption");
            description.TextWrapping = TextWrapping.Wrap;
            var radio = new RadioButton { GroupName = group, IsChecked = workspace.Settings.Approvals.Mode == mode, Content = Kit.Column(0, Kit.Text(ApprovalRules.ModeLabel(mode), "body"), description) };
            var value = mode;
            radio.IsCheckedChanged += (_, _) => { if (radio.IsChecked == true) { workspace.SetApprovalMode(value); changed?.Invoke(); } };
            AutomationProperties.SetName(radio, ApprovalRules.ModeLabel(mode));
            modes.Children.Add(radio);
        }
        var switches = Kit.Column(4);
        foreach (var (label, help, get, set) in Items)
        {
            var toggle = new ToggleSwitch { IsChecked = get(workspace.Settings.Permissions), OnContent = "On", OffContent = "Off" };
            AutomationProperties.SetName(toggle, label);
            toggle.IsCheckedChanged += (_, _) => { set(workspace.Settings.Permissions, toggle.IsChecked == true); workspace.SaveSettings(); changed?.Invoke(); };
            switches.Children.Add(Kit.SettingRow(label, help, toggle));
        }
        var always = Kit.Text("Always confirmed, in every mode: payments, sending messages or emails, deleting significant data, account and security changes, publishing or submitting, and changing passwords, keys or tokens.", "caption");
        always.TextWrapping = TextWrapping.Wrap;
        return Kit.Column(10, Kit.Text("Approval mode", "subtitle"), modes, always, Kit.Divider(), Kit.Text("What agents may do", "subtitle"), switches);
    }

    public static async Task ShowAsync(MainWindow window, Action? changed = null) =>
        await window.Dialogs.ShowAsync("Approvals and permissions", new ScrollViewer { Content = Build(window.Workspace, changed), MaxHeight = 480 }, ["Done"], maxWidth: 640);
}
