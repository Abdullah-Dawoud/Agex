namespace AutodeskAIBridge.Core;

/// <summary>Lifecycle wrapper used by host integration tests to model plugin reconnects.</summary>
public sealed class MockPluginSession : IAutodeskAdapter
{
    private readonly MockAutodeskAdapter _adapter;
    public MockPluginSession(MockAutodeskAdapter adapter) => _adapter = adapter;
    public string Product => _adapter.Product;
    public bool Connected { get; private set; } = true;
    public AutodeskInstanceInfo GetInstanceInfo() => _adapter.GetInstanceInfo() with { ApiReady = Connected && _adapter.GetInstanceInfo().ApiReady };
    public void Disconnect() { Connected = false; _adapter.Disconnect(); }
    public void Reconnect() { Connected = true; _adapter.Reconnect(); }
    public Task<ToolResult> ExecuteAsync(string operation, IReadOnlyDictionary<string, object?> parameters, ToolCallContext context)
        => Connected ? _adapter.ExecuteAsync(operation, parameters, context) : Task.FromResult(ToolResult.Fail(BridgeErrorCodes.PluginDisconnected, "Mock plugin is disconnected."));
}
