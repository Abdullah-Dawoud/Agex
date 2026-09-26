#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using AutodeskAIBridge.Core;
using Autodesk.Revit.UI;

namespace AutodeskAIBridge.Revit;

public sealed class RevitBridgeApplication : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        RevitBridgeRuntime.Start(application);
        CreateRibbon(application);
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        RevitBridgeRuntime.Stop();
        return Result.Succeeded;
    }

    private static void CreateRibbon(UIControlledApplication application)
    {
        try
        {
            var panel = application.CreateRibbonPanel("Autodesk AI Bridge");
            var assemblyPath = typeof(RevitBridgeApplication).Assembly.Location;
            panel.AddItem(new PushButtonData("Reconnect", "Reconnect", assemblyPath, typeof(RevitCommands.ReconnectCommand).FullName!));
            panel.AddItem(new PushButtonData("Diagnostics", "Diagnostics", assemblyPath, typeof(RevitCommands.DiagnosticsCommand).FullName!));
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // Ribbon panel may already exist after an add-in reload.
        }
    }
}

public static class RevitBridgeRuntime
{
    private static readonly object Sync = new();
    private static ExternalEvent? _externalEvent;
    private static RevitPluginSession? _session;

    public static RevitRequestQueue? Queue { get; private set; }

    public static void Start(UIControlledApplication application)
    {
        lock (Sync)
        {
            if (Queue is not null)
                return;
            Queue = new RevitRequestQueue();
            var dispatcher = new RevitCommandDispatcher();
            var handler = new RevitExternalEventHandler(Queue, dispatcher);
            _externalEvent = ExternalEvent.Create(handler);
            Queue.Attach(_externalEvent);
            var settings = BridgeRuntimeSettings.TryLoad();
            var secret = Environment.GetEnvironmentVariable("AUTODESK_AI_BRIDGE_SECRET") ?? settings?.SharedSecret;
            if (!string.IsNullOrWhiteSpace(secret))
            {
                _session = new RevitPluginSession(Environment.GetEnvironmentVariable("AUTODESK_AI_BRIDGE_PIPE") ?? settings?.PipeName ?? "AutodeskAIBridge", secret, application.ControlledApplication.VersionNumber);
                _ = RunSessionAsync(_session);
            }
        }
    }

    public static void Stop()
    {
        lock (Sync)
        {
            Queue?.Dispose();
            _session?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _session = null;
            _externalEvent?.Dispose();
            _externalEvent = null;
            Queue = null;
        }
    }

    private static async Task RunSessionAsync(RevitPluginSession session)
    {
        try { await session.RunAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception exception) { System.Diagnostics.Debug.WriteLine($"Autodesk AI Bridge Revit session stopped: {exception.GetType().Name}"); }
    }
}

internal static class RevitCommands
{
    internal sealed class ReconnectCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            TaskDialog.Show("Autodesk AI Bridge", "Bridge request queue is ready.");
            return Result.Succeeded;
        }
    }

    internal sealed class DiagnosticsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            var version = commandData.Application.Application.VersionNumber;
            var document = commandData.Application.ActiveUIDocument?.Document;
            TaskDialog.Show("Autodesk AI Bridge", $"Revit {version}\nDocument: {document?.Title ?? "none"}\nQueue: {RevitBridgeRuntime.Queue is not null}");
            return Result.Succeeded;
        }
    }
}
