namespace AutodeskAIBridge.Core;

/// <summary>Stable error code vocabulary shared by host and product adapters.</summary>
public static class BridgeErrorCodes
{
    public const string NoActiveSession = "NO_ACTIVE_SESSION";
    public const string AmbiguousTarget = "AMBIGUOUS_TARGET";
    public const string UnsupportedVersion = "UNSUPPORTED_VERSION";
    public const string NoOpenDocument = "NO_OPEN_DOCUMENT";
    public const string DocumentNotActive = "DOCUMENT_NOT_ACTIVE";
    public const string DocumentReadOnly = "DOCUMENT_READ_ONLY";
    public const string ElementNotFound = "ELEMENT_NOT_FOUND";
    public const string EntityNotFound = "ENTITY_NOT_FOUND";
    public const string LevelNotFound = "LEVEL_NOT_FOUND";
    public const string ViewNotFound = "VIEW_NOT_FOUND";
    public const string SheetNotFound = "SHEET_NOT_FOUND";
    public const string FamilyNotFound = "FAMILY_NOT_FOUND";
    public const string TypeNotFound = "TYPE_NOT_FOUND";
    public const string AmbiguousType = "AMBIGUOUS_TYPE";
    public const string HostRequired = "HOST_REQUIRED";
    public const string ParameterNotFound = "PARAMETER_NOT_FOUND";
    public const string ParameterReadOnly = "PARAMETER_READ_ONLY";
    public const string InvalidParameterValue = "INVALID_PARAMETER_VALUE";
    public const string TypeParameterRequiresTypeTarget = "TYPE_PARAMETER_REQUIRES_TYPE_TARGET";
    public const string ElementPinned = "ELEMENT_PINNED";
    public const string ElementNotModifiable = "ELEMENT_NOT_MODIFIABLE";
    public const string InvalidGeometry = "INVALID_GEOMETRY";
    public const string InvalidBoundary = "INVALID_BOUNDARY";
    public const string RoomNotEnclosed = "ROOM_NOT_ENCLOSED";
    public const string InvalidCoordinate = "INVALID_COORDINATE";
    public const string FileAlreadyExists = "FILE_ALREADY_EXISTS";
    public const string BlockNotFound = "BLOCK_NOT_FOUND";
    public const string InvalidEntity = "INVALID_ENTITY";
    public const string BlockedByMissingApi = "BLOCKED_BY_MISSING_API";
    public const string InvalidUnit = "INVALID_UNIT";
    public const string InvalidRequest = "INVALID_REQUEST";
    public const string InvalidPath = "INVALID_PATH";
    public const string TransactionFailed = "TRANSACTION_FAILED";
    public const string DocumentLockFailed = "DOCUMENT_LOCK_FAILED";
    public const string Timeout = "TIMEOUT";
    public const string Cancelled = "CANCELLED";
    public const string PluginDisconnected = "PLUGIN_DISCONNECTED";
    public const string ProtocolVersionMismatch = "PROTOCOL_VERSION_MISMATCH";
    public const string AuthenticationFailed = "AUTHENTICATION_FAILED";
    public const string UnsupportedOperation = "UNSUPPORTED_OPERATION";

    public static BridgeError Create(string code, string message, bool recoverable = true, object? details = null)
        => new(code, message, recoverable, details);
}

/// <summary>Maps common transport and adapter exceptions into stable bridge errors.</summary>
public static class BridgeErrorMapper
{
    public static BridgeError FromException(Exception exception)
    {
        if (exception is OperationCanceledException)
            return new BridgeError(BridgeErrorCodes.Cancelled, "Operation was cancelled.");
        if (exception is TimeoutException)
            return new BridgeError(BridgeErrorCodes.Timeout, "Operation timed out.");
        if (exception is UnauthorizedAccessException)
            return new BridgeError(BridgeErrorCodes.AuthenticationFailed, "Authentication failed.", false);
        if (exception is IOException)
            return new BridgeError(BridgeErrorCodes.PluginDisconnected, "Plugin connection was lost.", true, new { exception = exception.GetType().Name });
        return new BridgeError(BridgeErrorCodes.InvalidRequest, "Operation failed.", true, new { exception = exception.GetType().Name });
    }
}
