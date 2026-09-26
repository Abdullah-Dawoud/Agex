#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using AutodeskAIBridge.Core;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;

namespace AutodeskAIBridge.AutoCAD;

public static class AutoCadBridgeRuntime
{
    private static readonly object Sync = new();
    private static AutoCadDispatcher? _dispatcher;
    private static DocumentCollection? _documents;
    private static AutoCadPluginSession? _session;

    public static void Start(DocumentCollection documents)
    {
        lock (Sync)
        {
            if (_dispatcher is not null)
                return;
            _documents = documents;
            _dispatcher = new AutoCadDispatcher(documents);
            documents.DocumentActivated += OnDocumentActivated;
            documents.DocumentCreated += OnDocumentCreated;
            documents.DocumentToBeDestroyed += OnDocumentToBeDestroyed;
            var settings = BridgeRuntimeSettings.TryLoad();
            var secret = Environment.GetEnvironmentVariable("AUTODESK_AI_BRIDGE_SECRET") ?? settings?.SharedSecret;
            if (!string.IsNullOrWhiteSpace(secret))
            {
                _session = new AutoCadPluginSession(Environment.GetEnvironmentVariable("AUTODESK_AI_BRIDGE_PIPE") ?? settings?.PipeName ?? "AutodeskAIBridge", secret, Application.GetSystemVariable("ACADVER")?.ToString() ?? "unknown");
                _ = RunSessionAsync(_session);
            }
        }
    }

    public static void Stop()
    {
        lock (Sync)
        {
            if (_documents is not null)
            {
                _documents.DocumentActivated -= OnDocumentActivated;
                _documents.DocumentCreated -= OnDocumentCreated;
                _documents.DocumentToBeDestroyed -= OnDocumentToBeDestroyed;
            }
            _documents = null;
            _session?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _session = null;
            _dispatcher = null;
        }
    }

    public static AutoCadDispatcher Dispatcher => _dispatcher ?? throw new InvalidOperationException("AutoCAD bridge is not initialized.");

    public static void ShowDiagnostics()
    {
        var document = Application.DocumentManager.MdiActiveDocument;
        document?.Editor.WriteMessage($"\nAutodesk AI Bridge: API ready; active document: {document.Name}\n");
    }

    private static void OnDocumentActivated(object sender, DocumentCollectionEventArgs args) { }
    private static void OnDocumentCreated(object sender, DocumentCollectionEventArgs args) { }
    private static void OnDocumentToBeDestroyed(object sender, DocumentCollectionEventArgs args) { }
    private static async Task RunSessionAsync(AutoCadPluginSession session)
    {
        try { await session.RunAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception exception) { System.Diagnostics.Debug.WriteLine($"Autodesk AI Bridge AutoCAD session stopped: {exception.GetType().Name}"); }
    }
}
