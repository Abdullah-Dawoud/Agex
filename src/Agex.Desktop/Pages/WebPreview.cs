using Agex.Desktop.Ui;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;

namespace Agex.Desktop.Pages;

/// <summary>
/// Embedded web preview for HTML files and local dev servers, using the
/// system web engine (WebView2 on Windows, WKWebView on macOS, WebKitGTK on
/// Linux). Only files inside the previewed folder and addresses on this
/// computer load; any other navigation is blocked and offered as "Open
/// externally", so a previewed page can never browse the web inside AGEX.
/// </summary>
public sealed class WebPreview : UserControl
{
    private readonly MainWindow _window;
    private readonly Border _host = new() { MinHeight = 320 };
    private readonly TextBlock _status = Kit.Text("", "caption");
    private readonly TextBox _address = new() { PlaceholderText = "http://localhost:5173", MinWidth = 160 };
    private NativeWebView? _view;
    private Uri? _current;
    private string? _root;

    public WebPreview(MainWindow window)
    {
        _window = window;
        Avalonia.Automation.AutomationProperties.SetName(_address, "Preview address");
        _address.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) GoToAddress(); };
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 4 };
        bar.Children.Add(_address);
        var buttons = Kit.Row(0,
            Kit.IconButton(Icons.Send, "Open this address in the preview", GoToAddress),
            Kit.IconButton(Icons.Refresh, "Reload", () => { if (_current is { } uri) Show(uri, _root); }),
            Kit.IconButton(Icons.External, "Open in your browser", OpenExternally));
        Grid.SetColumn(buttons, 1);
        bar.Children.Add(buttons);
        _status.TextWrapping = TextWrapping.Wrap;
        var layout = new DockPanel();
        var top = Kit.Column(4, bar, _status);
        DockPanel.SetDock(top, Dock.Top);
        layout.Children.Add(top);
        layout.Children.Add(_host);
        Content = layout;
        // A native web view draws above AGEX's own overlays, so it hides while a dialog or the command palette is open.
        window.Dialogs.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) UpdateOverlay(); };
        window.Palette.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) UpdateOverlay(); };
    }

    public Uri? Current => _current;

    public static bool IsAllowed(Uri uri, string? root) => Agex.Core.Runtime.PreviewPolicy.IsAllowed(uri, root);

    /// <summary>Loads a local HTML file (its folder may be read) or a local server address.</summary>
    public void Show(Uri uri, string? root)
    {
        _root = root;
        _current = uri;
        _pendingExternal = null;
        _address.Text = uri.IsFile ? "" : uri.AbsoluteUri;
        if (!IsAllowed(uri, root)) { Blocked(uri); return; }
        try
        {
            if (_view is null)
            {
                _view = new NativeWebView { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
                _view.EnvironmentRequested += (_, e) =>
                {
                    // Keep the web engine's cache and cookies in AGEX's own data folder.
                    if (e is WindowsWebView2EnvironmentRequestedEventArgs windows)
                        windows.UserDataFolder = Path.Combine(_window.Workspace.Core.Platform.Paths.CacheRoot, "webview");
                };
                _view.NavigationStarted += (_, e) =>
                {
                    if (e.Request is { } target && !IsAllowed(target, _root)) { e.Cancel = true; Blocked(target); }
                };
                _view.NewWindowRequested += (_, e) => { e.Handled = true; if (e.Request is { } target) Blocked(target); };
                _view.NavigationCompleted += (_, e) =>
                {
                    // A navigation AGEX cancelled also ends as "not successful": keep the explanation.
                    if (_pendingExternal is not null) return;
                    _status.Text = e.IsSuccess ? Describe(_current) : "The page did not load. Is the file or server still there?";
                };
                _host.Child = _view;
            }
            _status.Text = "Loading " + Describe(uri) + "...";
            _view.Navigate(uri);
        }
        catch (Exception ex)
        {
            _window.Workspace.Core.Log.Error("web_preview_failed", ex);
            Unavailable();
        }
        UpdateOverlay();
    }

    private static string Describe(Uri? uri) => uri is null ? "" : uri.IsFile ? Path.GetFileName(uri.LocalPath) + " (file on this computer)" : uri.AbsoluteUri;

    private void GoToAddress()
    {
        var text = (_address.Text ?? "").Trim();
        if (text.Length == 0) return;
        if (!text.Contains("://", StringComparison.Ordinal)) text = "http://" + text;
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri)) Show(uri, _root);
        else _status.Text = "Enter an address such as http://localhost:5173.";
    }

    private void Blocked(Uri target)
    {
        _status.Text = $"The preview only shows files from this folder and servers on this computer. {target.Host} was not opened here; use Open in your browser.";
        _pendingExternal = target;
    }

    private Uri? _pendingExternal;

    private void OpenExternally()
    {
        var target = _pendingExternal ?? _current;
        _pendingExternal = null;
        if (target is null) return;
        try
        {
            if (target.IsFile) _window.Workspace.Core.Platform.OpenPath(target.LocalPath);
            else _window.Workspace.Core.Platform.OpenUrl(target);
        }
        catch (Exception ex) { _window.Toast("Could not open it", ex.Message, ToastKind.Error); }
    }

    private void Unavailable()
    {
        _view = null;
        var help = OperatingSystem.IsWindows() ? "Install the Microsoft Edge WebView2 Runtime (free, from Microsoft) to preview pages here."
            : OperatingSystem.IsLinux() ? "Install WebKitGTK (for example the libwebkit2gtk-4.1 package) to preview pages here."
            : "The system web view could not start.";
        _host.Child = Kit.Text("This computer has no web view AGEX can use. " + help + " Open in your browser works without it.", "small");
        ((TextBlock)_host.Child).TextWrapping = TextWrapping.Wrap;
        _status.Text = "";
    }

    private void UpdateOverlay()
    {
        if (_view is not null) _view.IsVisible = !_window.Dialogs.IsVisible && !_window.Palette.IsVisible;
    }
}
