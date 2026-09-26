using AutodeskAIBridge.Core;

namespace AutodeskAIBridge.Diagnostics;

public sealed record IntegrationResult(bool AntiGravityConfigured, string? AntiGravityPath, string? Error);

/// <summary>Installer and Manager operations. All writes stay inside user-owned locations.</summary>
public static class IntegrationService
{
    public static IntegrationResult Configure(string hostPath)
    {
        try
        {
            if (!Path.IsPathFullyQualified(hostPath) || !File.Exists(hostPath))
                return new(false, null, "Bridge Host file was not found.");
            BridgeRuntimeSettings.Ensure();
            var path = AntiGravityConfigManager.DetectDefaultPath();
            if (string.IsNullOrWhiteSpace(path))
                return new(false, null, null);
            AntiGravityConfigManager.Configure(path, hostPath, new[] { "--stdio" });
            return new(true, path, null);
        }
        catch (Exception exception)
        {
            return new(false, null, exception.Message);
        }
    }

    public static void RemoveConfiguration()
    {
        foreach (var path in AntiGravityConfigManager.DetectCandidatePaths())
        {
            try { AntiGravityConfigManager.Remove(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static string CreateDiagnosticReport(string hostPath)
    {
        var lines = new List<string>
        {
            "Autodesk AI Bridge Diagnostics",
            $"Host ............... {(File.Exists(hostPath) ? "OK" : "Missing")}",
            $"AntiGravity ......... {(AntiGravityConfigManager.DetectDefaultPath() is not null ? "OK" : "Not detected")}",
            $"MCP configuration ... {(AntiGravityConfigManager.DetectDefaultPath() is not null ? "OK" : "Not detected")}",
            $"IPC security ........ {(BridgeRuntimeSettings.TryLoad() is not null ? "OK" : "Not configured")}",
            string.Empty
        };
        foreach (var item in new DiagnosticsScanner().Scan())
            lines.Add($"{item.Product} {item.Version} ....... {(item.Installed ? (item.BridgeInstalled ? "Plugin installed" : "Product detected; repair needed") : "Not detected")}");
        if (lines.Skip(5).All(line => line.EndsWith("Not detected", StringComparison.Ordinal)))
            lines.Add("Status: No supported Revit or AutoCAD installation was detected. Repair later if a product is installed.");
        return string.Join(Environment.NewLine, lines);
    }
}
