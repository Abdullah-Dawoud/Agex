using Avalonia.Controls;

namespace Agex.Desktop.Pages;

/// <summary>A top-level page. The view is built once and refreshed when shown.</summary>
public abstract class AppPage(MainWindow window)
{
    private Control? _view;
    protected MainWindow Window { get; } = window;
    protected Workspace Workspace => Window.Workspace;
    public abstract string Id { get; }
    public abstract string Title { get; }
    public abstract string Icon { get; }
    public Control View => _view ??= Build();
    protected abstract Control Build();
    public virtual void OnShown() { }
}
