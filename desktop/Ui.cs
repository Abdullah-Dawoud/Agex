// AGEX desktop: theme and small UI building helpers (the UI is built in code
// so the app compiles with the C# compiler that ships with Windows).
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace Agex.Desktop
{
    public static class Theme
    {
        public static string Current = "Light";

        public static void Apply(ResourceDictionary resources, string theme)
        {
            Current = theme == "Dark" ? "Dark" : "Light";
            bool dark = Current == "Dark";
            Set(resources, "WindowBrush", dark ? "#15181E" : "#F4F6F9");
            Set(resources, "CardBrush", dark ? "#1E232B" : "#FFFFFF");
            Set(resources, "CardBorderBrush", dark ? "#2C333D" : "#E1E5EB");
            Set(resources, "TextBrush", dark ? "#E6E9EF" : "#1D2330");
            Set(resources, "MutedBrush", dark ? "#9AA4B2" : "#5F6B7A");
            Set(resources, "SidebarBrush", dark ? "#0F1216" : "#1C222D");
            Set(resources, "SidebarTextBrush", "#D8DEE9");
            Set(resources, "SidebarSelectedBrush", dark ? "#1F2733" : "#2B3444");
            Set(resources, "AccentBrush", "#3D7BFD");
            Set(resources, "InputBrush", dark ? "#12161C" : "#FFFFFF");
            Set(resources, "HoverBrush", dark ? "#262D38" : "#EEF2F8");
            Set(resources, "CodeBrush", dark ? "#12161C" : "#F7F8FA");
        }

        static void Set(ResourceDictionary resources, string key, string color)
        {
            resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }

        public static Brush StatusBrush(string state)
        {
            switch ((state ?? "").ToLowerInvariant())
            {
                case "running": case "starting": case "verifying": return Hex("#1E88E5");
                case "done": case "complete": case "complete_with_fallback": case "ready": case "ok": case "pass": return Hex("#2E7D32");
                case "waiting": case "queued": case "partial": case "unverified": case "cancelled": case "cancelling": case "warning": case "warn": case "fallback": case "detected": return Hex("#B7791F");
                case "failed": case "start_failed": case "fail": case "error": case "unavailable": case "repair required": case "blocked": return Hex("#C62828");
                default: return Hex("#78909C");
            }
        }

        public static Brush Hex(string color)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }

        public static string StatusLabel(string status)
        {
            switch ((status ?? "").ToUpperInvariant())
            {
                case "RUNNING": return "Running";
                case "CANCELLING": return "Cancelling";
                case "COMPLETE": return "Complete";
                case "COMPLETE_WITH_FALLBACK": return "Recovered";
                case "PARTIAL": return "Partial";
                case "UNVERIFIED": return "Not verified";
                case "FAILED": return "Failed";
                case "START_FAILED": return "Could not start";
                case "CANCELLED": return "Cancelled";
                case "": case "IDLE": return "Ready";
                default: return status;
            }
        }
    }

    public static class U
    {
        public static TextBlock Text(string text, double size = 13, bool bold = false, string brushKey = "TextBrush")
        {
            var t = new TextBlock { Text = text ?? "", FontSize = size, TextWrapping = TextWrapping.Wrap };
            if (bold) t.FontWeight = FontWeights.SemiBold;
            t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
            return t;
        }

        public static TextBlock Muted(string text, double size = 12)
        {
            return Text(text, size, false, "MutedBrush");
        }

        public static TextBlock Header(string text)
        {
            var t = Text(text, 20, true);
            t.Margin = new Thickness(0, 0, 0, 12);
            return t;
        }

        public static TextBlock Section(string text)
        {
            var t = Text(text, 14, true);
            t.Margin = new Thickness(0, 0, 0, 8);
            return t;
        }

        public static Border Card(UIElement child, double padding = 16)
        {
            var b = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(padding), Margin = new Thickness(0, 0, 0, 12), BorderThickness = new Thickness(1), Child = child };
            b.SetResourceReference(Border.BackgroundProperty, "CardBrush");
            b.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            return b;
        }

        public static Button Button(string text, RoutedEventHandler click, bool primary = false)
        {
            var b = new Button { Content = text, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 4, 8, 4), MinWidth = 80, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left };
            b.Template = ButtonTemplate();
            if (primary)
            {
                b.SetResourceReference(Control.BackgroundProperty, "AccentBrush");
                b.Foreground = Brushes.White;
                b.BorderThickness = new Thickness(0);
            }
            else
            {
                b.SetResourceReference(Control.BackgroundProperty, "CardBrush");
                b.SetResourceReference(Control.ForegroundProperty, "TextBrush");
                b.SetResourceReference(Control.BorderBrushProperty, "CardBorderBrush");
                b.BorderThickness = new Thickness(1);
            }
            if (click != null) b.Click += click;
            return b;
        }

        static ControlTemplate buttonTemplate;
        static ControlTemplate ButtonTemplate()
        {
            if (buttonTemplate != null) return buttonTemplate;
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);
            var t = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.88));
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
            t.Triggers.Add(hover);
            t.Triggers.Add(disabled);
            buttonTemplate = t;
            return t;
        }

        public static Border Pill(string text, Brush color)
        {
            var t = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = color };
            var b = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 8, 2), BorderThickness = new Thickness(1), BorderBrush = color, Child = t, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            return b;
        }

        public static Ellipse Dot(Brush color, double size = 9)
        {
            return new Ellipse { Width = size, Height = size, Fill = color, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        }

        public static StackPanel Row(params UIElement[] children)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var c in children) if (c != null) p.Children.Add(c);
            return p;
        }

        public static WrapPanel Wrap(params UIElement[] children)
        {
            var p = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var c in children) if (c != null) p.Children.Add(c);
            return p;
        }

        public static StackPanel Stack(params UIElement[] children)
        {
            var p = new StackPanel();
            foreach (var c in children) if (c != null) p.Children.Add(c);
            return p;
        }

        public static TextBox ReadOnlyBox(string text, bool mono = false, double minHeight = 0)
        {
            var box = new TextBox { Text = text ?? "", IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0), MinHeight = minHeight, AcceptsReturn = true };
            box.SetResourceReference(Control.BackgroundProperty, "CardBrush");
            box.SetResourceReference(Control.ForegroundProperty, "TextBrush");
            if (mono) { box.FontFamily = new FontFamily("Cascadia Mono, Consolas"); box.FontSize = 12; box.TextWrapping = TextWrapping.NoWrap; box.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto; }
            return box;
        }

        public static TextBox Input(string text = "", double minHeight = 0, bool multiline = false)
        {
            var box = new TextBox { Text = text ?? "", Padding = new Thickness(8, 6, 8, 6), MinHeight = minHeight, BorderThickness = new Thickness(1) };
            if (multiline) { box.AcceptsReturn = true; box.TextWrapping = TextWrapping.Wrap; box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; }
            box.SetResourceReference(Control.BackgroundProperty, "InputBrush");
            box.SetResourceReference(Control.ForegroundProperty, "TextBrush");
            box.SetResourceReference(Control.BorderBrushProperty, "CardBorderBrush");
            return box;
        }

        public static ComboBox Combo(string[] items, string selected)
        {
            var c = new ComboBox { MinWidth = 160, Margin = new Thickness(0, 0, 12, 0), Padding = new Thickness(6, 4, 6, 4) };
            foreach (var i in items) c.Items.Add(i);
            c.SelectedItem = Array.IndexOf(items, selected) >= 0 ? selected : (items.Length > 0 ? items[0] : null);
            return c;
        }

        public static CheckBox Check(string text, bool value)
        {
            var c = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 4, 0, 4), VerticalContentAlignment = VerticalAlignment.Center };
            c.SetResourceReference(Control.ForegroundProperty, "TextBrush");
            return c;
        }

        public static ScrollViewer Scroll(UIElement content)
        {
            return new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(24, 20, 24, 20) };
        }

        public static FrameworkElement Labeled(string label, UIElement control)
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var l = Text(label);
            l.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(l);
            Grid.SetColumn(control, 1);
            g.Children.Add(control);
            return g;
        }
    }

}
