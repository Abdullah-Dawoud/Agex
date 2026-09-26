using System.Globalization;
using System.Text.Json;

namespace AutodeskAIBridge.Core;

/// <summary>Deterministic stateful mock. Mirrors core operation shapes without pretending to be Autodesk runtime.</summary>
public sealed class MockAutodeskAdapter : IAutodeskAdapter
{
    private readonly object _gate = new();
    private readonly List<string> _operations = [];
    private readonly Dictionary<string, MockObject> _objects = new(StringComparer.OrdinalIgnoreCase);
    private AutodeskInstanceInfo _instance;
    private int _nextId = 1;
    private string? _savedPath;

    public MockAutodeskAdapter(string product, string instanceId, string version, string? documentName = null)
    {
        if (!product.Equals("revit", StringComparison.OrdinalIgnoreCase) && !product.Equals("autocad", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Product must be revit or autocad.", nameof(product));
        Product = product.ToLowerInvariant();
        var documents = documentName is null ? (IReadOnlyList<AutodeskDocumentInfo>)Array.Empty<AutodeskDocumentInfo>() : new[] { new AutodeskDocumentInfo($"{instanceId}-doc-1", documentName, documentName, true, false, false) };
        _instance = new AutodeskInstanceInfo(Product, instanceId, version, documents.FirstOrDefault()?.DocumentId, documents.FirstOrDefault()?.Name, true, documents, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, MockOperationCatalog.For(Product));
        if (Product == "autocad") _objects[(_nextId++).ToString(CultureInfo.InvariantCulture)] = new MockObject("1", $"{Product}-block-1", "block", "BridgeBlock", 0);
    }
    public string Product { get; }
    public bool IsConnected { get; private set; } = true;
    public IReadOnlyList<string> Operations { get { lock (_gate) return _operations.ToArray(); } }
    public AutodeskInstanceInfo GetInstanceInfo() { lock (_gate) return IsConnected ? _instance : _instance with { ApiReady = false }; }
    public void Disconnect() { lock (_gate) IsConnected = false; }
    public void Reconnect() { lock (_gate) { IsConnected = true; _instance = _instance with { ApiReady = true, LastHeartbeat = DateTimeOffset.UtcNow }; } }
    public void SetApiReady(bool ready) { lock (_gate) _instance = _instance with { ApiReady = ready }; }

    public Task<ToolResult> ExecuteAsync(string operation, IReadOnlyDictionary<string, object?> values, ToolCallContext context)
    {
        lock (_gate)
        {
            _operations.Add(operation);
            if (!IsConnected) return Task.FromResult(ToolResult.Fail(BridgeErrorCodes.PluginDisconnected, "Mock plugin is disconnected."));
            if (!_instance.ApiReady) return Task.FromResult(ToolResult.Fail("API_NOT_READY", $"{Product} API is not ready."));
            if (context.CancellationToken.IsCancellationRequested) return Task.FromCanceled<ToolResult>(context.CancellationToken);
            var name = operation.Contains('.') ? operation.Substring(operation.IndexOf('.') + 1) : operation;
            return Task.FromResult(Product == "revit" ? ExecuteRevit(name, values, context) : ExecuteAutoCad(name, values, context));
        }
    }

    private ToolResult ExecuteRevit(string operation, IReadOnlyDictionary<string, object?> values, ToolCallContext context)
    {
        if (operation is "health" or "get_application_info") return ToolResult.Ok(new { product = "Revit", version = _instance.ProductVersion, apiReady = true });
        if (operation is "get_document_info" or "get_project_info") return ToolResult.Ok(_instance.Documents.FirstOrDefault());
        if (operation == "get_units") return ToolResult.Ok(new { lengthUnit = "mm", angleUnit = "deg" });
        if (operation == "list_levels") return ToolResult.Ok(_objects.Values.Where(item => item.Kind == "level").Select(item => item.ToData()).ToArray());
        if (operation is "query_elements" or "find_elements") return PageRevit(operation, values);
        if (operation is "get_element" or "describe_element") { var item = Find(values, "elementId", "uniqueId"); return item is null ? ToolResult.Fail(BridgeErrorCodes.ElementNotFound, "Element not found.") : ToolResult.Ok(item.ToData()); }
        if (operation == "get_elements") return ToolResult.Ok(StringValues(values, "elementIds").Select(id => FindById(id)?.ToData()).Where(value => value is not null).ToArray());
        if (operation == "get_element_parameters") { var item = Find(values, "elementId", "uniqueId"); return item is null ? ToolResult.Fail(BridgeErrorCodes.ElementNotFound, "Element not found.") : ToolResult.Ok(item.Parameters); }
        if (operation == "get_level") { var item = Find(values, "level", "elementId", "uniqueId"); return item?.Kind == "level" ? ToolResult.Ok(item.ToData()) : ToolResult.Fail(BridgeErrorCodes.LevelNotFound, "Level not found."); }
        if (operation == "create_level") { var name = Required(values, "name"); if (name is null) return ToolResult.Fail(BridgeErrorCodes.InvalidRequest, "name is required."); return CreateObject(context, "level", name, values, new { elevationMm = Number(values, "elevation", Number(values, "elevationMm", 0)) }); }
        if (operation == "rename_level") return Rename(context, values, "level", BridgeErrorCodes.LevelNotFound);
        if (operation == "create_wall") return CreateObject(context, "wall", "Wall", values, new { category = "Walls", level = Required(values, "levelId") ?? Required(values, "level") });
        if (operation == "modify_wall") return Modify(context, values, "wall");
        if (operation == "delete_wall") return Delete(context, values, "wall");
        if (operation == "create_floor") return CreateObject(context, "floor", "Floor", values, new { category = "Floors" });
        if (operation is "list_views" or "list_sheets" or "list_rooms" or "list_families" or "list_family_types") return ToolResult.Ok(_objects.Values.Where(item => item.Kind == operation.Substring(5, operation.Length - 6) || item.Kind == operation.Substring(5)).Select(item => item.ToData()).ToArray());
        if (operation is "get_view" or "get_room" or "get_sheet" or "get_family_type") { var item = Find(values, "elementId", "uniqueId"); return item is null ? ToolResult.Fail(BridgeErrorCodes.ElementNotFound, "Target not found.") : ToolResult.Ok(item.ToData()); }
        if (operation is "create_view" or "create_floor_plan") return CreateObject(context, "view", Required(values, "name") ?? "Floor Plan", values, null);
        if (operation == "create_sheet") return CreateObject(context, "sheet", Required(values, "sheetName") ?? Required(values, "name") ?? "Sheet", values, new { number = Required(values, "sheetNumber") ?? Required(values, "number") });
        if (operation == "place_view_on_sheet") return CreateObject(context, "viewport", "Viewport", values, null);
        if (operation == "create_room") return CreateObject(context, "room", Required(values, "name") ?? "Room", values, null);
        if (operation is "list_families" or "list_family_types" or "get_family_type" or "place_family_instance" or "change_type" or "load_family" or "activate_family_type") return operation == "place_family_instance" ? CreateObject(context, "familyInstance", "Family Instance", values, null) : ToolResult.Ok(new { operation, implemented = true });
        if (operation is "set_parameter" or "set_parameters" or "bulk_set_parameters" or "set_room_parameters") return SetParameters(context, values);
        if (operation is "move_element" or "move_elements" or "rotate_element" or "copy_element" or "copy_elements") return Transform(context, values, operation);
        if (operation is "delete_elements") return Delete(context, values, null);
        if (operation is "create_text_note" or "save" or "save_as" or "execute_batch") return operation == "create_text_note" ? CreateObject(context, "textNote", Required(values, "text") ?? Required(values, "content") ?? "", values, null) : FileOperation(context, values, operation);
        return Unsupported(operation);
    }

    private ToolResult ExecuteAutoCad(string operation, IReadOnlyDictionary<string, object?> values, ToolCallContext context)
    {
        if (operation is "health" or "get_application_info") return ToolResult.Ok(new { product = "AutoCAD", version = _instance.ProductVersion, apiReady = true });
        if (operation is "get_document_info" or "get_active_document" or "list_documents") return ToolResult.Ok(_instance.Documents);
        if (operation is "get_database_info" or "get_units") return ToolResult.Ok(new { insUnits = "Millimeters", measurement = "Metric" });
        if (operation is "list_layers" or "list_blocks") return ToolResult.Ok(_objects.Values.Where(item => item.Kind == (operation == "list_layers" ? "layer" : "block")).Select(item => item.ToData()).ToArray());
        if (operation == "get_layer") { var item = Find(values, "layer", "name", "handle"); return item?.Kind == "layer" ? ToolResult.Ok(item.ToData()) : ToolResult.Fail(BridgeErrorCodes.EntityNotFound, "Layer not found."); }
        if (operation is "get_block_definition" or "list_block_references") { var item = Find(values, "blockName", "name"); return item is null ? ToolResult.Fail(BridgeErrorCodes.BlockNotFound, "Block not found.") : ToolResult.Ok(item.ToData()); }
        if (operation is "query_entities" or "get_entity" or "get_entities") return ToolResult.Ok(_objects.Values.Where(item => item.Kind == "entity").Select(item => item.ToData()).ToArray());
        if (operation == "create_layer") { var name = Required(values, "name"); if (name is null) return ToolResult.Fail(BridgeErrorCodes.InvalidRequest, "name is required."); if (_objects.Values.Any(item => item.Kind == "layer" && item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return ToolResult.Fail("LAYER_EXISTS", "Layer already exists."); return CreateObject(context, "layer", name, values, null); }
        if (operation == "modify_layer") return Rename(context, values, "layer", BridgeErrorCodes.EntityNotFound);
        if (operation is "create_line" or "create_polyline" or "create_circle" or "create_arc" or "create_rectangle" or "create_text" or "create_mtext") return CreateObject(context, "entity", operation, values, new { layer = Required(values, "layer") ?? "0" });
        if (operation is "insert_block") { var name = Required(values, "blockName") ?? Required(values, "name"); if (string.IsNullOrWhiteSpace(name) || !_objects.Values.Any(item => item.Kind == "block" && item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return ToolResult.Fail(BridgeErrorCodes.BlockNotFound, "Block not found."); return CreateObject(context, "entity", "insert_block", values, null); }
        if (operation is "move_entities" or "copy_entities" or "rotate_entities" or "scale_entities" or "change_layer" or "set_properties") return Transform(context, values, operation);
        if (operation is "erase_entities") return Delete(context, values, "entity");
        if (operation is "create_dimension" or "regen" or "save" or "save_as") return operation is "save" or "save_as" ? FileOperation(context, values, operation) : ToolResult.Ok(new { operation, completed = true });
        return Unsupported(operation);
    }

    private ToolResult PageRevit(string operation, IReadOnlyDictionary<string, object?> values) { var filter = Required(values, "name"); var items = _objects.Values.Where(item => operation == "list_levels" ? item.Kind == "level" : item.Kind != "layer").Where(item => string.IsNullOrWhiteSpace(filter) || item.Name.IndexOf(filter!, StringComparison.OrdinalIgnoreCase) >= 0).Select(item => item.ToData()).ToArray(); return new ToolResult(true, new { items, offset = (int)Number(values, "offset", 0), limit = (int)Number(values, "limit", 100), totalCount = items.Length, hasMore = false }); }
    private ToolResult CreateObject(ToolCallContext context, string kind, string name, IReadOnlyDictionary<string, object?> values, object? extra) { var objectId = (_nextId++).ToString(CultureInfo.InvariantCulture); var item = new MockObject(objectId, $"{Product}-{kind}-{objectId}", kind, name, Number(values, "elevation", 0)); item.Parameters["name"] = name; item.Parameters["properties"] = extra; foreach (var pair in values) item.Parameters[pair.Key] = pair.Value; if (context.DryRun) return ToolResult.Ok(new { valid = true, planned = item.ToData() }); _objects[objectId] = item; return ToolResult.Ok(item.ToData(), new ChangeSet(new object[] { objectId }, Array.Empty<object>(), Array.Empty<object>())); }
    private ToolResult Rename(ToolCallContext context, IReadOnlyDictionary<string, object?> values, string kind, string errorCode) { var item = Find(values, kind, "elementId", "uniqueId", "name", "handle"); var name = Required(values, "name") ?? Required(values, "newName"); if (item is null || item.Kind != kind) return ToolResult.Fail(errorCode, "Target not found."); if (name is null) return ToolResult.Fail(BridgeErrorCodes.InvalidRequest, "name is required."); if (context.DryRun) return ToolResult.Ok(new { valid = true, planned = new { id = item.Id, name } }); item.Name = name; return ToolResult.Ok(item.ToData(), new ChangeSet(Array.Empty<object>(), new object[] { item.Id }, Array.Empty<object>())); }
    private ToolResult Modify(ToolCallContext context, IReadOnlyDictionary<string, object?> values, string kind) { var item = FindAny(values, "elementId", "uniqueId"); return item?.Kind == kind ? context.DryRun ? ToolResult.Ok(new { valid = true, planned = item.Id }) : ToolResult.Ok(item.ToData(), new ChangeSet(Array.Empty<object>(), new object[] { item.Id }, Array.Empty<object>())) : ToolResult.Fail(BridgeErrorCodes.ElementNotFound, "Target not found."); }
    private ToolResult SetParameters(ToolCallContext context, IReadOnlyDictionary<string, object?> values) { var item = FindAny(values, "elementId", "uniqueId"); var parameter = Required(values, "parameter"); if (item is null) return ToolResult.Fail(BridgeErrorCodes.ElementNotFound, "Target not found."); if (parameter is null) return ToolResult.Fail(BridgeErrorCodes.ParameterNotFound, "parameter is required."); if (context.DryRun) return ToolResult.Ok(new { valid = true, planned = item.Id }); item.Parameters[parameter] = values.TryGetValue("value", out var value) ? value : null; return ToolResult.Ok(new { elementId = item.Id, parameter, value }, new ChangeSet(Array.Empty<object>(), new object[] { item.Id }, Array.Empty<object>())); }
    private ToolResult Transform(ToolCallContext context, IReadOnlyDictionary<string, object?> values, string operation) { var ids = StringValues(values, Product == "revit" ? "elementIds" : "entityIds"); if (ids.Count == 0 && Required(values, Product == "revit" ? "elementId" : "entityId") is { } single) ids = new[] { single }; var targets = ids.Select(FindById).ToArray(); if (targets.Any(item => item is null)) return ToolResult.Fail(Product == "revit" ? BridgeErrorCodes.ElementNotFound : BridgeErrorCodes.EntityNotFound, "Target not found."); if (context.DryRun) return ToolResult.Ok(new { valid = true, planned = ids }); if (operation.IndexOf("copy", StringComparison.OrdinalIgnoreCase) >= 0) { var created = new List<object>(); foreach (var target in targets!) { var clone = target!.Clone((_nextId++).ToString(CultureInfo.InvariantCulture)); _objects[clone.Id] = clone; created.Add(clone.Id); } return ToolResult.Ok(new { copied = created }, new ChangeSet(created, Array.Empty<object>(), Array.Empty<object>())); } return ToolResult.Ok(new { modified = ids }, new ChangeSet(Array.Empty<object>(), ids.Cast<object>().ToArray(), Array.Empty<object>())); }
    private ToolResult Delete(ToolCallContext context, IReadOnlyDictionary<string, object?> values, string? kind) { var key = Product == "revit" ? "elementIds" : "entityIds"; var ids = StringValues(values, key); if (ids.Count == 0 && Required(values, Product == "revit" ? "elementId" : "entityId") is { } single) ids = new[] { single }; var targets = ids.Select(FindById).Where(item => item is not null && (kind is null || item.Kind == kind)).ToArray(); if (targets.Length != ids.Count) return ToolResult.Fail(Product == "revit" ? BridgeErrorCodes.ElementNotFound : BridgeErrorCodes.EntityNotFound, "Target not found."); if (context.DryRun) return ToolResult.Ok(new { valid = true, planned = ids }); foreach (var target in targets) _objects.Remove(target!.Id); return ToolResult.Ok(new { deleted = ids }, new ChangeSet(Array.Empty<object>(), Array.Empty<object>(), ids.Cast<object>().ToArray())); }
    private ToolResult FileOperation(ToolCallContext context, IReadOnlyDictionary<string, object?> values, string operation) { var path = Required(values, "path") ?? _instance.ActiveDocumentName; if (operation == "save_as" && string.IsNullOrWhiteSpace(path)) return ToolResult.Fail(BridgeErrorCodes.InvalidPath, "path is required."); if (context.DryRun) return ToolResult.Ok(new { valid = true, planned = new { path } }); _savedPath = path; return ToolResult.Ok(new { saved = true, path }); }
    /// <summary>Finds an object of any kind by id or unique id.</summary>
    private MockObject? FindAny(IReadOnlyDictionary<string, object?> values, params string[] keys)
    {
        foreach (var key in keys)
            if (Required(values, key) is { } value && _objects.Values.FirstOrDefault(candidate => candidate.Id.Equals(value, StringComparison.OrdinalIgnoreCase) || candidate.UniqueId.Equals(value, StringComparison.OrdinalIgnoreCase)) is { } item)
                return item;
        return null;
    }

    private MockObject? Find(IReadOnlyDictionary<string, object?> values, string kind, params string[] keys) { foreach (var key in keys) if (Required(values, key) is { } value) { var item = _objects.Values.FirstOrDefault(candidate => candidate.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase) && (candidate.Id.Equals(value, StringComparison.OrdinalIgnoreCase) || candidate.UniqueId.Equals(value, StringComparison.OrdinalIgnoreCase) || candidate.Name.Equals(value, StringComparison.OrdinalIgnoreCase))); if (item is not null) return item; } return null; }
    private MockObject? FindById(string id) => _objects.TryGetValue(id, out var value) ? value : _objects.Values.FirstOrDefault(item => item.UniqueId.Equals(id, StringComparison.OrdinalIgnoreCase));
    private static string? Required(IReadOnlyDictionary<string, object?> values, string key) => values.TryGetValue(key, out var value) && value is not null && !string.IsNullOrWhiteSpace(value.ToString()) ? value.ToString()!.Trim('"') : null;
    private static double Number(IReadOnlyDictionary<string, object?> values, string key, double fallback) => values.TryGetValue(key, out var value) && double.TryParse(value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : fallback;
    private static IReadOnlyList<string> StringValues(IReadOnlyDictionary<string, object?> values, string key) { if (!values.TryGetValue(key, out var value) || value is null) return Array.Empty<string>(); if (value is IEnumerable<object?> objects) return objects.Select(item => item?.ToString() ?? string.Empty).Where(item => item.Length > 0).ToArray(); return value.ToString()!.Trim('[', ']', '"').Split(',').Select(item => item.Trim(' ', '"')).Where(item => item.Length > 0).ToArray(); }
    private static ToolResult Unsupported(string operation) => ToolResult.Fail(BridgeErrorCodes.UnsupportedOperation, $"Mock adapter does not expose non-MVP operation '{operation}'.");

    private sealed class MockObject
    {
        public MockObject(string id, string uniqueId, string kind, string name, double value) { Id = id; UniqueId = uniqueId; Kind = kind; Name = name; Value = value; }
        public string Id { get; }
        public string UniqueId { get; }
        public string Kind { get; }
        public string Name { get; set; }
        public double Value { get; }
        public Dictionary<string, object?> Parameters { get; } = new(StringComparer.OrdinalIgnoreCase);
        public MockObject Clone(string id) { var clone = new MockObject(id, $"{ProductPlaceholder}-{Kind}-{id}", Kind, Name, Value); foreach (var pair in Parameters) clone.Parameters[pair.Key] = pair.Value; return clone; }
        private static string ProductPlaceholder => "mock";
        public MockObjectDto ToData() => new(Id, UniqueId, Kind, Name, Value, Parameters);
    }
}

public sealed record MockObjectDto(string Id, string UniqueId, string Kind, string Name, double Value, IReadOnlyDictionary<string, object?>? Parameters = null);

public static class MockOperationCatalog
{
    private static readonly string[] Revit = ["health", "get_application_info", "get_document_info", "get_project_info", "get_units", "get_active_view", "query_elements", "find_elements", "get_element", "get_elements", "get_element_parameters", "describe_element", "list_levels", "get_level", "create_level", "rename_level", "delete_level", "create_wall", "modify_wall", "delete_wall", "create_floor", "list_families", "list_family_types", "get_family_type", "place_family_instance", "change_type", "load_family", "activate_family_type", "move_element", "move_elements", "rotate_element", "copy_element", "copy_elements", "delete_elements", "set_parameter", "set_parameters", "bulk_set_parameters", "list_rooms", "get_room", "create_room", "set_room_parameters", "list_views", "get_view", "create_floor_plan", "create_view", "duplicate_view", "rename_view", "list_sheets", "get_sheet", "create_sheet", "place_view_on_sheet", "create_text_note", "save", "save_as", "execute_batch"];
    private static readonly string[] AutoCad = ["health", "get_application_info", "list_documents", "get_document_info", "get_active_document", "get_database_info", "get_units", "list_layers", "get_layer", "create_layer", "modify_layer", "query_entities", "get_entity", "get_entities", "create_line", "create_polyline", "create_circle", "create_arc", "create_rectangle", "create_text", "create_mtext", "list_blocks", "get_block_definition", "list_block_references", "insert_block", "modify_block_reference", "move_entities", "copy_entities", "rotate_entities", "scale_entities", "erase_entities", "change_layer", "set_properties", "create_dimension", "regen", "save", "save_as"];
    public static IReadOnlyList<string> For(string product) => product.Equals("revit", StringComparison.OrdinalIgnoreCase) ? Revit : AutoCad;
}

public sealed class MockAutodeskInstanceRegistry : IAutodeskSessionRegistry
{
    private readonly object _gate = new(); private readonly List<IAutodeskAdapter> _adapters;
    public MockAutodeskInstanceRegistry(IEnumerable<IAutodeskAdapter> adapters) => _adapters = adapters.ToList();
    public IReadOnlyList<AutodeskInstanceInfo> ListInstances() { lock (_gate) return _adapters.Select(adapter => adapter.GetInstanceInfo()).ToArray(); }
    public IAutodeskAdapter? Resolve(AutodeskTarget target) { lock (_gate) return _adapters.FirstOrDefault(adapter => adapter.Product.Equals(target.Product, StringComparison.OrdinalIgnoreCase) && adapter.GetInstanceInfo().InstanceId.Equals(target.InstanceId, StringComparison.OrdinalIgnoreCase)); }
    public AutodeskInstanceInfo? GetActive(string product) { lock (_gate) { var matches = _adapters.Where(adapter => adapter.Product.Equals(product, StringComparison.OrdinalIgnoreCase)).Select(adapter => adapter.GetInstanceInfo()).ToArray(); return matches.Length == 1 ? matches[0] : null; } }
    public void Register(IAutodeskAdapter adapter) { lock (_gate) { if (_adapters.All(existing => !existing.Product.Equals(adapter.Product, StringComparison.OrdinalIgnoreCase) || existing.GetInstanceInfo().InstanceId != adapter.GetInstanceInfo().InstanceId)) _adapters.Add(adapter); } }
    public bool Unregister(string product, string instanceId) { lock (_gate) return _adapters.RemoveAll(adapter => adapter.Product.Equals(product, StringComparison.OrdinalIgnoreCase) && adapter.GetInstanceInfo().InstanceId.Equals(instanceId, StringComparison.OrdinalIgnoreCase)) > 0; }
    public void MarkHeartbeat(string product, string instanceId, AutodeskInstanceInfo? refreshedInfo = null) { }
    public int ExpireStale(DateTimeOffset now) => 0;
}
