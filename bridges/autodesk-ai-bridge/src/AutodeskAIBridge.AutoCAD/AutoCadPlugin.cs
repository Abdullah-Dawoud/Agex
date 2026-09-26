#nullable enable
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;

[assembly: ExtensionApplication(typeof(AutodeskAIBridge.AutoCAD.AutoCadBridgePlugin))]
[assembly: CommandClass(typeof(AutodeskAIBridge.AutoCAD.AutoCadBridgePlugin))]

namespace AutodeskAIBridge.AutoCAD;

public sealed class AutoCadBridgePlugin : IExtensionApplication
{
    public void Initialize() => AutoCadBridgeRuntime.Start(Application.DocumentManager);

    public void Terminate() => AutoCadBridgeRuntime.Stop();

    [CommandMethod("AUTODESK_AIBRIDGE_DIAGNOSTICS", CommandFlags.Session)]
    public void Diagnostics()
    {
        AutoCadBridgeRuntime.ShowDiagnostics();
    }
}
