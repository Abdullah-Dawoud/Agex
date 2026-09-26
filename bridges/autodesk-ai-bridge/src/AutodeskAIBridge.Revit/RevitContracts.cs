#nullable enable
using System;
using System.Collections.Generic;

namespace AutodeskAIBridge.Revit;

public enum RevitRiskCategory
{
    ReadOnly,
    ModelEdit,
    FileWrite,
    Destructive
}

public sealed class RevitRequest
{
    public string ProtocolVersion { get; set; } = "1.0";
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");
    public string Operation { get; set; } = string.Empty;
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public RevitRiskCategory Risk { get; set; } = RevitRiskCategory.ReadOnly;
    public bool DryRun { get; set; }
}

/// <summary>Typed coordinate supplied by host. Unit defaults to millimetres.</summary>
public sealed record RevitCoordinateDto(double X, double Y, double Z = 0, string Unit = "mm");

public sealed record RevitBoundingBoxDto(RevitCoordinateDto Min, RevitCoordinateDto Max, string Unit = "mm");

public sealed record RevitParameterFilterDto(string Name, string? Value = null, string Operator = "equals");

public sealed record RevitElementQueryDto(
    string? Category = null,
    string? BuiltInCategory = null,
    string? Class = null,
    string? FamilyName = null,
    string? TypeName = null,
    string? Level = null,
    IReadOnlyList<long>? ElementIds = null,
    IReadOnlyList<string>? UniqueIds = null,
    IReadOnlyList<RevitParameterFilterDto>? ParameterFilters = null,
    string? Name = null,
    string? View = null,
    RevitBoundingBoxDto? BoundingBox = null,
    int Offset = 0,
    int Limit = 100);

public sealed record RevitElementDto(
    long ElementId,
    string UniqueId,
    string Name,
    string? Category,
    long? CategoryId,
    string? Family,
    string? Type,
    long? TypeId,
    string? Level,
    long? LevelId,
    object? Location,
    object? BoundingBox,
    IReadOnlyDictionary<string, object?> Parameters,
    bool? Pinned,
    string? Workset);

public sealed record RevitParameterDto(
    string Name,
    long? Id,
    string StorageType,
    object? Value,
    string? DisplayValue,
    bool IsReadOnly,
    bool IsShared,
    bool IsInstance);

public sealed record RevitPagedResult<T>(IReadOnlyList<T> Items, int Offset, int Limit, int? TotalCount, bool HasMore);

public sealed record RevitFamilySummaryDto(long ElementId, string Name, bool IsInPlace, bool IsEditable);
public sealed record RevitFamilyTypeSummaryDto(long ElementId, string Name, string FamilyName, bool IsActive);
public sealed record RevitViewSummaryDto(long ElementId, string UniqueId, string Name, string ViewType, bool IsTemplate, long? LevelId);
public sealed record RevitSheetSummaryDto(long ElementId, string UniqueId, string Name, string Number, long? TitleBlockId);
public sealed record RevitRoomSummaryDto(long ElementId, string UniqueId, string Name, string Number, long? LevelId, string? Level, double? AreaSqM, double? PerimeterMm, object? Location, bool Placed, IReadOnlyList<object> Boundaries);
public sealed record RevitChangePlanDto(string Operation, IReadOnlyList<long> TargetIds, IReadOnlyList<string> Effects, bool DryRun);

/// <summary>Version boundary for APIs whose signatures changed between supported Revit releases.</summary>
public interface IRevitVersionAdapter
{
    string Version { get; }
    double ToInternalLength(double value, string unit);
    double FromInternalLength(double value, string unit);
    double ToInternalAngle(double value, string unit);
}

public sealed class RevitVersionAdapter : IRevitVersionAdapter
{
    public RevitVersionAdapter(string version) => Version = version;
    public string Version { get; }
    public double ToInternalLength(double value, string unit) => Autodesk.Revit.DB.UnitUtils.ConvertToInternalUnits(value, ToUnit(unit));
    public double FromInternalLength(double value, string unit) => Autodesk.Revit.DB.UnitUtils.ConvertFromInternalUnits(value, ToUnit(unit));
    public double ToInternalAngle(double value, string unit) => Autodesk.Revit.DB.UnitUtils.ConvertToInternalUnits(unit.StartsWith("deg", StringComparison.OrdinalIgnoreCase) ? value * Math.PI / 180d : value, Autodesk.Revit.DB.UnitTypeId.Radians);
    private static Autodesk.Revit.DB.ForgeTypeId ToUnit(string unit)
        => unit.Trim().ToLowerInvariant() switch
        {
            "mm" or "millimetre" or "millimetres" => Autodesk.Revit.DB.UnitTypeId.Millimeters,
            "cm" or "centimetre" or "centimetres" => Autodesk.Revit.DB.UnitTypeId.Centimeters,
            "m" or "metre" or "metres" => Autodesk.Revit.DB.UnitTypeId.Meters,
            "in" or "inch" or "inches" => Autodesk.Revit.DB.UnitTypeId.Inches,
            "ft" or "foot" or "feet" => Autodesk.Revit.DB.UnitTypeId.Feet,
            _ => throw new ArgumentException("Supported length unit: mm, cm, m, in, ft.", nameof(unit))
        };
}

public sealed class RevitResponse
{
    public string CorrelationId { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public bool Recoverable { get; set; } = true;
    public object? Data { get; set; }
    public object? ErrorDetails { get; set; }
    public List<string> Warnings { get; } = new();
    public List<long> ChangedElementIds { get; } = new();
    public List<long> CreatedElementIds { get; } = new();
    public List<long> ModifiedElementIds { get; } = new();
    public List<long> DeletedElementIds { get; } = new();

    public static RevitResponse Ok(RevitRequest request, object? data = null) => new()
    {
        RequestId = request.RequestId,
        CorrelationId = request.CorrelationId,
        Success = true,
        Data = data
    };

    public static RevitResponse Fail(RevitRequest request, string code, string message, object? details = null) => new()
    {
        RequestId = request.RequestId,
        CorrelationId = request.CorrelationId,
        Success = false,
        ErrorCode = code,
        ErrorMessage = message,
        ErrorDetails = details
    };
}

public sealed class RevitLevelSummary
{
    public long ElementId { get; set; }
    public string UniqueId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double ElevationMm { get; set; }
}

public sealed class RevitHealth
{
    public string Product { get; set; } = "Revit";
    public string ApiReady { get; set; } = "true";
    public string Version { get; set; } = string.Empty;
    public string DocumentTitle { get; set; } = string.Empty;
    public bool HasDocument { get; set; }
}
