using System.Text.Json;
using AutodeskAIBridge.Core;
using AutodeskAIBridge.Host;
using AutodeskAIBridge.Protocol;

namespace AutodeskAIBridge.Tests;

internal static class Program
{
    private static int _passed;

    public static async Task<int> Main()
    {
        await RunAsync("auth proof", TestAuthAsync);
        await RunAsync("frame serialization and malformed rejection", TestFrameAsync);
        await RunAsync("mock IPC transport", TestMockIpcAsync);
        await RunAsync("plugin reconnect and live document discovery", TestPluginReconnectAsync);
        await RunAsync("unit conversion", TestUnitsAsync);
        await RunAsync("config merge, validation, backup", TestConfigAsync);
        await RunAsync("mock Revit lifecycle", TestMockRevitAsync);
        await RunAsync("mock AutoCAD lifecycle", TestMockAutoCadAsync);
        await RunAsync("mock MVP workflows", TestMockMvpWorkflowsAsync);
        await RunAsync("mock MVP validation and dry-run", TestMockMvpValidationAsync);
        await RunAsync("session registry ambiguity and expiry", TestRegistryAsync);
        await RunAsync("tool registration completeness", TestToolRegistrationAsync);
        await RunAsync("routing and selection", TestRoutingAsync);
        await RunAsync("safe mode", TestPermissionPolicyAsync);
        await RunAsync("dry run", TestDryRunAsync);
        await RunAsync("timeout and cancellation", TestCancellationAsync);
        await RunAsync("MCP stdio", TestMcpStdioAsync);
        Console.WriteLine($"Passed: {_passed}");
        return Environment.ExitCode;
    }

    private static Task TestAuthAsync()
    {
        var proof = IpcAuthentication.CreateProof("secret", "nonce");
        Assert(IpcAuthentication.Verify("secret", "nonce", proof), "valid proof rejected");
        Assert(!IpcAuthentication.Verify("wrong", "nonce", proof), "wrong proof accepted");
        return Task.CompletedTask;
    }

    private static Task TestFrameAsync()
    {
        var envelope = new IpcEnvelope { Kind = IpcMessageKind.Request, MessageId = "m1", RequestId = "r1", Payload = JsonSerializer.SerializeToElement(new { ok = true }, ProtocolJson.Options) };
        var line = IpcFrameCodec.Serialize(envelope);
        Assert(IpcFrameCodec.Deserialize(line).RequestId == "r1", "frame round trip failed");
        AssertThrows<InvalidDataException>(() => IpcFrameCodec.Deserialize("{bad"), "malformed frame accepted");
        return Task.CompletedTask;
    }

    private static Task TestUnitsAsync()
    {
        Assert(Math.Abs(BridgeUnits.ToMetres(25.4, "mm") - 0.0254) < 0.0000001, "mm conversion failed");
        Assert(Math.Abs(BridgeUnits.FromMetres(1, "ft") - 3.280839895) < 0.0000001, "feet conversion failed");
        Assert(Math.Abs(BridgeUnits.ToRadians(180, "deg") - Math.PI) < 0.0000001, "degree conversion failed");
        AssertThrows<ArgumentException>(() => BridgeUnits.ToMetres(1, "bogus"), "invalid unit accepted");
        return Task.CompletedTask;
    }

    private static async Task TestMockIpcAsync()
    {
        var pair = MockIpcTransport.CreatePair();
        await pair.A.ConnectAsync(CancellationToken.None); await pair.B.ConnectAsync(CancellationToken.None);
        var message = new IpcEnvelope { Kind = IpcMessageKind.Heartbeat, MessageId = "m1" };
        await pair.A.SendAsync(message, CancellationToken.None);
        Assert((await pair.B.ReceiveAsync(CancellationToken.None))?.MessageId == "m1", "mock IPC round trip failed");
        await pair.A.DisposeAsync(); await pair.B.DisposeAsync();
    }

    private static async Task TestPluginReconnectAsync()
    {
        var options = new NamedPipeOptions
        {
            PipeName = "AutodeskAIBridge-test-" + Guid.NewGuid().ToString("N"), SharedSecret = "test-secret-" + Guid.NewGuid().ToString("N"),
            HandshakeTimeout = TimeSpan.FromSeconds(2), MaxReconnectAttempts = 2, HeartbeatInterval = TimeSpan.FromMilliseconds(500)
        };
        var registry = new HostSessionRegistry();
        using var firstHost = new CancellationTokenSource();
        using var pluginStop = new CancellationTokenSource();
        Task StartHost(CancellationToken token) => new NamedPipeServer(options, (_, _) => Task.FromResult<IpcEnvelope?>(null),
            connection => { registry.Register(new IpcAutodeskAdapter(connection)); return Task.CompletedTask; },
            connection => { registry.Unregister(connection.Info.Product, connection.Info.ClientInstanceId); return Task.CompletedTask; }).RunAsync(token);
        var host = StartHost(firstHost.Token);
        await using var plugin = new PluginIpcClient(options, "autocad", "2027", "test-instance");
        var receivedOperation = "";
        var currentDocument = "Example.dwg";
        var running = plugin.RunWithReconnectAsync((request, _) =>
        {
            receivedOperation = request.Operation;
            if (currentDocument.Length == 0)
                return Task.FromResult(new BridgeResponse { RequestId = request.RequestId, Success = false,
                    Error = new BridgeErrorDto(BridgeErrorCodes.NoOpenDocument, "No drawing is open.", true) });
            return Task.FromResult(new BridgeResponse { RequestId = request.RequestId, Success = true,
                Data = new { name = currentDocument, path = "C:/drawings/" + currentDocument, isReadOnly = false, isModified = false } });
        }, pluginStop.Token);
        async Task WaitForConnectionAsync()
        {
            for (var attempt = 0; attempt < 100 && registry.ListInstances().Count == 0; attempt++) await Task.Delay(50);
            Assert(registry.ListInstances().Count == 1, "plugin did not connect to host");
        }
        try
        {
            await WaitForConnectionAsync();
            var adapter = (IpcAutodeskAdapter)registry.Resolve(new AutodeskTarget("autocad", "test-instance"))!;
            await adapter.RefreshDocumentAsync(new ToolCallContext("read", "test", null, CancellationToken.None));
            Assert(receivedOperation == "autocad.get_document_info", "host did not qualify plugin operation");
            Assert(registry.ListInstances().Single().ActiveDocumentName == "Example.dwg", "live document not discovered");
            currentDocument = "Changed.dwg";
            var tools = new ToolRegistry();
            BuiltInTools.RegisterAll(tools, registry);
            var documents = await new ToolDispatcher(tools).DispatchAsync("autodesk.list_documents", new Dictionary<string, object?> { ["product"] = "autocad" },
                new ToolCallContext("documents", "test", null, CancellationToken.None));
            Assert(documents.Success && registry.ListInstances().Single().ActiveDocumentName == "Changed.dwg", "document tools used stale startup metadata");
            currentDocument = "";
            await new ToolDispatcher(tools).DispatchAsync("autodesk.list_instances", new Dictionary<string, object?>(),
                new ToolCallContext("closed", "test", null, CancellationToken.None));
            Assert(registry.ListInstances().Single().ActiveDocumentName is null, "closed document remained connected");
            firstHost.Cancel();
            await host;
            using var secondHost = new CancellationTokenSource();
            host = StartHost(secondHost.Token);
            await WaitForConnectionAsync();
            secondHost.Cancel();
            await host;
        }
        finally
        {
            pluginStop.Cancel();
            await running;
        }
    }

    private static Task TestConfigAsync()
    {
        var merged = AntiGravityConfigManager.Merge("{\"other\":true,\"mcpServers\":{\"existing\":{\"command\":\"x\"}}}", "autodesk-ai-bridge", "bridge.exe");
        Assert(AntiGravityConfigManager.Validate(merged, out _), "merged config invalid");
        Assert(merged.Contains("existing", StringComparison.Ordinal), "existing config entry lost");
        var dir = Path.Combine(Path.GetTempPath(), "AutodeskAIBridgeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "mcp_settings.json");
        File.WriteAllText(path, merged);
        var backup = AntiGravityConfigManager.Backup(path, DateTimeOffset.Parse("2026-01-02T03:04:05.006+00:00"));
        Assert(File.Exists(backup), "backup missing");
        var configureBackup = AntiGravityConfigManager.Configure(path, "C:\\Bridge\\AutodeskAIBridge.Host.exe", new[] { "--stdio" });
        Assert(File.Exists(configureBackup), "configure backup missing");
        var configured = File.ReadAllText(path);
        Assert(configured.Contains("C:\\\\Bridge\\\\AutodeskAIBridge.Host.exe", StringComparison.Ordinal), "bridge entry missing after configure"); // JSON escapes the backslashes
        Assert(configured.Contains("existing", StringComparison.Ordinal), "unrelated server lost during configure");
        Assert(AntiGravityConfigManager.Remove(path), "bridge entry was not removed");
        Assert(!File.ReadAllText(path).Contains("C:\\Bridge\\AutodeskAIBridge.Host.exe", StringComparison.Ordinal), "bridge entry survived removal");
        Directory.Delete(dir, true);
        return Task.CompletedTask;
    }

    private static async Task TestMockRevitAsync()
    {
        var adapter = new MockAutodeskAdapter("revit", "r1", "2026", "sample.rvt");
        var context = new ToolCallContext("r", "c", new AutodeskTarget("revit", "r1"), CancellationToken.None);
        var created = await adapter.ExecuteAsync("revit.create_level", new Dictionary<string, object?> { ["name"] = "Level 1", ["elevationMm"] = 3000d }, context);
        Assert(created.Success && created.EffectiveChanges.Created.Count == 1, "level create failed");
        var level = ((MockObjectDto)created.Data!).Id;
        var listed = await adapter.ExecuteAsync("revit.list_levels", new Dictionary<string, object?>(), context);
        Assert(listed.Success && listed.Data is Array array && array.Length == 1, "level query failed");
        var renamed = await adapter.ExecuteAsync("revit.rename_level", new Dictionary<string, object?> { ["elementId"] = level, ["name"] = "Level Renamed" }, context);
        Assert(renamed.Success, "level rename failed");
        var deleted = await adapter.ExecuteAsync("revit.delete_elements", new Dictionary<string, object?> { ["elementIds"] = new[] { level } }, context);
        Assert(deleted.Success && deleted.EffectiveChanges.Deleted.Count == 1, "level delete failed");
    }

    private static async Task TestMockAutoCadAsync()
    {
        var adapter = new MockAutodeskAdapter("autocad", "a1", "2026", "sample.dwg");
        var context = new ToolCallContext("r", "c", new AutodeskTarget("autocad", "a1"), CancellationToken.None);
        var layer = await adapter.ExecuteAsync("autocad.create_layer", new Dictionary<string, object?> { ["name"] = "A-WALL" }, context);
        Assert(layer.Success, "layer create failed");
        var line = await adapter.ExecuteAsync("autocad.create_line", new Dictionary<string, object?>(), context);
        var id = ((MockObjectDto)line.Data!).Id;
        var moved = await adapter.ExecuteAsync("autocad.move_entities", new Dictionary<string, object?> { ["entityIds"] = new[] { id } }, context);
        Assert(moved.Success, "entity move failed");
        var erased = await adapter.ExecuteAsync("autocad.erase_entities", new Dictionary<string, object?> { ["entityIds"] = new[] { id } }, context);
        Assert(erased.Success && erased.EffectiveChanges.Deleted.Count == 1, "entity erase failed");
    }

    private static async Task TestMockMvpWorkflowsAsync()
    {
        var revit = new MockAutodeskAdapter("revit", "rw", "2026", "workflow.rvt");
        var rc = new ToolCallContext("r", "c", new AutodeskTarget("revit", "rw"), CancellationToken.None);
        var level = await revit.ExecuteAsync("revit.create_level", new Dictionary<string, object?> { ["name"] = "Level 1", ["elevation"] = 0d }, rc);
        var levelId = ((MockObjectDto)level.Data!).Id;
        var wall = await revit.ExecuteAsync("revit.create_wall", new Dictionary<string, object?> { ["levelId"] = levelId, ["start"] = new { x = 0, y = 0, unit = "mm" }, ["end"] = new { x = 5000, y = 0, unit = "mm" } }, rc);
        var wallId = ((MockObjectDto)wall.Data!).Id;
        Assert(wall.Success, "workflow wall create failed");
        Assert((await revit.ExecuteAsync("revit.query_elements", new Dictionary<string, object?> { ["name"] = "Wall" }, rc)).Success, "workflow wall query failed");
        Assert((await revit.ExecuteAsync("revit.set_parameter", new Dictionary<string, object?> { ["elementId"] = wallId, ["parameter"] = "Mark", ["value"] = "W-1" }, rc)).Success, "workflow parameter set failed");
        Assert((await revit.ExecuteAsync("revit.move_element", new Dictionary<string, object?> { ["elementId"] = wallId }, rc)).Success, "workflow move failed");
        var view = await revit.ExecuteAsync("revit.create_floor_plan", new Dictionary<string, object?> { ["name"] = "Level 1 Plan" }, rc);
        var viewId = ((MockObjectDto)view.Data!).Id;
        var sheet = await revit.ExecuteAsync("revit.create_sheet", new Dictionary<string, object?> { ["sheetNumber"] = "A-101", ["sheetName"] = "Plan" }, rc);
        var sheetId = ((MockObjectDto)sheet.Data!).Id;
        Assert((await revit.ExecuteAsync("revit.place_view_on_sheet", new Dictionary<string, object?> { ["viewId"] = viewId, ["sheetId"] = sheetId }, rc)).Success, "workflow viewport failed");
        Assert((await revit.ExecuteAsync("revit.delete_wall", new Dictionary<string, object?> { ["elementId"] = wallId }, rc)).Success, "workflow wall delete failed");

        var autocad = new MockAutodeskAdapter("autocad", "aw", "2027", "workflow.dwg");
        var ac = new ToolCallContext("r", "c", new AutodeskTarget("autocad", "aw"), CancellationToken.None);
        Assert((await autocad.ExecuteAsync("autocad.create_layer", new Dictionary<string, object?> { ["name"] = "A-WALL" }, ac)).Success, "workflow layer failed");
        var polyline = await autocad.ExecuteAsync("autocad.create_polyline", new Dictionary<string, object?> { ["points"] = new[] { new { x = 0, y = 0, unit = "mm" }, new { x = 1000, y = 0, unit = "mm" } } }, ac);
        var entityId = ((MockObjectDto)polyline.Data!).Id;
        Assert((await autocad.ExecuteAsync("autocad.query_entities", new Dictionary<string, object?>(), ac)).Success, "workflow entity query failed");
        Assert((await autocad.ExecuteAsync("autocad.change_layer", new Dictionary<string, object?> { ["entityIds"] = new[] { entityId }, ["layer"] = "A-WALL" }, ac)).Success, "workflow layer change failed");
        Assert((await autocad.ExecuteAsync("autocad.move_entities", new Dictionary<string, object?> { ["entityIds"] = new[] { entityId } }, ac)).Success, "workflow move failed");
        Assert((await autocad.ExecuteAsync("autocad.copy_entities", new Dictionary<string, object?> { ["entityIds"] = new[] { entityId } }, ac)).Success, "workflow copy failed");
        Assert((await autocad.ExecuteAsync("autocad.save_as", new Dictionary<string, object?> { ["path"] = "workflow-copy.dwg" }, ac)).Success, "workflow save-as failed");
        Assert((await autocad.ExecuteAsync("autocad.erase_entities", new Dictionary<string, object?> { ["entityIds"] = new[] { entityId } }, ac)).Success, "workflow erase failed");
    }

    private static async Task TestMockMvpValidationAsync()
    {
        var revit = new MockAutodeskAdapter("revit", "rv", "2026", "validation.rvt");
        var context = new ToolCallContext("r", "c", new AutodeskTarget("revit", "rv"), CancellationToken.None, true);
        var invalidLevel = await revit.ExecuteAsync("revit.create_level", new Dictionary<string, object?>(), context);
        Assert(!invalidLevel.Success && invalidLevel.Error?.Code == BridgeErrorCodes.InvalidRequest, "missing level name was accepted");
        var dryWall = await revit.ExecuteAsync("revit.create_wall", new Dictionary<string, object?> { ["start"] = new { x = 0 }, ["end"] = new { x = 1 } }, context);
        Assert(dryWall.Success && revit.Operations.Count == 2, "dry-run wall validation failed");
        revit.Disconnect();
        var disconnected = await revit.ExecuteAsync("revit.get_element", new Dictionary<string, object?>(), context);
        Assert(!disconnected.Success && disconnected.Error?.Code == BridgeErrorCodes.PluginDisconnected, "disconnected routing was not mapped");

        var autocad = new MockAutodeskAdapter("autocad", "av", "2027", "validation.dwg");
        var autoContext = new ToolCallContext("r", "c", new AutodeskTarget("autocad", "av"), CancellationToken.None, true);
        var dryPolyline = await autocad.ExecuteAsync("autocad.create_polyline", new Dictionary<string, object?> { ["points"] = new[] { new { x = 0 }, new { x = 1 } } }, autoContext);
        Assert(dryPolyline.Success, "dry-run polyline failed");
        var missingEntity = await autocad.ExecuteAsync("autocad.erase_entities", new Dictionary<string, object?> { ["entityIds"] = new[] { "missing" } }, autoContext with { DryRun = false });
        Assert(!missingEntity.Success && missingEntity.Error?.Code == BridgeErrorCodes.EntityNotFound, "missing entity was accepted");
    }

    private static Task TestRegistryAsync()
    {
        var first = new MockAutodeskAdapter("revit", "r1", "2026", "one.rvt");
        var second = new MockAutodeskAdapter("revit", "r2", "2026", "two.rvt");
        var registry = new HostSessionRegistry(TimeSpan.FromSeconds(1));
        registry.Register(first); registry.Register(second);
        Assert(registry.ListInstances().Count == 2, "multi-instance registry failed");
        Assert(registry.GetActive("revit") is null, "ambiguous active instance guessed");
        Assert(registry.ExpireStale(DateTimeOffset.UtcNow.AddMinutes(1)) == 2, "stale expiry failed");
        var session = new MockPluginSession(new MockAutodeskAdapter("autocad", "a1", "2026", "one.dwg"));
        session.Disconnect();
        Assert(!session.GetInstanceInfo().ApiReady, "plugin disconnect state failed");
        session.Reconnect();
        Assert(session.GetInstanceInfo().ApiReady, "plugin reconnect state failed");
        return Task.CompletedTask;
    }

    private static Task TestToolRegistrationAsync()
    {
        var registry = new ToolRegistry();
        BuiltInTools.RegisterAll(registry, new MockAutodeskInstanceRegistry(Array.Empty<IAutodeskAdapter>()));
        var names = registry.List().Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var required in new[] { "autodesk.capabilities", "autodesk.health", "autodesk.list_instances", "autodesk.get_active_instance", "autodesk.select_instance", "autodesk.list_documents", "autodesk.get_active_document", "revit.create_level", "revit.load_family", "autocad.create_line", "autocad.save_as" })
            Assert(names.Contains(required), $"missing tool {required}");
        Assert(registry.List().All(t => t.InputSchema.ContainsKey("type")), "tool schema missing type");
        return Task.CompletedTask;
    }

    private static async Task TestRoutingAsync()
    {
        var adapter = new MockAutodeskAdapter("revit", "r1", "2026", "sample.rvt");
        var registry = new MockAutodeskInstanceRegistry(new[] { adapter });
        var tools = new ToolRegistry();
        BuiltInTools.RegisterAll(tools, registry);
        var dispatcher = new ToolDispatcher(tools);
        var selected = await dispatcher.DispatchAsync("autodesk.select_instance", new Dictionary<string, object?> { ["product"] = "revit", ["instanceId"] = "r1" }, new("r", "c", null, CancellationToken.None));
        Assert(selected.Success, "instance selection failed");
        var result = await dispatcher.DispatchAsync("revit.create_level", new Dictionary<string, object?> { ["name"] = "L1", ["elevationMm"] = 0d }, new("r", "c", null, CancellationToken.None));
        Assert(result.Success, "selected target routing failed");
    }

    private static Task TestPermissionPolicyAsync()
    {
        var policy = new PermissionPolicy(new PermissionOptions { SafeMode = true });
        var descriptor = new ToolDescriptor("edit", "edit", RiskCategory.ModelEdit, false, new Dictionary<string, object?>());
        Assert(policy.Validate(descriptor, new Dictionary<string, object?>())?.Code == "SAFE_MODE_BLOCKED", "safe mode did not block edit");
        return Task.CompletedTask;
    }

    private static async Task TestDryRunAsync()
    {
        var adapter = new MockAutodeskAdapter("revit", "r1", "2026", "sample.rvt");
        var result = await adapter.ExecuteAsync("revit.create_level", new Dictionary<string, object?> { ["name"] = "L1", ["elevationMm"] = 1d }, new("r", "c", null, CancellationToken.None, true));
        Assert(result.Success && adapter.Operations.Count == 1 && ((MockAutodeskAdapter)adapter).GetInstanceInfo().Documents.Count == 1, "dry run failed");
        var list = await adapter.ExecuteAsync("revit.list_levels", new Dictionary<string, object?>(), new("r", "c", null, CancellationToken.None));
        Assert(list.Data is Array array && array.Length == 0, "dry run changed mock state");
    }

    private static async Task TestCancellationAsync()
    {
        var registry = new ToolRegistry();
        registry.Register(new SlowTool());
        var dispatcher = new ToolDispatcher(registry);
        using var cancellation = new CancellationTokenSource(20);
        var result = await dispatcher.DispatchAsync("test.slow", new Dictionary<string, object?>(), new("r", "c", null, cancellation.Token));
        Assert(result.Error?.Code == "CANCELLED", "cancellation was not mapped");
    }

    private static async Task TestMcpStdioAsync()
    {
        var registry = new ToolRegistry();
        BuiltInTools.RegisterAll(registry, new MockAutodeskInstanceRegistry(Array.Empty<IAutodeskAdapter>()));
        using var input = new StringReader("{\"jsonrpc\":\"2.0\",\"id\":\"1\",\"method\":\"initialize\"}\n{\"jsonrpc\":\"2.0\",\"id\":\"2\",\"method\":\"tools/list\"}\n");
        using var output = new StringWriter();
        await new McpStdioServer(new ToolDispatcher(registry), input, output).RunAsync(CancellationToken.None);
        var lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert(lines.Length == 2 && lines[1].Contains("autodesk_list_instances", StringComparison.Ordinal), "MCP output incomplete");
        Assert(lines[0].Contains("\"id\":\"1\"", StringComparison.Ordinal) && !lines[0].Contains("\"error\"", StringComparison.Ordinal), "MCP response must keep the id type and omit error");
        Assert(!lines[0].Contains("\"resources\"", StringComparison.Ordinal), "Host must not advertise resource listing it does not implement");
        using var numeric = new StringReader("{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\"}}\n");
        using var numericOutput = new StringWriter();
        await new McpStdioServer(new ToolDispatcher(registry), numeric, numericOutput).RunAsync(CancellationToken.None);
        Assert(numericOutput.ToString().Contains("\"id\":7,", StringComparison.Ordinal) && numericOutput.ToString().Contains("2025-06-18", StringComparison.Ordinal), "numeric id or protocol version not echoed");
    }

    private sealed class SlowTool : IBridgeTool
    {
        public ToolDescriptor Descriptor { get; } = new("test.slow", "slow", RiskCategory.ReadOnly, false, new Dictionary<string, object?> { ["type"] = "object" });
        public async Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> arguments, ToolCallContext context) { await Task.Delay(500, context.CancellationToken); return ToolResult.Ok(); }
    }

    private static async Task RunAsync(string name, Func<Task> test)
    {
        try { await test(); _passed++; Console.WriteLine($"PASS {name}"); }
        catch (Exception exception) { Console.Error.WriteLine($"FAIL {name}: {exception}"); Environment.ExitCode = 1; }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void AssertThrows<T>(Action action, string message) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException(message); }
}
