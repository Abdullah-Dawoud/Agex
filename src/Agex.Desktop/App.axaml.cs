using Agex.Core;
using Agex.Core.Settings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Agex.Desktop;

public partial class App : Application
{
    public static readonly ThemeVariant HighContrast = new("HighContrast", ThemeVariant.Dark);
    private static AgexCore? _core;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        AddHighContrastTheme();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _core = new AgexCore(safeMode: Program.SafeMode);
            _core.Start();
            ApplyTheme(_core.Settings.Theme);
            ApplyTextScale(_core.Settings.TextScale);
            if (PlatformSettings is { } settings) settings.ColorValuesChanged += (_, _) => ApplyTheme(_core.Settings.Theme);
            var workspace = new Workspace(_core);
            var window = new MainWindow(workspace);
            // A failing button handler (full disk, read-only folder, agent error) must not close the app.
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                _core.Log.Error("ui_error", e.Exception);
                window.Toast("Something went wrong", e.Exception.Message + " (details are in the log).", ToastKind.Error);
                e.Handled = true;
            };
            desktop.MainWindow = window;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.ShutdownRequested += (_, _) => workspace.Shutdown();
            if (Program.StartMinimized || _core.Settings.LaunchMinimized) window.WindowState = WindowState.Minimized;
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Applies System, Light or Dark immediately (no restart). System follows the OS, including high contrast.</summary>
    public static void ApplyTheme(ThemeChoice choice)
    {
        if (Current is not { } app) return;
        // AGEX_HIGH_CONTRAST=1 previews the high-contrast theme without changing the system setting (testing and screenshots).
        var contrast = app.PlatformSettings?.GetColorValues().ContrastPreference == ColorContrastPreference.High || Environment.GetEnvironmentVariable("AGEX_HIGH_CONTRAST") == "1";
        app.RequestedThemeVariant = choice switch
        {
            ThemeChoice.Light => ThemeVariant.Light,
            ThemeChoice.Dark => ThemeVariant.Dark,
            _ => contrast ? HighContrast : ThemeVariant.Default,
        };
    }

    /// <summary>Scales the whole type ramp (accessibility: larger text without relayout bugs).</summary>
    public static void ApplyTextScale(double scale)
    {
        if (Current is not { } app) return;
        foreach (var (key, size) in new[] { ("FontCaption", 12.0), ("FontSmall", 13.0), ("FontBody", 14.0), ("FontSubtitle", 16.0), ("FontTitle", 20.0), ("FontDisplay", 28.0), ("ControlContentThemeFontSize", 14.0) })
            app.Resources[key] = Math.Round(size * scale, 1);
    }

    private void AddHighContrastTheme()
    {
        var colors = new Dictionary<string, string>
        {
            ["AppBg"] = "#000000", ["Surface"] = "#000000", ["Surface2"] = "#0A0A0A", ["SurfaceHover"] = "#1A1A1A",
            ["Border"] = "#FFFFFF", ["BorderStrong"] = "#FFFFFF", ["Text"] = "#FFFFFF", ["Text2"] = "#FFFFFF", ["Text3"] = "#E0E0E0",
            ["Accent"] = "#FFFF00", ["AccentHover"] = "#FFFF66", ["AccentText"] = "#000000", ["AccentSoft"] = "#1A1A00",
            ["Success"] = "#3FF23F", ["SuccessSoft"] = "#002200", ["Warning"] = "#FFB000", ["WarningSoft"] = "#221800",
            ["Danger"] = "#FF6B6B", ["DangerSoft"] = "#220000", ["Info"] = "#6BD5FF", ["InfoSoft"] = "#001A22",
            ["DiffAdd"] = "#003300", ["DiffRemove"] = "#330000", ["Focus"] = "#FFFF00", ["Overlay"] = "#CC000000", ["Shadow"] = "#00000000",
        };
        var dictionary = new ResourceDictionary();
        foreach (var (key, value) in colors) dictionary[key] = Color.Parse(value);
        Resources.ThemeDictionaries[HighContrast] = dictionary;
    }

    /// <summary>Last-chance handler: log, save state as not clean, never lose the user's draft.</summary>
    public static void ReportFatal(Exception? ex)
    {
        try
        {
            _core?.Log.Error("fatal", ex ?? new Exception("unknown"));
            _core?.Stop(clean: false);
        }
        catch (Exception) { }
    }

    public static void ReportBackground(Exception ex)
    {
        try { _core?.Log.Error("background_error", ex); } catch (Exception) { }
    }

    public static void Post(Action action) => Dispatcher.UIThread.Post(() =>
    {
        try { action(); }
        catch (Exception ex) { ReportBackground(ex); }
    });
}
