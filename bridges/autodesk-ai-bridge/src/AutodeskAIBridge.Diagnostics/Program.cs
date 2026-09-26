using System;
using System.Windows.Forms;

namespace AutodeskAIBridge.Diagnostics;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--configure", StringComparer.OrdinalIgnoreCase))
        {
            var hostIndex = Array.FindIndex(args, value => value.Equals("--host", StringComparison.OrdinalIgnoreCase));
            var hostPath = hostIndex >= 0 && hostIndex + 1 < args.Length ? args[hostIndex + 1] : string.Empty;
            var result = IntegrationService.Configure(hostPath);
            Environment.ExitCode = result.Error is null ? 0 : 1;
            return;
        }
        if (args.Contains("--unconfigure", StringComparer.OrdinalIgnoreCase))
        {
            IntegrationService.RemoveConfiguration();
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
