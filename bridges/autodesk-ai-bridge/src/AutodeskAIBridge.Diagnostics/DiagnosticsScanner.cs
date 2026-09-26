using Microsoft.Win32;

namespace AutodeskAIBridge.Diagnostics;

public sealed class InstallationStatus
{
    public string Product { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public bool Installed { get; init; }
    public bool ApiPresent { get; init; }
    public bool BridgeInstalled { get; init; }
    public string Detail { get; init; } = string.Empty;
}

public sealed class DiagnosticsScanner
{
    private static readonly string[] RevitVersions = ["2024", "2025", "2026"];
    private static readonly string[] AutoCadVersions = ["2025", "2026", "2027"];

    public IReadOnlyList<InstallationStatus> Scan()
    {
        var results = new List<InstallationStatus>();
        foreach (var version in RevitVersions)
        {
            var key = $"SOFTWARE\\Autodesk\\Revit\\Autodesk Revit {version}";
            results.Add(ReadProduct("Revit", version, key, "RevitAPI.dll", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "Addins", version, "AutodeskAIBridge.addin")));
        }
        foreach (var version in AutoCadVersions)
        {
            var key = $"SOFTWARE\\Autodesk\\AutoCAD\\R{(int.Parse(version) - 1999)}.0";
            results.Add(ReadProduct("AutoCAD", version, key, "AcDbMgd.dll", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "ApplicationPlugins", "AutodeskAIBridge.bundle", "PackageContents.xml")));
        }
        return results;
    }

    private static InstallationStatus ReadProduct(string product, string version, string key, string apiFile, string bridgePath)
    {
        var registryPath = ReadInstallLocation(Registry.LocalMachine, key) ?? ReadInstallLocation(Registry.CurrentUser, key);
        var registryInstalled = registryPath is not null || Registry.LocalMachine.OpenSubKey(key) is not null || Registry.CurrentUser.OpenSubKey(key) is not null;
        var productDirectory = registryPath ?? Path.Combine("C:\\Program Files\\Autodesk", $"{product} {version}");
        var apiPath = Path.Combine(productDirectory, apiFile);
        var installed = registryInstalled || Directory.Exists(productDirectory);
        var apiPresent = File.Exists(apiPath);
        return new InstallationStatus
        {
            Product = product,
            Version = version,
            Installed = installed,
            ApiPresent = apiPresent,
            BridgeInstalled = File.Exists(bridgePath),
            Detail = installed
                ? File.Exists(bridgePath) ? "Detected; Autodesk AI Bridge integration installed." : "Detected; Bridge integration needs repair."
                : "Not detected"
        };
    }

    private static string? ReadInstallLocation(RegistryKey root, string key)
    {
        using var product = root.OpenSubKey(key);
        var value = product?.GetValue("InstallLocation") as string;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
