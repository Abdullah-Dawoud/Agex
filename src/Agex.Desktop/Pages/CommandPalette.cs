using Agex.Desktop.Ui;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Agex.Desktop.Pages;

public sealed record PaletteCommand(string Title, string Icon, string Shortcut, Action Run);

/// <summary>Ctrl+K / Cmd+K: type to find a command, a project or a past session.</summary>
public sealed class CommandPalette : Grid
{
    private readonly MainWindow _window;
    private readonly TextBox _query = new() { PlaceholderText = "Type a command, project or past request..." };
    private readonly ListBox _results = new() { MaxHeight = 380 };
    private List<PaletteCommand> _items = [];

    public CommandPalette(MainWindow window)
    {
        _window = window;
        IsVisible = false;
        var scrim = new Border();
        scrim.Res(Border.BackgroundProperty, "OverlayBrush");
        scrim.PointerPressed += (_, _) => Close();
        AutomationProperties.SetName(_query, "Command search");
        _query.TextChanged += (_, _) => Filter();
        _query.KeyDown += OnKeyDown;
        _results.DoubleTapped += (_, _) => RunSelected();
        _results.KeyDown += OnKeyDown;
        var frame = Kit.Card(Kit.Column(8, _query, _results), 12);
        frame.MaxWidth = 600;
        frame.VerticalAlignment = VerticalAlignment.Top;
        frame.Margin = new Thickness(24, 90, 24, 24);
        frame.BoxShadow = BoxShadows.Parse("0 12 32 0 #33000000");
        Children.Add(scrim);
        Children.Add(frame);
    }

    public void Open()
    {
        var commands = _window.Commands().ToList();
        foreach (var path in _window.Workspace.Settings.RecentProjects.Take(6))
            commands.Add(new PaletteCommand("Open project: " + Path.GetFileName(path.TrimEnd('/', '\\')), Icons.Folder, "", () => _window.Workspace.OpenProject(path)));
        foreach (var session in _window.Workspace.Core.Sessions.List().Take(8))
            commands.Add(new PaletteCommand("Session: " + session.Title, Icons.History, "", () => { _window.Navigate("sessions"); _window.Page<SessionsPage>("sessions").Select(session.Id); }));
        _items = commands;
        _query.Text = "";
        Filter();
        IsVisible = true;
        _query.Focus();
    }

    public void Close() => IsVisible = false;

    private void Filter()
    {
        var query = (_query.Text ?? "").Trim();
        var matches = _items.Where(item => query.Length == 0 || item.Title.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(40).ToList();
        _results.ItemsSource = matches.Select(item =>
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10, Margin = new Thickness(8, 6), Tag = item };
            row.Children.Add(Kit.Icon(item.Icon, 16));
            var title = Kit.Text(item.Title, "body");
            Grid.SetColumn(title, 1);
            row.Children.Add(title);
            if (item.Shortcut.Length > 0) { var keys = Kit.Text(item.Shortcut, "caption"); Grid.SetColumn(keys, 2); row.Children.Add(keys); }
            AutomationProperties.SetName(row, item.Title);
            return row;
        }).ToList();
        _results.SelectedIndex = matches.Count > 0 ? 0 : -1;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Close(); e.Handled = true; break;
            case Key.Enter: RunSelected(); e.Handled = true; break;
            case Key.Down when sender == _query: _results.SelectedIndex = Math.Min(_results.ItemCount - 1, _results.SelectedIndex + 1); e.Handled = true; break;
            case Key.Up when sender == _query: _results.SelectedIndex = Math.Max(0, _results.SelectedIndex - 1); e.Handled = true; break;
        }
    }

    private void RunSelected()
    {
        if (_results.SelectedItem is Control { Tag: PaletteCommand command })
        {
            Close();
            command.Run();
        }
    }
}
