using Avalonia.Controls;
using Avalonia.Media;

namespace Agex.Desktop.Ui;

/// <summary>
/// AGEX's own line icons, drawn from simple geometry on a 24x24 grid (no
/// third-party icon set). Rendered as outlines through <see cref="Icon"/>.
/// </summary>
public static class Icons
{
    public const string Home = "M4,11 L12,4 L20,11 M6,9.5 L6,20 L18,20 L18,9.5 M10,20 L10,14 L14,14 L14,20";
    public const string Room = "M4,5 L16,5 L16,13 L9,13 L5,16.5 L5,13 L4,13 Z M18,9 L20,9 L20,17 L19,17 L19,20 L15.5,17 L10,17 L10,15";
    public const string Folder = "M3,6 L9,6 L11,8 L21,8 L21,19 L3,19 Z";
    public const string Agent = "M7,8 L17,8 L17,18 L7,18 Z M12,8 L12,5 M12,4.5 L12,4.6 M10,12 L10,12.2 M14,12 L14,12.2 M10,15 L14,15 M4,12 L7,12 M17,12 L20,12";
    public const string Skills = "M4,9 L9,9 L9,7 A2,2 0 1 1 13,7 L13,9 L18,9 L18,13 L20,13 A2,2 0 1 1 20,17 L18,17 L18,20 L4,20 Z";
    public const string History = "M4,12 A8,8 0 1 0 6.5,6.2 M4,4 L4,8 L8,8 M12,8 L12,12 L15,14";
    public const string Settings = "M4,7 L20,7 M4,12 L20,12 M4,17 L20,17 M9,5 L9,9 M15,10 L15,14 M7,15 L7,19";
    public const string Search = "M10.5,4 A6.5,6.5 0 1 1 10.49,4 Z M15.5,15.5 L20,20";
    public const string Send = "M4,12 L20,5 L15,20 L11.5,13 Z M11.5,13 L20,5";
    public const string Stop = "M7,7 L17,7 L17,17 L7,17 Z";
    public const string Pause = "M8,6 L8,18 M16,6 L16,18";
    public const string Play = "M8,5 L19,12 L8,19 Z";
    public const string Check = "M5,12.5 L10,17 L19,7";
    public const string Close = "M6,6 L18,18 M18,6 L6,18";
    public const string Alert = "M12,4 L21,19 L3,19 Z M12,10 L12,14 M12,16.5 L12,16.6";
    public const string Info = "M12,3.5 A8.5,8.5 0 1 1 11.99,3.5 Z M12,11 L12,16 M12,8 L12,8.1";
    public const string Plus = "M12,5 L12,19 M5,12 L19,12";
    public const string Copy = "M9,9 L19,9 L19,19 L9,19 Z M5,15 L5,5 L15,5";
    public const string External = "M14,5 L19,5 L19,10 M19,5 L11,13 M17,14 L17,19 L5,19 L5,7 L10,7";
    public const string Refresh = "M19,7 A8,8 0 1 0 20,12 M19,3 L19,7 L15,7";
    public const string Moon = "M19,14.5 A7.5,7.5 0 1 1 9.5,5 A6,6 0 0 0 19,14.5 Z";
    public const string Sun = "M12,8 A4,4 0 1 1 11.99,8 Z M12,2.5 L12,4.5 M12,19.5 L12,21.5 M2.5,12 L4.5,12 M19.5,12 L21.5,12 M5.3,5.3 L6.7,6.7 M17.3,17.3 L18.7,18.7 M5.3,18.7 L6.7,17.3 M17.3,6.7 L18.7,5.3";
    public const string Graph = "M6,6 A2,2 0 1 1 5.99,6 Z M18,6 A2,2 0 1 1 17.99,6 Z M12,18 A2,2 0 1 1 11.99,18 Z M8,6 L16,6 M7,8 L11,16 M17,8 L13,16";
    public const string Chevron = "M9,6 L15,12 L9,18";
    public const string ChevronDown = "M6,9 L12,15 L18,9";
    public const string Lock = "M7,11 L17,11 L17,20 L7,20 Z M9,11 L9,8 A3,3 0 0 1 15,8 L15,11";
    public const string Cloud = "M7,18 A4,4 0 0 1 7,10 A5,5 0 0 1 16.5,8.5 A4.5,4.5 0 0 1 17,18 Z";
    public const string Computer = "M4,5 L20,5 L20,15 L4,15 Z M9,19 L15,19 M12,15 L12,19";
    public const string Tool = "M14.5,5.5 A4,4 0 0 0 19,11 L11,19 A2,2 0 0 1 8,16 L16,8 A4,4 0 0 0 14.5,5.5 Z";
    public const string Question = "M9.5,9 A2.5,2.5 0 1 1 13,11.3 L12,12.2 L12,14 M12,17 L12,17.1";
    public const string Download = "M12,4 L12,15 M7.5,10.5 L12,15 L16.5,10.5 M5,19 L19,19";
    public const string Trash = "M5,7 L19,7 M10,7 L10,4.5 L14,4.5 L14,7 M7,7 L8,20 L16,20 L17,7";
    public const string Undo = "M9,7 L4,11 L9,15 M4,11 L15,11 A5,5 0 0 1 15,21 L11,21";
    public const string Keyboard = "M3,7 L21,7 L21,17 L3,17 Z M7,14 L17,14 M7,10 L7,10.1 M11,10 L11,10.1 M15,10 L15,10.1";
    public const string Shield = "M12,3.5 L19,6.5 L19,12 A8,8 0 0 1 12,20.5 A8,8 0 0 1 5,12 L5,6.5 Z";
    public const string Dot = "M12,9 A3,3 0 1 1 11.99,9 Z";

    /// <summary>An outline icon. Size in device-independent pixels; color follows the element's foreground unless given.</summary>
    public static Control Icon(string data, double size = 18, IBrush? brush = null, double stroke = 1.7)
    {
        var path = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(data),
            StrokeThickness = stroke,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Stretch = Stretch.None,
            Width = 24,
            Height = 24,
        };
        if (brush is not null) path.Stroke = brush;
        else path.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, path.GetResourceObservable("Text2Brush"));
        return new Viewbox { Width = size, Height = size, Child = path, IsHitTestVisible = false };
    }
}
