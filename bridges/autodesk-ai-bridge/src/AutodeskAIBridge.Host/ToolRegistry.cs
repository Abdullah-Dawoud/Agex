using AutodeskAIBridge.Core;

namespace AutodeskAIBridge.Host;

/// <summary>Thread-safe registry for explicitly implemented tools.</summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<string, IBridgeTool> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public void Register(IBridgeTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        lock (_gate)
        {
            if (!_tools.TryAdd(tool.Descriptor.Name, tool))
                throw new InvalidOperationException($"Tool already registered: {tool.Descriptor.Name}");
        }
    }

    public IReadOnlyList<ToolDescriptor> List()
    {
        lock (_gate) return _tools.Values.Select(t => t.Descriptor).OrderBy(t => t.Name).ToArray();
    }

    public IBridgeTool? Resolve(string name)
    {
        lock (_gate) return _tools.GetValueOrDefault(name);
    }
}

/// <summary>Runs registered tools after policy validation.</summary>
public sealed class ToolDispatcher
{
    private readonly ToolRegistry _registry;
    private readonly PermissionPolicy _policy;

    public ToolDispatcher(ToolRegistry registry, PermissionPolicy? policy = null)
    {
        _registry = registry;
        _policy = policy ?? new PermissionPolicy();
    }

    public IReadOnlyList<ToolDescriptor> ListTools() => _registry.List();

    public async Task<ToolResult> DispatchAsync(
        string name,
        IReadOnlyDictionary<string, object?> arguments,
        ToolCallContext context)
    {
        var tool = _registry.Resolve(name);
        if (tool is null)
            return ToolResult.Fail("UNKNOWN_TOOL", $"Tool '{name}' is not registered.", details: new { tool = name });

        var permissionError = _policy.Validate(tool.Descriptor, arguments);
        if (permissionError is not null)
            return new ToolResult(false, Error: permissionError);

        var inputError = ToolInputValidator.Validate(tool.Descriptor.InputSchema, arguments);
        if (inputError is not null)
            return new ToolResult(false, Error: inputError);

        try
        {
            return await tool.ExecuteAsync(arguments, context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            return ToolResult.Fail("CANCELLED", "Tool execution was cancelled.");
        }
        catch (Exception exception)
        {
            return new ToolResult(false, Error: BridgeErrorMapper.FromException(exception));
        }
    }
}

internal static class ToolInputValidator
{
    public static BridgeError? Validate(IReadOnlyDictionary<string, object?> schema, IReadOnlyDictionary<string, object?> values)
    {
        if (schema.Count == 0) return null;
        if (schema.TryGetValue("required", out var required) && required is IEnumerable<string> names)
            foreach (var name in names)
                if (!values.TryGetValue(name, out var value) || value is null || value is string text && string.IsNullOrWhiteSpace(text))
                    return new BridgeError(BridgeErrorCodes.InvalidRequest, $"Argument '{name}' is required.");
        if (schema.TryGetValue("properties", out var propertyValue) && propertyValue is IReadOnlyDictionary<string, object?> properties)
            foreach (var pair in values)
            {
                if (!properties.TryGetValue(pair.Key, out var descriptor) || descriptor is not IReadOnlyDictionary<string, object?> typed)
                {
                    if (schema.TryGetValue("additionalProperties", out var additional) && additional is bool allow && !allow)
                        return new BridgeError(BridgeErrorCodes.InvalidRequest, $"Argument '{pair.Key}' is not allowed.");
                    continue;
                }
                if (!typed.TryGetValue("type", out var typeValue) || typeValue is not string type || pair.Value is null) continue;
                var valid = type switch
                {
                    "string" => pair.Value is string,
                    "boolean" => pair.Value is bool,
                    "number" => pair.Value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal,
                    "array" => pair.Value is System.Collections.IEnumerable and not string,
                    _ => true
                };
                if (!valid) return new BridgeError(BridgeErrorCodes.InvalidRequest, $"Argument '{pair.Key}' must be {type}.");
                if (pair.Value is string text && typed.TryGetValue("minLength", out var minLength) && int.TryParse(minLength?.ToString(), out var minimumLength) && text.Length < minimumLength)
                    return new BridgeError(BridgeErrorCodes.InvalidRequest, $"Argument '{pair.Key}' must not be empty.");
                if (pair.Value is System.Collections.ICollection collection && typed.TryGetValue("minItems", out var minItems) && int.TryParse(minItems?.ToString(), out var minimumItems) && collection.Count < minimumItems)
                    return new BridgeError(BridgeErrorCodes.InvalidRequest, $"Argument '{pair.Key}' must contain at least {minimumItems} item(s).");
                if (typed.TryGetValue("enum", out var enumValues) && enumValues is IEnumerable<string> allowed && !allowed.Contains(pair.Value.ToString() ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                    return new BridgeError(BridgeErrorCodes.InvalidRequest, $"Argument '{pair.Key}' has an invalid value.");
                var nestedError = ValidateNested(typed, pair.Value, pair.Key);
                if (nestedError is not null) return nestedError;
            }
        return null;
    }

    private static BridgeError? ValidateNested(IReadOnlyDictionary<string, object?> schema, object value, string path)
    {
        if (schema.TryGetValue("required", out var required) && required is IEnumerable<string> names && value is IReadOnlyDictionary<string, object?> map)
            foreach (var name in names)
                if (!map.TryGetValue(name, out var child) || child is null || child is string text && string.IsNullOrWhiteSpace(text))
                    return new BridgeError(BridgeErrorCodes.InvalidRequest, $"Argument '{path}.{name}' is required.");
        if (schema.TryGetValue("properties", out var propertiesValue) && propertiesValue is IReadOnlyDictionary<string, object?> properties && value is IReadOnlyDictionary<string, object?> objectValue)
            foreach (var pair in objectValue)
                if (properties.TryGetValue(pair.Key, out var childSchema) && childSchema is IReadOnlyDictionary<string, object?> typed && pair.Value is not null)
                {
                    if (typed.TryGetValue("type", out var type) && type is string expected && expected == "number" && !IsNumber(pair.Value)) return new BridgeError(BridgeErrorCodes.InvalidRequest, $"Argument '{path}.{pair.Key}' must be number.");
                    if (typed.TryGetValue("type", out type) && type is string stringType && stringType == "string" && pair.Value is not string) return new BridgeError(BridgeErrorCodes.InvalidRequest, $"Argument '{path}.{pair.Key}' must be string.");
                    if (typed.TryGetValue("enum", out var enumValue) && enumValue is IEnumerable<string> allowed && !allowed.Contains(pair.Value.ToString() ?? string.Empty, StringComparer.OrdinalIgnoreCase)) return new BridgeError(BridgeErrorCodes.InvalidRequest, $"Argument '{path}.{pair.Key}' has an invalid unit or enum value.");
                    var error = ValidateNested(typed, pair.Value, $"{path}.{pair.Key}");
                    if (error is not null) return error;
                }
        return null;
    }

    private static bool IsNumber(object value) => value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;
}

internal sealed class DelegateTool : IBridgeTool
{
    private readonly Func<IReadOnlyDictionary<string, object?>, ToolCallContext, Task<ToolResult>> _handler;

    public DelegateTool(ToolDescriptor descriptor, Func<IReadOnlyDictionary<string, object?>, ToolCallContext, Task<ToolResult>> handler)
    {
        Descriptor = descriptor;
        _handler = handler;
    }

    public ToolDescriptor Descriptor { get; }

    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> arguments, ToolCallContext context)
        => _handler(arguments, context);
}
