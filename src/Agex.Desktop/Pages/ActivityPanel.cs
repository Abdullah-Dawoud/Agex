using Agex.Core.Agents;
using Agex.Core.Orchestration;
using Agex.Desktop.Ui;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Agex.Desktop.Pages;

/// <summary>Right-hand panel: who is on the team and what each agent is doing right now.</summary>
public sealed class ActivityPanel : UserControl
{
    private readonly Workspace _workspace;
    private readonly StackPanel _list = new() { Spacing = 8 };

    public ActivityPanel(Workspace workspace)
    {
        _workspace = workspace;
        Content = new ScrollViewer { Content = new StackPanel { Margin = new Thickness(16), Spacing = 12, Children = { Kit.Text("Team", "subtitle"), _list } } };
        workspace.AgentChanged += _ => RefreshSoon();
        workspace.SessionChanged += RefreshSoon;
        workspace.ScanChanged += RefreshSoon;
        workspace.ProjectChanged += RefreshSoon;
        Refresh();
    }

    private bool _pending;

    /// <summary>Coalesces bursts of agent events into one redraw (at most 4 per second).</summary>
    private void RefreshSoon()
    {
        if (_pending) return;
        _pending = true;
        Avalonia.Threading.DispatcherTimer.RunOnce(() => { _pending = false; Refresh(); }, TimeSpan.FromMilliseconds(250));
    }

    public void Refresh()
    {
        _list.Children.Clear();
        var members = _workspace.Members();
        if (members.Count == 0)
        {
            _list.Children.Add(Kit.Text("No agent is ready yet. Open Agents to enable one.", "small"));
            return;
        }
        foreach (var member in members)
        {
            _workspace.AgentStates.TryGetValue(member.Id, out var state);
            var health = _workspace.Core.Registry.Health(member.Id);
            var (text, tone) = state?.State switch
            {
                AgentWorkState.Planning => ("Planning", Tone.Accent),
                AgentWorkState.Reviewing => ("Reviewing", Tone.Accent),
                AgentWorkState.Working => ("Working", Tone.Accent),
                AgentWorkState.Answering => ("Answering", Tone.Accent),
                AgentWorkState.Done => ("Finished", Tone.Success),
                AgentWorkState.Failed => ("Problem", Tone.Danger),
                AgentWorkState.Cancelled => ("Stopped", Tone.Neutral),
                _ when !health.Healthy => ("Paused (errors)", Tone.Warning),
                _ => ("Idle", Tone.Neutral),
            };
            var privacy = member.Privacy == PrivacyKind.Local ? Kit.Badge("Local", Tone.Success, Icons.Computer) : Kit.Badge("Cloud", Tone.Neutral, Icons.Cloud);
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
            header.Children.Add(Kit.Avatar(member.Name, 28));
            var name = Kit.Column(0, Kit.Text(member.Name, "body"), Kit.Text(member.CanWrite ? "Can edit files" : member.CanReadFiles ? "Read-only" : "Text only", "caption"));
            Grid.SetColumn(name, 1);
            header.Children.Add(name);
            var badge = Kit.Badge(text, tone);
            Grid.SetColumn(badge, 2);
            header.Children.Add(badge);
            var detail = state is { Text.Length: > 0 } && state.State is not AgentWorkState.Idle ? Kit.Text((state.TaskId.Length > 0 ? state.TaskId.Replace("task-000", "#").Replace("task-00", "#") + ": " : "") + Agex.Core.Runtime.Redactor.RedactPaths(state.Text), "small") : null;
            if (detail is not null) detail.MaxLines = 3;
            _list.Children.Add(Kit.Panel(Kit.Column(6, header, detail, Kit.Row(6, privacy, health.Healthy ? null : Kit.Text(health.Reason, "caption")))));
        }
    }
}
