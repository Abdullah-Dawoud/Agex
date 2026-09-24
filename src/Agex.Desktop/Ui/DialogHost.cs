using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Agex.Desktop.Ui;

/// <summary>
/// In-window modal dialogs (same look on every platform, keyboard friendly:
/// Enter = default button, Esc = cancel, focus trapped in the dialog).
/// </summary>
public sealed class DialogHost : Grid
{
    private readonly Border _scrim;
    private readonly Border _frame;
    private TaskCompletionSource<int>? _pending;
    private int _cancelIndex;
    private Button? _defaultButton;
    private IInputElement? _previousFocus;

    public DialogHost()
    {
        IsVisible = false;
        _scrim = new Border();
        _scrim.Res(Border.BackgroundProperty, "OverlayBrush");
        _frame = new Border { MaxWidth = 560, Margin = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(24) };
        _frame.Classes.Add("card");
        _frame.BoxShadow = BoxShadows.Parse("0 12 32 0 #33000000");
        Children.Add(_scrim);
        Children.Add(_frame);
        KeyDown += OnKeyDown;
    }

    public bool IsOpen => IsVisible;

    /// <summary>Shows a dialog and returns the index of the pressed button (or the cancel index on Esc).</summary>
    public Task<int> ShowAsync(string title, Control body, string[] buttons, int defaultIndex = 0, int cancelIndex = -1, double maxWidth = 560)
    {
        _pending?.TrySetResult(_cancelIndex);
        _pending = new TaskCompletionSource<int>();
        _cancelIndex = cancelIndex < 0 ? buttons.Length - 1 : cancelIndex;
        _frame.MaxWidth = maxWidth;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        for (var index = 0; index < buttons.Length; index++)
        {
            var captured = index;
            var button = Kit.Button(buttons[index], () => Close(captured), index == defaultIndex ? "primary" : "");
            if (index == defaultIndex) _defaultButton = button;
            actions.Children.Add(button);
        }
        var heading = Kit.Text(title, "subtitle");
        AutomationProperties.SetName(_frame, title);
        _frame.Child = new StackPanel { Spacing = 12, Children = { heading, new ScrollViewer { MaxHeight = 520, Content = body }, actions } };
        _previousFocus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        IsVisible = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => (FirstFocusable(body) ?? _defaultButton)?.Focus());
        return _pending.Task;
    }

    public Task<int> MessageAsync(string title, string message, params string[] buttons) =>
        ShowAsync(title, Kit.Text(message, "body"), buttons.Length == 0 ? ["OK"] : buttons);

    public void Close(int result)
    {
        IsVisible = false;
        _frame.Child = null;
        _pending?.TrySetResult(result);
        _pending = null;
        _previousFocus?.Focus();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsVisible) return;
        if (e.Key == Key.Escape) { Close(_cancelIndex); e.Handled = true; }
        else if (e.Key == Key.Enter && e.Source is not TextBox { AcceptsReturn: true } && _defaultButton is not null)
        {
            _defaultButton.Focus();
        }
    }

    private static InputElement? FirstFocusable(Control root)
    {
        if (root is TextBox or ComboBox or CheckBox) return (InputElement)root;
        if (root is Panel panel) foreach (var child in panel.Children) if (child is Control control && FirstFocusable(control) is { } found) return found;
        if (root is Decorator { Child: Control decorated }) return FirstFocusable(decorated);
        if (root is ContentControl { Content: Control content }) return FirstFocusable(content);
        return null;
    }
}
