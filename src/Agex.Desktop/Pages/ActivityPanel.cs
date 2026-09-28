using Agex.Core.Agents;
using Agex.Core.Orchestration;
using Agex.Core.Sessions;
using Agex.Desktop.Ui;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Agex.Desktop.Pages;

/// <summary>Right-hand panel: who is on the team and what each agent is doing right now.</summary>
public sealed class ActivityPanel : UserControl
{
    private readonly Workspace _workspace;
    private readonly StackPanel _plan = new() { Spacing = 7 };
    private readonly StackPanel _list = new() { Spacing = 8 };

    public ActivityPanel(Workspace workspace)
    {
        _workspace = workspace;
        Content = new ScrollViewer { Content = new StackPanel { Margin = new Thickness(16), Spacing = 12, Children = { _plan, Kit.Text("Team", "subtitle"), _list } } };
        workspace.AgentChanged += _ => RefreshSoon();
        workspace.Tasks.CollectionChanged += (_, _) => RefreshSoon();
        workspace.SessionChanged += RefreshSoon;
        workspace.ScanChanged += RefreshSoon;
        workspace.ProjectChanged += RefreshSoon;
        workspace.SettingsChanged += RefreshSoon;
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
        RefreshPlan();
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
            var privacy = member.Privacy switch
            {
                PrivacyKind.Local => Kit.Badge("Local", Tone.Success, Icons.Computer),
                PrivacyKind.Cloud => Kit.Badge("Cloud", Tone.Neutral, Icons.Cloud),
                PrivacyKind.Mixed => Kit.Badge("Local or cloud (depends on the model)", Tone.Warning, Icons.Cloud),
                _ => Kit.Badge("Data location unknown", Tone.Warning, Icons.Alert),
            };
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
            header.Children.Add(Kit.Avatar(member.Name, 28));
            var name = Kit.Column(0, Kit.Text(member.Name, "body"), Kit.Text(member.CanWrite ? "Can edit files" : member.CanReadFiles ? "Read-only" : "Text only", "caption"));
            Grid.SetColumn(name, 1);
            header.Children.Add(name);
            var badge = Kit.Badge(text, tone);
            Grid.SetColumn(badge, 2);
            header.Children.Add(badge);
            var detail = _workspace.Settings.LiveView != Agex.Core.Settings.LiveViewMode.Off && state is { Text.Length: > 0 } && state.State is not AgentWorkState.Idle ? Kit.Text((state.TaskId.Length > 0 ? state.TaskId.Replace("task-000", "#").Replace("task-00", "#") + ": " : "") + Agex.Core.Runtime.Redactor.RedactPaths(state.Text), "small") : null;
            if (detail is not null) detail.MaxLines = 3;
            _list.Children.Add(Kit.Panel(Kit.Column(6, header, detail, Kit.Row(6, privacy, health.Healthy ? null : Kit.Text(health.Reason, "caption")))));
        }
    }

    private void RefreshPlan()
    {
        _plan.Children.Clear();
        var tasks = _workspace.Tasks.ToList();
        if (tasks.Count == 0) { _plan.IsVisible = false; return; }
        _plan.IsVisible = true;
        var done = tasks.Count(task => task.State == TaskState.Done);
        _plan.Children.Add(Kit.Row(8, Kit.Text("Plan", "subtitle"), Kit.Text($"{done} of {tasks.Count} complete", "caption")));
        _plan.Children.Add(new ProgressBar { Minimum = 0, Maximum = tasks.Count, Value = done, Height = 5 });
        foreach (var task in tasks.Take(12))
        {
            var (status, tone) = HomePage.TaskLook(task.State);
            var agent = _workspace.Core.Registry.Get(task.Agent)?.Name ?? task.Agent;
            var model = _workspace.Members().FirstOrDefault(member => member.Id == task.Agent)?.Model;
            var title = Kit.Text(task.Label, "small");
            title.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
            title.MaxLines = 2;
            var dependencies = task.Dependencies.Count > 0 ? "After " + string.Join(", ", task.Dependencies.Select(id => id.Replace("task-", "#"))) : "Starts when ready";
            var detail = task.State switch
            {
                TaskState.Running or TaskState.Starting or TaskState.Verifying => task.Note.Length > 0 ? task.Note : "Working now",
                TaskState.Failed or TaskState.RepairRequired => task.Error,
                _ => dependencies,
            };
            var card = Kit.Panel(Kit.Column(4, Kit.Row(6, Kit.Badge(status, tone), title),
                Kit.Text(agent + (string.IsNullOrEmpty(model) ? " · Auto model" : " · " + model), "caption"),
                Kit.Text(detail, "caption")));
            _plan.Children.Add(card);
        }
        if (tasks.Count > 12) _plan.Children.Add(Kit.Text($"{tasks.Count - 12} more tasks in Agent Room", "caption"));
        _plan.Children.Add(Kit.Divider());
    }
}
