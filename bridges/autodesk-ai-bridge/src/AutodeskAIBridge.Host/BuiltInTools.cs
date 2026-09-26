using AutodeskAIBridge.Core;

namespace AutodeskAIBridge.Host;

/// <summary>Registers safe, explicit Autodesk discovery and health tools.</summary>
public static class BuiltInTools
{
    public static void RegisterAll(ToolRegistry registry, IAutodeskInstanceRegistry instances, InstanceSelection? selection = null)
    {
        selection ??= new InstanceSelection();
        registry.Register(new DelegateTool(
            new ToolDescriptor("autodesk.list_instances", "List connected Revit and AutoCAD instances.", RiskCategory.ReadOnly, false, EmptySchema()),
            (_, _) => Task.FromResult(ToolResult.Ok(new { instances = instances.ListInstances() }))));

        registry.Register(new DelegateTool(
            new ToolDescriptor("autodesk.get_active_instance", "Get active instance for one Autodesk product.", RiskCategory.ReadOnly, false, ProductSchema()),
            (arguments, _) =>
            {
                var error = InputValidation.RequireProduct(arguments, out var product);
                if (error is not null) return Task.FromResult(new ToolResult(false, Error: error));
                var matches = instances.ListInstances().Where(i => i.Product.Equals(product, StringComparison.OrdinalIgnoreCase)).ToArray();
                return Task.FromResult(matches.Length == 0
                    ? ToolResult.Fail(BridgeErrorCodes.NoActiveSession, $"No connected {product} instance.")
                    : matches.Length > 1
                        ? ToolResult.Fail("AMBIGUOUS_TARGET", $"Multiple {product} instances are connected; select an instance.", details: new { instances = matches })
                        : ToolResult.Ok(matches[0]));
            }));

        registry.Register(new DelegateTool(
            new ToolDescriptor("autodesk.select_instance", "Select one connected Autodesk instance for later calls.", RiskCategory.ReadOnly, false, TargetSchema(), null, false, false),
            (arguments, _) =>
            {
                var error = InputValidation.RequireProduct(arguments, out var product);
                if (error is not null) return Task.FromResult(new ToolResult(false, Error: error));
                error = InputValidation.RequireString(arguments, "instanceId");
                if (error is not null) return Task.FromResult(new ToolResult(false, Error: error));
                var instanceId = (string)arguments["instanceId"]!;
                var target = new AutodeskTarget(product, instanceId, arguments.TryGetValue("documentId", out var document) ? document as string : null);
                return instances.Resolve(target) is null
                    ? Task.FromResult(ToolResult.Fail(BridgeErrorCodes.NoActiveSession, $"Instance '{instanceId}' is not connected."))
                    : Select(target);

                Task<ToolResult> Select(AutodeskTarget selected) { selection.Select(selected); return Task.FromResult(ToolResult.Ok(selected)); }
            }));

        registry.Register(new DelegateTool(
            new ToolDescriptor("autodesk.get_active_document", "Get active document for one connected Autodesk instance.", RiskCategory.ReadOnly, false, ProductSchema()),
            (arguments, _) =>
            {
                var error = InputValidation.RequireProduct(arguments, out var product);
                if (error is not null) return Task.FromResult(new ToolResult(false, Error: error));
                var target = selection.Get(product);
                var candidates = instances.ListInstances().Where(i => i.Product.Equals(product, StringComparison.OrdinalIgnoreCase)).ToArray();
                var info = target is null && candidates.Length == 1 ? candidates[0] : target is not null ? instances.Resolve(target)?.GetInstanceInfo() : null;
                if (info is null && candidates.Length > 1) return Task.FromResult(ToolResult.Fail(BridgeErrorCodes.AmbiguousTarget, $"Multiple {product} instances are connected; select an instance."));
                if (info is null) return Task.FromResult(ToolResult.Fail(BridgeErrorCodes.NoActiveSession, $"No connected {product} instance."));
                var document = info.Documents.FirstOrDefault(d => d.IsActive) ?? info.Documents.FirstOrDefault();
                return Task.FromResult(document is null ? ToolResult.Fail(BridgeErrorCodes.NoOpenDocument, $"No open {product} document.") : ToolResult.Ok(new { instanceId = info.InstanceId, document }));
            }));

        registry.Register(new DelegateTool(
            new ToolDescriptor("autodesk.list_documents", "List documents for connected product instances.", RiskCategory.ReadOnly, false, ProductSchema()),
            (arguments, _) =>
            {
                var error = InputValidation.RequireProduct(arguments, out var product);
                if (error is not null) return Task.FromResult(new ToolResult(false, Error: error));
                var result = instances.ListInstances()
                    .Where(i => i.Product.Equals(product, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(i => i.Documents.Select(d => new { instanceId = i.InstanceId, document = d }))
                    .ToArray();
                return Task.FromResult(ToolResult.Ok(new { documents = result }));
            }));

        registry.Register(new DelegateTool(
            new ToolDescriptor("autodesk.capabilities", "Report connected products and explicitly registered tool names.", RiskCategory.ReadOnly, false, EmptySchema()),
            (_, _) => Task.FromResult(ToolResult.Ok(new
            {
                revit = Capability("revit", instances),
                autocad = Capability("autocad", instances)
            }))));

        registry.Register(new DelegateTool(
            new ToolDescriptor("autodesk.health", "Return host-side Autodesk connection health.", RiskCategory.ReadOnly, false, EmptySchema()),
            (_, _) => Task.FromResult(ToolResult.Ok(new
            {
                host = "HOST_RUNNING",
                revit = Health("revit", instances),
                autocad = Health("autocad", instances)
            }))));

        RegisterProductInfo(registry, instances, selection, "revit");
        RegisterProductInfo(registry, instances, selection, "autocad");
        RegisterForwardedTools(registry, instances, selection);
    }

    private static void RegisterProductInfo(ToolRegistry registry, IAutodeskInstanceRegistry instances, InstanceSelection selection, string product)
    {
        registry.Register(new DelegateTool(
            new ToolDescriptor($"{product}.get_document_info", $"Get active {product} document information.", RiskCategory.ReadOnly, true, EmptySchema(), product, true, true),
            (_, context) =>
            {
                var target = context.Target ?? selection.Get(product);
                var matches = instances.ListInstances().Where(i => i.Product.Equals(product, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (target is null && matches.Length > 1)
                    return Task.FromResult(ToolResult.Fail("AMBIGUOUS_TARGET", $"Multiple {product} instances are connected; select an instance.", details: new { instances = matches }));
                var adapter = target is null ? matches.FirstOrDefault() is { } active ? instances.Resolve(new AutodeskTarget(product, active.InstanceId)) : null : instances.Resolve(target);
                if (adapter is null) return Task.FromResult(ToolResult.Fail($"{product.ToUpperInvariant()}_NOT_CONNECTED", $"No connected {product} instance."));
                return adapter.ExecuteAsync("get_document_info", new Dictionary<string, object?>(), context);
            }));

        registry.Register(new DelegateTool(
            new ToolDescriptor($"{product}.health", $"Check {product} connection health.", RiskCategory.ReadOnly, true, EmptySchema(), product, true, false, ImplementationStatus.Supported),
            (_, context) =>
            {
                var target = context.Target ?? selection.Get(product);
                var matches = instances.ListInstances().Where(i => i.Product.Equals(product, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (target is null && matches.Length > 1)
                    return Task.FromResult(ToolResult.Fail("AMBIGUOUS_TARGET", $"Multiple {product} instances are connected; select an instance.", details: new { instances = matches }));
                var info = target is not null
                    ? instances.Resolve(target)?.GetInstanceInfo()
                    : matches.FirstOrDefault();
                return Task.FromResult(info is null
                    ? ToolResult.Fail($"{product.ToUpperInvariant()}_NOT_CONNECTED", $"No connected {product} instance.")
                    : ToolResult.Ok(new { status = "PLUGIN_CONNECTED", api = info.ApiReady ? "API_READY" : "API_NOT_READY", instance = info }));
            }));
    }

    private static void RegisterForwardedTools(ToolRegistry registry, IAutodeskInstanceRegistry instances, InstanceSelection selection)
    {
        var revit = new[]
        {
            "get_application_info", "get_project_info", "get_units", "get_active_view", "list_views", "get_view",
            "list_levels", "get_level", "list_sheets", "get_sheet", "list_rooms", "get_room", "list_families",
            "list_family_types", "get_family_type", "query_elements", "find_elements", "get_element", "get_elements",
            "get_element_parameters", "describe_element", "create_level", "rename_level", "delete_level", "create_wall",
            "modify_wall", "delete_wall", "create_floor", "place_family_instance", "move_element", "move_elements",
            "rotate_element", "copy_element", "copy_elements", "delete_elements", "set_parameter", "set_parameters",
            "bulk_set_parameters", "change_type", "load_family", "activate_family_type", "create_room", "set_room_parameters",
            "create_view", "create_floor_plan", "duplicate_view", "rename_view", "create_sheet", "place_view_on_sheet",
            "create_text_note", "save", "save_as", "execute_batch"
        };
        var autocad = new[]
        {
            "get_application_info", "list_documents", "get_document_info", "get_active_document", "get_database_info", "get_units",
            "list_layers", "get_layer", "create_layer", "modify_layer", "query_entities", "get_entity", "get_entities", "create_line",
            "create_polyline", "create_circle", "create_arc", "create_rectangle", "create_text", "create_mtext", "list_blocks",
            "get_block_definition", "list_block_references", "insert_block", "modify_block_reference", "move_entities", "copy_entities",
            "rotate_entities", "scale_entities", "erase_entities", "change_layer", "set_properties", "create_dimension", "regen", "save", "save_as"
        };
        foreach (var operation in revit) RegisterForwarded(registry, instances, selection, "revit", operation);
        foreach (var operation in autocad) RegisterForwarded(registry, instances, selection, "autocad", operation);
    }

    private static void RegisterForwarded(ToolRegistry registry, IAutodeskInstanceRegistry instances, InstanceSelection selection, string product, string operation)
    {
        var fullName = $"{product}.{operation}";
        if (registry.Resolve(fullName) is not null) return;
        var risk = operation switch
        {
            "save" or "save_as" => RiskCategory.FileWrite,
            "delete_elements" or "delete_wall" or "delete_layer" or "erase_entities" => RiskCategory.Destructive,
            "create_level" or "rename_level" or "create_layer" or "open_document" => RiskCategory.ModelEdit,
            _ when operation.StartsWith("create_", StringComparison.Ordinal) || operation.StartsWith("modify_", StringComparison.Ordinal) || operation.StartsWith("set_", StringComparison.Ordinal) || operation.Contains("move", StringComparison.Ordinal) || operation.Contains("copy", StringComparison.Ordinal) || operation.Contains("rotate", StringComparison.Ordinal) || operation.Contains("scale", StringComparison.Ordinal) || operation.Contains("change_", StringComparison.Ordinal) || operation.Contains("place_", StringComparison.Ordinal) || operation.Contains("load_", StringComparison.Ordinal) || operation.Contains("activate_", StringComparison.Ordinal) || operation.Contains("bulk_", StringComparison.Ordinal) || operation.Contains("batch_", StringComparison.Ordinal) || operation == "insert_block" => RiskCategory.ModelEdit,
            _ => RiskCategory.ReadOnly
        };
        registry.Register(new DelegateTool(
            new ToolDescriptor(fullName, $"Execute {product} operation '{operation}'. Coordinates use explicit units, default mm; model edits support dryRun where applicable.", risk, true, OperationSchema(product, operation), product, true, !operation.StartsWith("list_", StringComparison.Ordinal) && operation is not ("get_application_info" or "get_units" or "list_documents"), ImplementationStatus.SourceImplementedRuntimeUnverified),
            async (arguments, context) =>
            {
                var target = ResolveTarget(arguments, context, product, instances, selection, out var error);
                if (error is not null) return new ToolResult(false, Error: error);
                var adapter = instances.Resolve(target!);
                return adapter is null
                    ? ToolResult.Fail($"{product.ToUpperInvariant()}_NOT_CONNECTED", $"No connected {product} instance.")
                    : await adapter.ExecuteAsync(fullName, arguments, context).ConfigureAwait(false);
            }));
    }

    private static AutodeskTarget? ResolveTarget(IReadOnlyDictionary<string, object?> arguments, ToolCallContext context, string product, IAutodeskInstanceRegistry instances, InstanceSelection selection, out BridgeError? error)
    {
        error = null;
        if (context.Target is { } supplied && supplied.Product.Equals(product, StringComparison.OrdinalIgnoreCase)) return supplied;
        if (selection.Get(product) is { } selected) return selected;
        if (arguments.TryGetValue("instanceId", out var instanceValue) && instanceValue is string instanceId && !string.IsNullOrWhiteSpace(instanceId))
            return new AutodeskTarget(product, instanceId, arguments.TryGetValue("documentId", out var documentValue) ? documentValue as string : null);
        var matches = instances.ListInstances().Where(i => i.Product.Equals(product, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 1) return new AutodeskTarget(product, matches[0].InstanceId);
        error = matches.Length == 0
            ? new BridgeError(BridgeErrorCodes.NoActiveSession, $"No connected {product} instance.")
            : new BridgeError("AMBIGUOUS_TARGET", $"Multiple {product} instances are connected; provide instanceId.", Details: new { instances = matches });
        return null;
    }

    private static object Capability(string product, IAutodeskInstanceRegistry instances)
        => new
        {
            connected = instances.GetActive(product) is not null,
            version = instances.GetActive(product)?.ProductVersion,
            tools = MockOperationCatalog.For(product).Select(operation => new { name = $"{product}.{operation}", status = operation.Equals("health", StringComparison.OrdinalIgnoreCase) ? "SUPPORTED" : "SOURCE_IMPLEMENTED_RUNTIME_UNVERIFIED" }).ToArray()
        };

    private static object Health(string product, IAutodeskInstanceRegistry instances)
        => instances.GetActive(product) is { } info
            ? new { status = info.ApiReady ? "API_READY" : "API_NOT_READY", instanceId = (string?)info.InstanceId, document = info.ActiveDocumentName }
            : new { status = "NO_CONNECTED_INSTANCE", instanceId = (string?)null, document = (string?)null };

    private static IReadOnlyDictionary<string, object?> EmptySchema()
        => new Dictionary<string, object?> { ["type"] = "object", ["properties"] = new Dictionary<string, object?>() };

    private static IReadOnlyDictionary<string, object?> ProductSchema()
        => new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["required"] = new[] { "product" },
            ["properties"] = new Dictionary<string, object?>
            {
                ["product"] = StringProperty(new[] { "revit", "autocad" })
            }
        };

    private static IReadOnlyDictionary<string, object?> GenericSchema()
        => new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["additionalProperties"] = true,
            ["properties"] = new Dictionary<string, object?>
            {
                ["instanceId"] = new { type = "string" },
                ["documentId"] = new { type = "string" },
                ["dryRun"] = new { type = "boolean" }
            }
        };

    private static IReadOnlyDictionary<string, object?> TargetSchema()
        => new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["required"] = new[] { "product", "instanceId" },
            ["properties"] = new Dictionary<string, object?>
            {
                ["product"] = StringProperty(new[] { "revit", "autocad" }),
                ["instanceId"] = StringProperty(),
                ["documentId"] = StringProperty()
            },
            ["additionalProperties"] = false
        };

    private static IReadOnlyDictionary<string, object?> OperationSchema(string product, string operation)
    {
        var properties = new Dictionary<string, object?>
        {
            ["instanceId"] = StringProperty(), ["documentId"] = StringProperty(), ["timeoutMs"] = NumberProperty(), ["dryRun"] = new Dictionary<string, object?> { ["type"] = "boolean" },
            ["elementId"] = StringProperty(), ["uniqueId"] = StringProperty(), ["entityId"] = StringProperty(),
            ["elementIds"] = ArrayProperty(1), ["entityIds"] = ArrayProperty(1), ["name"] = StringProperty(), ["elevation"] = NumberProperty(), ["elevationMm"] = NumberProperty(), ["unit"] = StringProperty(new[] { "mm", "cm", "m", "in", "ft" }),
            ["path"] = StringProperty(), ["typeId"] = StringProperty(), ["parameter"] = StringProperty(), ["value"] = new Dictionary<string, object?>(),
            ["points"] = ArrayProperty(2), ["boundaryLoops"] = ArrayProperty(1), ["position"] = CoordinateProperty(), ["start"] = CoordinateProperty(), ["end"] = CoordinateProperty(), ["translation"] = CoordinateProperty(),
            ["viewId"] = StringProperty(), ["sheetId"] = StringProperty(), ["text"] = StringProperty(), ["content"] = StringProperty(), ["overwrite"] = new Dictionary<string, object?> { ["type"] = "boolean" }, ["atomic"] = new Dictionary<string, object?> { ["type"] = "boolean" },
            ["confirmed"] = new Dictionary<string, object?> { ["type"] = "boolean" }, ["levelId"] = StringProperty(), ["level"] = StringProperty(), ["category"] = StringProperty(), ["builtInCategory"] = StringProperty(), ["class"] = StringProperty(), ["familyName"] = StringProperty(), ["typeName"] = StringProperty(), ["uniqueIds"] = ArrayProperty(1), ["parameterFilters"] = ArrayProperty(1), ["view"] = StringProperty(), ["boundingBox"] = new Dictionary<string, object?> { ["type"] = "object" }, ["height"] = NumberProperty(), ["offset"] = NumberProperty(), ["structural"] = new Dictionary<string, object?> { ["type"] = "boolean" }, ["flip"] = new Dictionary<string, object?> { ["type"] = "boolean" }, ["hostId"] = StringProperty(), ["host"] = StringProperty(), ["hosted"] = new Dictionary<string, object?> { ["type"] = "boolean" }, ["structuralType"] = StringProperty(), ["familyTypeId"] = StringProperty(), ["familyTypeName"] = StringProperty(), ["titleBlockId"] = StringProperty(), ["titleBlockName"] = StringProperty(), ["viewType"] = StringProperty(), ["duplicateOption"] = StringProperty(), ["sheetNumber"] = StringProperty(), ["sheetName"] = StringProperty(), ["parameters"] = new Dictionary<string, object?> { ["type"] = "object" }, ["items"] = ArrayProperty(1), ["operations"] = ArrayProperty(1), ["axisStart"] = CoordinateProperty(), ["axisEnd"] = CoordinateProperty(), ["angle"] = NumberProperty(), ["angleUnit"] = StringProperty(new[] { "deg", "rad" }), ["basePoint"] = CoordinateProperty(), ["scale"] = NumberProperty(), ["center"] = CoordinateProperty(), ["radius"] = NumberProperty(), ["startAngle"] = NumberProperty(), ["endAngle"] = NumberProperty(), ["closed"] = new Dictionary<string, object?> { ["type"] = "boolean" }, ["constantWidth"] = NumberProperty(), ["rotation"] = NumberProperty(), ["textStyle"] = StringProperty(), ["layer"] = StringProperty(), ["layerName"] = StringProperty(), ["entityType"] = StringProperty(), ["dxfType"] = StringProperty(), ["color"] = NumberProperty(), ["linetype"] = StringProperty(), ["lineweight"] = StringProperty(), ["transparency"] = NumberProperty(), ["boundingRegion"] = new Dictionary<string, object?> { ["type"] = "object" }, ["blockName"] = StringProperty(), ["attributes"] = new Dictionary<string, object?> { ["type"] = "object" }, ["dimensionLine"] = CoordinateProperty(), ["compact"] = new Dictionary<string, object?> { ["type"] = "boolean" }, ["textContent"] = StringProperty(), ["objectId"] = StringProperty()
        };
        var required = operation switch
        {
            "create_level" or "create_layer" => new[] { "name" },
            "create_wall" or "create_line" => new[] { "start", "end" },
            "create_polyline" or "create_rectangle" => new[] { "points" },
            "create_circle" or "create_arc" => new[] { "center", "radius" },
            "create_floor" => new[] { "boundaryLoops" },
            "create_text" or "create_mtext" => new[] { "content", "position" },
            "create_text_note" => new[] { "text", "position", "viewId" },
            "create_sheet" => new[] { "sheetNumber", "sheetName" },
            "save_as" => new[] { "path", "overwrite" },
            _ => Array.Empty<string>()
        };
        return new Dictionary<string, object?> { ["type"] = "object", ["properties"] = properties, ["required"] = required, ["additionalProperties"] = false };
    }

    private static IReadOnlyDictionary<string, object?> StringProperty(IReadOnlyList<string>? values = null)
    {
        var property = new Dictionary<string, object?> { ["type"] = "string", ["minLength"] = 1 };
        if (values is not null) property["enum"] = values;
        return property;
    }

    private static IReadOnlyDictionary<string, object?> NumberProperty() => new Dictionary<string, object?> { ["type"] = "number" };
    private static IReadOnlyDictionary<string, object?> ArrayProperty(int minimum) => new Dictionary<string, object?> { ["type"] = "array", ["minItems"] = minimum };
    private static IReadOnlyDictionary<string, object?> CoordinateProperty() => new Dictionary<string, object?> { ["type"] = "object", ["required"] = new[] { "x", "y" }, ["properties"] = new Dictionary<string, object?> { ["x"] = NumberProperty(), ["y"] = NumberProperty(), ["z"] = NumberProperty(), ["unit"] = StringProperty(new[] { "mm", "cm", "m", "in", "ft" }) } };
}
