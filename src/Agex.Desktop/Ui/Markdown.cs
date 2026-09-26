using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Agex.Desktop.Ui;

/// <summary>
/// Readable answers: the common Markdown that agents write (headings, lists,
/// code blocks, bold, inline code and links) shown as formatted text. Links are
/// shown as their text; nothing is opened from here.
/// </summary>
public static partial class Markdown
{
    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"(\*\*[^*]+\*\*|`[^`]+`)")]
    private static partial Regex Inline();

    [GeneratedRegex(@"^(\s*)([-*•]|\d+[.)])\s+(.*)$")]
    private static partial Regex ListItem();

    public static Control View(string text, double maxWidth = 760)
    {
        var panel = new StackPanel { Spacing = 6, MaxWidth = maxWidth, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var paragraph = new List<string>();
        void Flush()
        {
            if (paragraph.Count == 0) return;
            panel.Children.Add(Block(string.Join(" ", paragraph.Select(line => line.Trim())), "body"));
            paragraph.Clear();
        }
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                Flush();
                var code = new List<string>();
                for (index++; index < lines.Length && !lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal); index++) code.Add(lines[index]);
                var block = new SelectableTextBlock { Text = string.Join("\n", code), TextWrapping = TextWrapping.Wrap };
                block.Classes.Add("mono");
                var border = new Border { Child = block, Padding = new Thickness(10, 8), CornerRadius = new CornerRadius(6) };
                border.Res(Border.BackgroundProperty, "Surface2Brush");
                panel.Children.Add(border);
                continue;
            }
            if (line.Trim().Length == 0) { Flush(); continue; }
            var heading = line.TrimStart();
            if (heading.StartsWith('#'))
            {
                Flush();
                var level = heading.TakeWhile(ch => ch == '#').Count();
                var title = Block(heading[level..].Trim(), level <= 2 ? "subtitle" : "body");
                title.FontWeight = FontWeight.SemiBold;
                title.Margin = new Thickness(0, panel.Children.Count > 0 ? 6 : 0, 0, 0);
                panel.Children.Add(title);
                continue;
            }
            if (ListItem().Match(line) is { Success: true } item)
            {
                Flush();
                var indent = Math.Min(item.Groups[1].Value.Length / 2, 3) * 16;
                var marker = char.IsDigit(item.Groups[2].Value[0]) ? item.Groups[2].Value : "•";
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8, Margin = new Thickness(indent + 4, 0, 0, 0) };
                grid.Children.Add(Kit.Text(marker, "body"));
                var body = Block(item.Groups[3].Value, "body");
                Grid.SetColumn(body, 1);
                grid.Children.Add(body);
                panel.Children.Add(grid);
                continue;
            }
            paragraph.Add(line);
        }
        Flush();
        return panel;
    }

    /// <summary>A wrapping, selectable paragraph with bold and inline code.</summary>
    private static SelectableTextBlock Block(string text, string classes)
    {
        var block = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
        block.Classes.Add(classes);
        text = Link().Replace(text, match => match.Groups[1].Value);
        foreach (var part in Inline().Split(text))
        {
            if (part.Length == 0) continue;
            if (part.StartsWith("**", StringComparison.Ordinal) && part.EndsWith("**", StringComparison.Ordinal) && part.Length > 4)
                block.Inlines!.Add(new Run(part[2..^2]) { FontWeight = FontWeight.SemiBold });
            else if (part.StartsWith('`') && part.EndsWith('`') && part.Length > 2)
                block.Inlines!.Add(new Run(part[1..^1]) { FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace") });
            else block.Inlines!.Add(new Run(part));
        }
        return block;
    }
}
