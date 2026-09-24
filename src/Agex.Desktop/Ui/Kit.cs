using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Agex.Desktop.Ui;

public enum Tone { Neutral, Accent, Success, Warning, Danger, Info }

/// <summary>
/// Small builders for the AGEX design system. Views are composed from these so
/// spacing, colors and states stay consistent. Colors are always theme
/// resources, so switching Light/Dark updates everything live.
/// </summary>
public static class Kit
{
    public static readonly Thickness Gap4 = new(4);
    public const double Space1 = 4, Space2 = 8, Space3 = 12, Space4 = 16, Space5 = 24, Space6 = 32;

    public static T Res<T>(this T control, AvaloniaProperty property, string key) where T : Control
    {
        control.Bind(property, control.GetResourceObservable(key));
        return control;
    }

    public static TextBlock Text(string text, string classes = "body", string? foregroundKey = null)
    {
        var block = new TextBlock { Text = text };
        foreach (var name in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)) block.Classes.Add(name);
        if (foregroundKey is not null) block.Res(TextBlock.ForegroundProperty, foregroundKey);
        return block;
    }

    public static SelectableTextBlock Selectable(string text, string classes = "")
    {
        var block = new SelectableTextBlock { Text = text };
        foreach (var name in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)) block.Classes.Add(name);
        return block;
    }

    public static StackPanel Column(double spacing, params Control?[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = spacing };
        foreach (var child in children) if (child is not null) panel.Children.Add(child);
        return panel;
    }

    public static StackPanel Row(double spacing, params Control?[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        foreach (var child in children) if (child is not null) panel.Children.Add(child);
        return panel;
    }

    public static WrapPanel Wrap(params Control?[] children)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var child in children) if (child is not null) { child.Margin = new Thickness(0, 0, Space2, Space2); panel.Children.Add(child); }
        return panel;
    }

    public static Border Card(Control child, double padding = Space4)
    {
        var border = new Border { Child = child, Padding = new Thickness(padding) };
        border.Classes.Add("card");
        return border;
    }

    public static Border Panel(Control child)
    {
        var border = new Border { Child = child };
        border.Classes.Add("panel");
        return border;
    }

    public static Control Icon(string data, double size = 18, string brushKey = "Text2Brush")
    {
        var icon = Icons.Icon(data, size, Brushes.Transparent);
        if (icon is Viewbox { Child: Avalonia.Controls.Shapes.Path path }) path.Res(Avalonia.Controls.Shapes.Shape.StrokeProperty, brushKey);
        return icon;
    }

    /// <summary>Button with optional icon, tooltip (which also shows the shortcut) and accessible name.</summary>
    public static Button Button(string label, Action onClick, string classes = "", string? icon = null, string? tooltip = null, string? shortcut = null)
    {
        var content = icon is null ? (Control)new TextBlock { Text = label, TextWrapping = TextWrapping.NoWrap }
            : Row(Space2, Icon(icon, 16, classes.Contains("primary") ? "AccentTextBrush" : classes.Contains("danger") ? "DangerBrush" : "Text2Brush"), label.Length > 0 ? new TextBlock { Text = label, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center } : null);
        var button = new Button { Content = content, VerticalAlignment = VerticalAlignment.Center };
        foreach (var name in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)) button.Classes.Add(name);
        button.Click += (_, _) => onClick();
        var tip = tooltip ?? (label.Length == 0 ? null : null);
        if (shortcut is not null) tip = (tip ?? label) + $"  ({shortcut})";
        if (tip is not null) ToolTip.SetTip(button, tip);
        AutomationProperties.SetName(button, label.Length > 0 ? label : tooltip ?? "Button");
        return button;
    }

    /// <summary>Icon-only button; always has an accessible name and a tooltip.</summary>
    public static Button IconButton(string icon, string name, Action onClick, string? shortcut = null)
    {
        var button = new Button { Content = Icon(icon, 16), Padding = new Thickness(7), MinHeight = 32, MinWidth = 32 };
        button.Classes.Add("subtle");
        button.Click += (_, _) => onClick();
        ToolTip.SetTip(button, shortcut is null ? name : $"{name}  ({shortcut})");
        AutomationProperties.SetName(button, name);
        return button;
    }

    public static (string Background, string Foreground, string Icon) ToneKeys(Tone tone) => tone switch
    {
        Tone.Accent => ("AccentSoftBrush", "AccentBrush", Icons.Dot),
        Tone.Success => ("SuccessSoftBrush", "SuccessBrush", Icons.Check),
        Tone.Warning => ("WarningSoftBrush", "WarningBrush", Icons.Alert),
        Tone.Danger => ("DangerSoftBrush", "DangerBrush", Icons.Close),
        Tone.Info => ("InfoSoftBrush", "InfoBrush", Icons.Info),
        _ => ("Surface2Brush", "Text2Brush", Icons.Dot),
    };

    /// <summary>Status badge: icon + text + color, so meaning never depends on color alone.</summary>
    public static Border Badge(string text, Tone tone, string? icon = null)
    {
        var (background, foreground, defaultIcon) = ToneKeys(tone);
        var label = Text(text, "caption", foreground);
        label.FontWeight = FontWeight.SemiBold;
        label.TextWrapping = TextWrapping.NoWrap;
        var border = new Border { Child = Row(4, Icon(icon ?? defaultIcon, 12, foreground), label), VerticalAlignment = VerticalAlignment.Center };
        border.Classes.Add("pill");
        border.Res(Border.BackgroundProperty, background);
        AutomationProperties.SetName(border, text);
        return border;
    }

    public static readonly Dictionary<string, Color> AgentColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Codex"] = Color.Parse("#0E8C7B"),
        ["Antigravity"] = Color.Parse("#7457E8"),
        ["Claude Code"] = Color.Parse("#C4702A"),
        ["Gemini CLI"] = Color.Parse("#2F74D0"),
        ["Ollama"] = Color.Parse("#5D6B7E"),
        ["AGEX"] = Color.Parse("#3558D6"),
        ["User"] = Color.Parse("#6B6B63"),
    };

    /// <summary>Round avatar with initials in the agent's color.</summary>
    public static Border Avatar(string name, double size = 30)
    {
        var color = AgentColors.TryGetValue(name, out var known) ? known : Color.Parse("#6B6B63");
        var initials = name == "User" ? "You" : string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => char.ToUpperInvariant(part[0])));
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = new SolidColorBrush(color),
            Child = new TextBlock { Text = initials, Foreground = Brushes.White, FontSize = size * (initials.Length > 2 ? 0.3 : 0.4), FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap },
            [AutomationProperties.NameProperty] = name,
        };
    }

    public static Control SectionHeader(string title, string? description = null, Control? action = null)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, Space3) };
        grid.Children.Add(Column(2, Text(title, "subtitle"), description is null ? null : Text(description, "small")));
        if (action is not null) { Grid.SetColumn(action, 1); action.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(action); }
        return grid;
    }

    public static Control PageHeader(string title, string? subtitle = null, Control? actions = null)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, Space5) };
        grid.Children.Add(Column(4, Text(title, "title"), subtitle is null ? null : Text(subtitle, "small")));
        if (actions is not null) { Grid.SetColumn(actions, 1); actions.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(actions); }
        return grid;
    }

    /// <summary>Scrollable page body with a comfortable reading width.</summary>
    public static ScrollViewer Page(Control content, double maxWidth = 1100) => new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Content = new Border { Padding = new Thickness(Space6, Space5, Space6, Space6), MaxWidth = maxWidth, HorizontalAlignment = HorizontalAlignment.Stretch, Child = content },
    };

    public static Control EmptyState(string icon, string title, string text, Control? action = null) =>
        new Border
        {
            Padding = new Thickness(Space5),
            Child = Column(Space2, Center(Icon(icon, 32, "Text3Brush")), Center(Text(title, "subtitle")), Center(Text(text, "small", null).WithAlignment(TextAlignment.Center)), action is null ? null : Center(action)),
        };

    private static Control Center(Control control) { control.HorizontalAlignment = HorizontalAlignment.Center; return control; }

    public static TextBlock WithAlignment(this TextBlock block, TextAlignment alignment) { block.TextAlignment = alignment; return block; }

    public static T Left<T>(this T control) where T : Control { control.HorizontalAlignment = HorizontalAlignment.Left; return control; }

    /// <summary>One line, cut with an ellipsis; the full text is in the tooltip.</summary>
    public static TextBlock Trimmed(this TextBlock block, double maxWidth)
    {
        block.TextWrapping = TextWrapping.NoWrap;
        block.TextTrimming = TextTrimming.CharacterEllipsis;
        block.MaxWidth = maxWidth;
        ToolTip.SetTip(block, block.Text);
        return block;
    }

    public static ToggleSwitch Toggle(string label, bool value, Action<bool> changed, string? description = null)
    {
        var toggle = new ToggleSwitch { IsChecked = value, OnContent = "On", OffContent = "Off" };
        toggle.IsCheckedChanged += (_, _) => changed(toggle.IsChecked == true);
        AutomationProperties.SetName(toggle, label);
        return toggle;
    }

    /// <summary>A labelled settings row: label and help text on the left, the control on the right.</summary>
    public static Control SettingRow(string label, string? help, Control control)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, Space2) };
        grid.Children.Add(Column(2, Text(label, "body"), help is null ? null : Text(help, "small")));
        Grid.SetColumn(control, 1);
        control.VerticalAlignment = VerticalAlignment.Center;
        control.Margin = new Thickness(Space4, 0, 0, 0);
        grid.Children.Add(control);
        if (control is not null) AutomationProperties.SetName(control, label);
        return grid;
    }

    public static ComboBox Combo<T>(IEnumerable<(T Value, string Label)> items, T selected, Action<T> changed, double width = 220)
    {
        var list = items.ToList();
        var combo = new ComboBox { ItemsSource = list.Select(item => item.Label).ToList(), MinWidth = width, SelectedIndex = Math.Max(0, list.FindIndex(item => Equals(item.Value, selected))) };
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) changed(list[combo.SelectedIndex].Value); };
        return combo;
    }

    public static Separator Divider() => new();

    public static KeyGesture Gesture(Key key, bool shift = false)
    {
        var modifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        return new KeyGesture(key, shift ? modifier | KeyModifiers.Shift : modifier);
    }

    public static string ShortcutText(string key, bool shift = false) => (OperatingSystem.IsMacOS() ? "Cmd+" : "Ctrl+") + (shift ? "Shift+" : "") + key;

    public static string Ago(DateTimeOffset at)
    {
        var span = DateTimeOffset.UtcNow - at;
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} min ago";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours} h ago";
        if (span < TimeSpan.FromDays(7)) return $"{(int)span.TotalDays} d ago";
        return at.ToLocalTime().ToString("d MMM yyyy");
    }
}
