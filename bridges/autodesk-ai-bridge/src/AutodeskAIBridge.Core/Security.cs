namespace AutodeskAIBridge.Core;

/// <summary>Validates tool risk against current user permissions.</summary>
public sealed class PermissionPolicy
{
    private readonly PermissionOptions _options;

    public PermissionPolicy(PermissionOptions? options = null)
        => _options = options ?? new PermissionOptions();

    public PermissionOptions Options => _options;

    public BridgeError? Validate(ToolDescriptor descriptor, IReadOnlyDictionary<string, object?> arguments)
    {
        if (_options.SafeMode && descriptor.Risk != RiskCategory.ReadOnly)
            return new BridgeError("SAFE_MODE_BLOCKED", $"Tool '{descriptor.Name}' is blocked in safe mode.");

        var allowed = descriptor.Risk switch
        {
            RiskCategory.ReadOnly => _options.Read,
            RiskCategory.ModelEdit => _options.Edit,
            RiskCategory.FileWrite => _options.FileWrite,
            RiskCategory.Destructive => _options.Destructive,
            _ => false
        };

        if (!allowed)
            return new BridgeError("PERMISSION_DENIED", $"Permission category '{descriptor.Risk}' is disabled.");

        if (descriptor.Risk == RiskCategory.Destructive &&
            arguments.TryGetValue("elementIds", out var ids) &&
            ids is System.Collections.ICollection collection &&
            collection.Count > _options.MaxElementsPerDeleteWithoutConfirmation &&
            !ReadBoolean(arguments, "confirmed"))
        {
            return new BridgeError(
                "CONFIRMATION_REQUIRED",
                $"Delete affects {collection.Count} elements; explicit confirmation is required.",
                Details: new { maximumWithoutConfirmation = _options.MaxElementsPerDeleteWithoutConfirmation });
        }

        return null;
    }

    private static bool ReadBoolean(IReadOnlyDictionary<string, object?> values, string key)
        => values.TryGetValue(key, out var value) && value is bool boolean && boolean;
}

/// <summary>Validates common tool input constraints without Autodesk dependencies.</summary>
public static class InputValidation
{
    public static BridgeError? RequireString(IReadOnlyDictionary<string, object?> values, string name)
        => !values.TryGetValue(name, out var value) || value is not string text || string.IsNullOrWhiteSpace(text)
            ? new BridgeError(BridgeErrorCodes.InvalidRequest, $"Argument '{name}' must be a non-empty string.")
            : null;

    public static BridgeError? RequireProduct(IReadOnlyDictionary<string, object?> values, out string product)
    {
        product = string.Empty;
        var error = RequireString(values, "product");
        if (error is not null) return error;
        product = (string)values["product"]!;
        return product.Equals("revit", StringComparison.OrdinalIgnoreCase) ||
               product.Equals("autocad", StringComparison.OrdinalIgnoreCase)
            ? null
            : new BridgeError(BridgeErrorCodes.InvalidRequest, "Argument 'product' must be 'revit' or 'autocad'.");
    }
}
