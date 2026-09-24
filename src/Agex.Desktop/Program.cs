using System.IO.Pipes;
using Avalonia;

namespace Agex.Desktop;

public static class Program
{
    public static bool SafeMode { get; private set; }
    public static bool StartMinimized { get; private set; }
    private const string PipeName = "agex-desktop-activate";
    private static Mutex? _instance;

    [STAThread]
    public static int Main(string[] args)
    {
        SafeMode = args.Contains("--safe-mode", StringComparer.OrdinalIgnoreCase);
        StartMinimized = args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
        var home = Environment.GetEnvironmentVariable("AGEX_HOME");
        // One window per user profile; a second start brings the first to the front.
        var mutexName = "AGEX.Desktop." + (string.IsNullOrEmpty(home) ? Environment.UserName : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(home)))[..12]);
        _instance = new Mutex(initiallyOwned: true, mutexName, out var created);
        if (!created)
        {
            SignalExistingInstance();
            return 0;
        }
        AppDomain.CurrentDomain.UnhandledException += (_, e) => App.ReportFatal(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { App.ReportBackground(e.Exception); e.SetObserved(); };
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            _instance.ReleaseMutex();
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .With(new MacOSPlatformOptions { ShowInDock = true })
        .LogToTrace();

    public static string ActivationPipe => PipeName + "-" + Environment.UserName;

    private static void SignalExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", ActivationPipe, PipeDirection.Out);
            client.Connect(1500);
            client.WriteByte(1);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException) { }
    }
}
