#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace AutodeskAIBridge.AutoCAD;

public enum AutoCadRiskCategory { ReadOnly, ModelEdit, FileWrite, Destructive }

public sealed class AutoCadRequest
{
    public string ProtocolVersion { get; set; } = "1.0";
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");
    public string Operation { get; set; } = string.Empty;
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public AutoCadRiskCategory Risk { get; set; } = AutoCadRiskCategory.ReadOnly;
    public bool DryRun { get; set; }
}

public sealed record AutoCadCoordinateDto(double X, double Y, double Z = 0, string Unit = "mm");
public sealed record AutoCadPointListDto(IReadOnlyList<AutoCadCoordinateDto> Points, bool Closed = false, double? ConstantWidth = null);
public sealed record AutoCadEntityQueryDto(string? EntityType = null, string? Layer = null, int? Color = null, string? Linetype = null, string? Handle = null, string? ObjectId = null, string? TextContent = null, string? BlockName = null, int Offset = 0, int Limit = 100, bool Compact = false);

public sealed class AutoCadResponse
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
    public List<string> ChangedHandles { get; } = new();
    public List<string> CreatedHandles { get; } = new();
    public List<string> ModifiedHandles { get; } = new();
    public List<string> DeletedHandles { get; } = new();

    public static AutoCadResponse Ok(AutoCadRequest request, object? data = null) => new() { RequestId = request.RequestId, CorrelationId = request.CorrelationId, Success = true, Data = data };
    public static AutoCadResponse Fail(AutoCadRequest request, string code, string message, bool recoverable = true, object? details = null) => new() { RequestId = request.RequestId, CorrelationId = request.CorrelationId, Success = false, ErrorCode = code, ErrorMessage = message, Recoverable = recoverable, ErrorDetails = details };
}

public enum AutoCadImplementationStatus { Supported, SourceImplementedRuntimeUnverified, BlockedByApi, Unsupported }

public static class AutoCadOperationCatalog
{
    private static readonly Dictionary<string, AutoCadImplementationStatus> States = new(StringComparer.OrdinalIgnoreCase)
    {
        ["autocad.health"] = AutoCadImplementationStatus.Supported,
        ["autocad.get_application_info"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.list_documents"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.get_document_info"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.get_active_document"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.get_database_info"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.get_units"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.list_layers"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.get_layer"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.create_layer"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.modify_layer"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.query_entities"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.get_entity"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.get_entities"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.create_line"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.create_polyline"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.create_circle"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.create_arc"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.create_rectangle"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.create_text"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.create_mtext"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.list_blocks"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.get_block_definition"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.list_block_references"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.insert_block"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.modify_block_reference"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.move_entities"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.copy_entities"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.rotate_entities"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.scale_entities"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.erase_entities"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.change_layer"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.set_properties"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.create_dimension"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.regen"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.save"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified,
        ["autocad.save_as"] = AutoCadImplementationStatus.SourceImplementedRuntimeUnverified
    };
    public static IReadOnlyCollection<string> Supported => States.Keys.ToArray();
    public static IReadOnlyCollection<string> RuntimeOnly => States.Where(pair => pair.Value == AutoCadImplementationStatus.SourceImplementedRuntimeUnverified).Select(pair => pair.Key).ToArray();
    public static bool IsKnown(string operation) => States.ContainsKey(operation);
    public static AutoCadImplementationStatus GetStatus(string operation) => States.TryGetValue(operation, out var status) ? status : AutoCadImplementationStatus.Unsupported;
    public static IReadOnlyDictionary<string, AutoCadImplementationStatus> Snapshot() => States;
}

public sealed record AutoCadLayerSummary(string Name, string Handle, bool IsOff, bool IsFrozen, bool IsLocked, bool IsPlottable = true, string? Color = null, string? Linetype = null, string? Lineweight = null);
public sealed record AutoCadEntityDto(string ObjectId, string Handle, string DxfType, string Layer, string Color, string Linetype, string? Lineweight, object? Bounds, object? Geometry, string? Text = null, string? BlockName = null);
public sealed record AutoCadBlockSummary(string Name, string Handle, bool IsAnonymous, bool IsLayout);
public sealed record AutoCadUnitDto(string InsUnits, string Measurement, string LinearFormat, string AngularFormat, double ExternalToDrawingScale);
public sealed record AutoCadDocumentDto(string Name, string Path, bool IsActive, bool IsReadOnly, bool IsModified);

public sealed class AutoCadHealth
{
    public string Product { get; set; } = "AutoCAD";
    public bool ApiReady { get; set; } = true;
    public string ActiveDocument { get; set; } = string.Empty;
}

public interface IAutoCadVersionAdapter
{
    string Version { get; }
    double ToDrawingUnits(double value, string externalUnit, Autodesk.AutoCAD.DatabaseServices.Database database);
    Autodesk.AutoCAD.Geometry.Point3d ToDrawingPoint(double x, double y, double z, string externalUnit, Autodesk.AutoCAD.DatabaseServices.Database database);
}

public sealed class AutoCadVersionAdapter : IAutoCadVersionAdapter
{
    public AutoCadVersionAdapter(string version) => Version = version;
    public string Version { get; }
    public double ToDrawingUnits(double value, string externalUnit, Autodesk.AutoCAD.DatabaseServices.Database database) => AutodeskAIBridge.Core.BridgeUnits.ToMetres(value, externalUnit) / UnitScale(database.Insunits);
    public Autodesk.AutoCAD.Geometry.Point3d ToDrawingPoint(double x, double y, double z, string externalUnit, Autodesk.AutoCAD.DatabaseServices.Database database) { var scale = UnitScale(database.Insunits); var metres = AutodeskAIBridge.Core.BridgeUnits.ToMetres(1, externalUnit); return new Autodesk.AutoCAD.Geometry.Point3d(x * metres / scale, y * metres / scale, z * metres / scale); }
    private static double UnitScale(Autodesk.AutoCAD.DatabaseServices.UnitsValue units) => AutodeskAIBridge.Core.BridgeUnits.ToMetres(1, UnitName(units));
    private static string UnitName(Autodesk.AutoCAD.DatabaseServices.UnitsValue units) => units switch { Autodesk.AutoCAD.DatabaseServices.UnitsValue.Millimeters => "mm", Autodesk.AutoCAD.DatabaseServices.UnitsValue.Centimeters => "cm", Autodesk.AutoCAD.DatabaseServices.UnitsValue.Meters => "m", Autodesk.AutoCAD.DatabaseServices.UnitsValue.Inches => "in", Autodesk.AutoCAD.DatabaseServices.UnitsValue.Feet => "ft", _ => "m" };
}
