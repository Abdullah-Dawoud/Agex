using System;
using System.Collections.Generic;
using System.Linq;

namespace AutodeskAIBridge.Revit;

public enum RevitImplementationStatus
{
    Supported,
    SourceImplementedRuntimeUnverified,
    BlockedByApi,
    Unsupported
}

/// <summary>Single source of truth for Revit runtime capability reporting.</summary>
public static class RevitOperationCatalog
{
    private static readonly Dictionary<string, RevitImplementationStatus> States =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["revit.health"] = RevitImplementationStatus.Supported,
            ["revit.get_application_info"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.get_document_info"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.get_project_info"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.get_units"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.get_active_view"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.query_elements"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.find_elements"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.get_element"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.get_elements"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.get_element_parameters"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.describe_element"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.list_levels"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.get_level"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.create_level"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.rename_level"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.delete_level"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.create_wall"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.modify_wall"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.delete_wall"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.create_floor"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.list_families"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.list_family_types"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.get_family_type"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.place_family_instance"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.change_type"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.load_family"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.activate_family_type"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.move_element"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.move_elements"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.rotate_element"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.copy_element"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.copy_elements"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.delete_elements"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.set_parameter"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.set_parameters"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.bulk_set_parameters"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.list_rooms"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.get_room"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.create_room"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.set_room_parameters"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.list_views"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.get_view"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.create_floor_plan"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.duplicate_view"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.rename_view"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.create_sheet"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.list_sheets"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.get_sheet"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.place_view_on_sheet"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.create_text_note"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.save"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.save_as"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.execute_batch"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.create_view"] = RevitImplementationStatus.SourceImplementedRuntimeUnverified,
            ["revit.list_grids"] = RevitImplementationStatus.Unsupported,
            ["revit.get_selection"] = RevitImplementationStatus.Unsupported,
            ["revit.get_warnings"] = RevitImplementationStatus.Unsupported,
            ["revit.get_linked_models"] = RevitImplementationStatus.Unsupported,
            ["revit.get_worksets"] = RevitImplementationStatus.Unsupported,
            ["revit.get_phases"] = RevitImplementationStatus.Unsupported,
            ["revit.get_materials"] = RevitImplementationStatus.Unsupported
        };

    public static IReadOnlyCollection<string> Supported => States.Where(pair => pair.Value != RevitImplementationStatus.Unsupported).Select(pair => pair.Key).ToArray();
    public static IReadOnlyCollection<string> RuntimeOnly => States.Where(pair => pair.Value == RevitImplementationStatus.SourceImplementedRuntimeUnverified).Select(pair => pair.Key).ToArray();
    public static IReadOnlyCollection<string> Blocked => States.Where(pair => pair.Value == RevitImplementationStatus.BlockedByApi).Select(pair => pair.Key).ToArray();
    public static bool IsKnown(string operation) => States.TryGetValue(operation, out var status) && status != RevitImplementationStatus.Unsupported;
    public static RevitImplementationStatus GetStatus(string operation) => States.TryGetValue(operation, out var status) ? status : RevitImplementationStatus.Unsupported;
    public static IReadOnlyDictionary<string, RevitImplementationStatus> Snapshot() => States;
}

public sealed record RevitDocumentDto(string Title, string Path, string Version, bool IsReadOnly, bool IsModified);
public sealed record RevitUnitDto(string LengthUnit, string AngleUnit, string? ProjectLengthUnit = null, string? ProjectAreaUnit = null);
public sealed record RevitElementReferenceDto(long ElementId, string UniqueId, string Category, string Name);
public sealed record RevitOperationPlanDto(string Operation, IReadOnlyDictionary<string, string> Parameters, IReadOnlyList<string> Warnings);
