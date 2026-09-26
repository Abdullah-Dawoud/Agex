#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using AutodeskAIBridge.Core;

namespace AutodeskAIBridge.Revit;

/// <summary>Allow-listed Revit operation dispatcher. All model writes use RevitTransactionRunner.</summary>
public sealed class RevitCommandDispatcher
{
    private readonly RevitTransactionRunner _transactions = new();
    private readonly IRevitVersionAdapter _units;
    private bool _insideBatch;

    public RevitCommandDispatcher(IRevitVersionAdapter? units = null) => _units = units ?? new RevitVersionAdapter("runtime");

    public RevitResponse Dispatch(UIApplication application, RevitRequest request)
    {
        if (application is null) return RevitResponse.Fail(request, BridgeErrorCodes.NoActiveSession, "Revit application is not available.");
        if (!RevitOperationCatalog.IsKnown(request.Operation))
            return RevitResponse.Fail(request, RevitOperationCatalog.GetStatus(request.Operation) == RevitImplementationStatus.Unsupported ? BridgeErrorCodes.UnsupportedOperation : BridgeErrorCodes.BlockedByMissingApi, request.Operation);
        try
        {
            if (request.Operation.Equals("revit.health", StringComparison.OrdinalIgnoreCase)) return Health(application, request);
            if (request.Operation.Equals("revit.get_application_info", StringComparison.OrdinalIgnoreCase)) return GetApplicationInfo(application, request);
            var document = application.ActiveUIDocument?.Document;
            if (document is null) return RevitResponse.Fail(request, BridgeErrorCodes.NoOpenDocument, "No active Revit document exists.");
            if (document.IsReadOnly && !request.DryRun && IsWriteOperation(request.Operation)) return RevitResponse.Fail(request, BridgeErrorCodes.DocumentReadOnly, "Active Revit document is read-only.");
            return request.Operation.ToLowerInvariant() switch
            {
                "revit.get_document_info" => GetDocumentInfo(document, application, request),
                "revit.get_project_info" => GetProjectInfo(document, request),
                "revit.get_units" => GetUnits(document, request),
                "revit.get_active_view" => GetActiveView(application, request),
                "revit.query_elements" or "revit.find_elements" => QueryElements(document, request),
                "revit.get_element" or "revit.describe_element" => GetElement(document, request),
                "revit.get_elements" => GetElements(document, request),
                "revit.get_element_parameters" => GetElementParameters(document, request),
                "revit.list_levels" => ListLevels(document, request),
                "revit.get_level" => GetLevel(document, request),
                "revit.create_level" => CreateLevel(document, request),
                "revit.rename_level" => RenameLevel(document, request),
                "revit.delete_level" => DeleteLevel(document, request),
                "revit.create_wall" => CreateWall(document, request),
                "revit.modify_wall" => ModifyWall(document, request),
                "revit.delete_wall" => DeleteWall(document, request),
                "revit.create_floor" => CreateFloor(document, request),
                "revit.list_families" => ListFamilies(document, request),
                "revit.list_family_types" => ListFamilyTypes(document, request),
                "revit.get_family_type" => GetFamilyType(document, request),
                "revit.place_family_instance" => PlaceFamilyInstance(document, request),
                "revit.change_type" => ChangeType(document, request),
                "revit.load_family" => LoadFamily(document, request),
                "revit.activate_family_type" => ActivateFamilyType(document, request),
                "revit.move_element" => Transform(document, request, "move"),
                "revit.move_elements" => Transform(document, request, "move"),
                "revit.rotate_element" => Transform(document, request, "rotate"),
                "revit.copy_element" => Transform(document, request, "copy"),
                "revit.copy_elements" => Transform(document, request, "copy"),
                "revit.delete_elements" => DeleteElements(document, request),
                "revit.set_parameter" => SetParameters(document, request, false),
                "revit.set_parameters" => SetParameters(document, request, true),
                "revit.bulk_set_parameters" => BulkSetParameters(document, request),
                "revit.list_rooms" => ListRooms(document, request),
                "revit.get_room" => GetRoom(document, request),
                "revit.create_room" => CreateRoom(document, request),
                "revit.set_room_parameters" => SetRoomParameters(document, request),
                "revit.list_views" => ListViews(document, request),
                "revit.get_view" => GetView(document, request),
                "revit.create_floor_plan" => CreateFloorPlan(document, request),
                "revit.create_view" => CreateView(document, request),
                "revit.duplicate_view" => DuplicateView(document, request),
                "revit.rename_view" => RenameView(document, request),
                "revit.list_sheets" => ListSheets(document, request),
                "revit.get_sheet" => GetSheet(document, request),
                "revit.create_sheet" => CreateSheet(document, request),
                "revit.place_view_on_sheet" => PlaceViewOnSheet(document, request),
                "revit.create_text_note" => CreateTextNote(document, request),
                "revit.save" => Save(document, request),
                "revit.save_as" => SaveAs(document, request),
                "revit.execute_batch" => ExecuteBatch(application, document, request),
                _ => RevitResponse.Fail(request, BridgeErrorCodes.UnsupportedOperation, request.Operation)
            };
        }
        catch (Autodesk.Revit.Exceptions.ModificationForbiddenException exception)
        { return RevitResponse.Fail(request, BridgeErrorCodes.ElementNotModifiable, exception.Message); }
        catch (Autodesk.Revit.Exceptions.ArgumentException exception)
        { return RevitResponse.Fail(request, BridgeErrorCodes.InvalidRequest, exception.Message); }
        catch (Exception exception)
        { return RevitResponse.Fail(request, BridgeErrorCodes.TransactionFailed, exception.Message); }
    }

    private static RevitResponse Health(UIApplication application, RevitRequest request)
    {
        var document = application.ActiveUIDocument?.Document;
        return RevitResponse.Ok(request, new RevitHealth { Version = application.Application.VersionNumber, HasDocument = document is not null, DocumentTitle = document?.Title ?? string.Empty });
    }

    private static RevitResponse GetApplicationInfo(UIApplication application, RevitRequest request)
        => RevitResponse.Ok(request, new { product = "Revit", version = application.Application.VersionNumber, username = application.Application.Username, path = application.Application.VersionBuild });

    private static RevitResponse GetDocumentInfo(Document document, UIApplication application, RevitRequest request)
        => RevitResponse.Ok(request, new RevitDocumentDto(document.Title, document.PathName, application.Application.VersionNumber, document.IsReadOnly, document.IsModified));

    private static RevitResponse GetProjectInfo(Document document, RevitRequest request)
        => RevitResponse.Ok(request, new { elementId = document.ProjectInformation.Id.Value, name = document.ProjectInformation.Name, parameters = Parameters(document.ProjectInformation, document) });

    private RevitResponse GetUnits(Document document, RevitRequest request)
    {
        var units = document.GetUnits();
        var length = units.GetFormatOptions(SpecTypeId.Length).GetUnitTypeId().TypeId;
        var area = units.GetFormatOptions(SpecTypeId.Area).GetUnitTypeId().TypeId;
        return RevitResponse.Ok(request, new RevitUnitDto("mm", "deg", length, area));
    }

    private static RevitResponse GetActiveView(UIApplication application, RevitRequest request)
    {
        var view = application.ActiveUIDocument?.ActiveView;
        return view is null ? RevitResponse.Fail(request, BridgeErrorCodes.ViewNotFound, "No active Revit view exists.") : RevitResponse.Ok(request, ViewDto(view));
    }

    private RevitResponse QueryElements(Document document, RevitRequest request)
    {
        var collector = CreateCollector(document, request);
        var candidates = collector.WhereElementIsNotElementType().ToElements();
        var filtered = candidates.Where(element => Matches(element, document, request)).ToList();
        var offset = (int)Math.Max(0, Math.Min(int.MaxValue, Int(request, "offset") ?? 0));
        var limit = Clamp((int)Math.Min(1000, Math.Max(1, Int(request, "limit") ?? 100)), 1, 1000);
        var page = filtered.Skip(offset).Take(limit).Select(element => Describe(element, document)).ToArray();
        return RevitResponse.Ok(request, new RevitPagedResult<RevitElementDto>(page, offset, limit, filtered.Count, offset + page.Length < filtered.Count));
    }

    private static FilteredElementCollector CreateCollector(Document document, RevitRequest request)
    {
        var view = String(request, "view") ?? String(request, "viewId");
        FilteredElementCollector collector;
        if (int.TryParse(view, NumberStyles.Integer, CultureInfo.InvariantCulture, out var viewId)) collector = new FilteredElementCollector(document, new ElementId((long)viewId));
        else collector = new FilteredElementCollector(document);
        var category = String(request, "builtInCategory");
        if (string.IsNullOrWhiteSpace(category)) category = String(request, "category");
        BuiltInCategory? resolvedCategory = null;
        if (Enum.TryParse(category, true, out BuiltInCategory parsedCategory)) resolvedCategory = parsedCategory;
        else resolvedCategory = category?.ToLowerInvariant() switch { "walls" or "wall" => BuiltInCategory.OST_Walls, "floors" or "floor" => BuiltInCategory.OST_Floors, "doors" or "door" => BuiltInCategory.OST_Doors, "windows" or "window" => BuiltInCategory.OST_Windows, "rooms" or "room" => BuiltInCategory.OST_Rooms, "furniture" => BuiltInCategory.OST_Furniture, _ => null };
        if (resolvedCategory is { } categoryId) collector = collector.OfCategory(categoryId);
        var className = String(request, "class");
        var classType = className?.ToLowerInvariant() switch
        {
            "wall" or "walls" => typeof(Wall), "floor" or "floors" => typeof(Floor), "familyinstance" or "family_instance" => typeof(FamilyInstance),
            "level" or "levels" => typeof(Level), "view" or "views" => typeof(View), "viewsheet" or "sheet" => typeof(ViewSheet),
            "room" or "rooms" => typeof(Autodesk.Revit.DB.Architecture.Room), "elementtype" => typeof(ElementType), _ => null
        };
        return classType is null ? collector : collector.OfClass(classType);
    }

    private bool Matches(Element element, Document document, RevitRequest request)
    {
        var ids = IntValues(request, "elementIds");
        if (ids.Count > 0 && !ids.Contains(element.Id.Value)) return false;
        var uniqueIds = StringValues(request, "uniqueIds");
        if (uniqueIds.Count > 0 && !uniqueIds.Contains(element.UniqueId, StringComparer.OrdinalIgnoreCase)) return false;
        var name = String(request, "name");
        if (!string.IsNullOrWhiteSpace(name) && element.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0) return false;
        var familyName = String(request, "familyName");
        var type = document.GetElement(element.GetTypeId()) as ElementType;
        var family = type as FamilySymbol;
        if (!string.IsNullOrWhiteSpace(familyName) && !(family?.FamilyName ?? string.Empty).Equals(familyName, StringComparison.OrdinalIgnoreCase)) return false;
        var typeName = String(request, "typeName");
        if (!string.IsNullOrWhiteSpace(typeName) && (type?.Name ?? string.Empty).IndexOf(typeName, StringComparison.OrdinalIgnoreCase) < 0) return false;
        var levelName = String(request, "level");
        if (!string.IsNullOrWhiteSpace(levelName))
        {
            var level = LevelOf(element, document);
            if (level is null || (!level.Name.Equals(levelName, StringComparison.OrdinalIgnoreCase) && level.Id.Value.ToString(CultureInfo.InvariantCulture) != levelName)) return false;
        }
        var parameterFilters = ObjectArray(request, "parameterFilters");
        foreach (var filter in parameterFilters)
        {
            var parameter = filter.TryGetProperty("name", out var parameterName) ? element.LookupParameter(parameterName.GetString() ?? string.Empty) : null;
            var expected = filter.TryGetProperty("value", out var value) ? value.ToString() : string.Empty;
            var operation = filter.TryGetProperty("operator", out var op) ? op.GetString() ?? "equals" : "equals";
            if (parameter is null || !Compare(ParameterText(parameter), expected, operation)) return false;
        }
        if (TryCoordinateBox(request, "boundingBox", out var box, out _))
        {
            var bounds = element.get_BoundingBox(null);
            if (bounds is null || bounds.Max.X < box.Min.X || bounds.Min.X > box.Max.X || bounds.Max.Y < box.Min.Y || bounds.Min.Y > box.Max.Y || bounds.Max.Z < box.Min.Z || bounds.Min.Z > box.Max.Z) return false;
        }
        return true;
    }

    private RevitResponse GetElement(Document document, RevitRequest request)
    {
        var element = FindElement(document, request, "elementId", "uniqueId");
        return element is null ? RevitResponse.Fail(request, BridgeErrorCodes.ElementNotFound, "Element not found by elementId or uniqueId.") : RevitResponse.Ok(request, Describe(element, document));
    }

    private RevitResponse GetElements(Document document, RevitRequest request)
    {
        var ids = IntValues(request, "elementIds");
        if (ids.Count == 0) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidRequest, "elementIds must contain at least one element id.");
        var elements = ids.Select(id => document.GetElement(new ElementId((long)id))).Where(element => element is not null).Select(element => Describe(element!, document)).ToArray();
        return RevitResponse.Ok(request, elements);
    }

    private RevitResponse GetElementParameters(Document document, RevitRequest request)
    {
        var element = FindElement(document, request, "elementId", "uniqueId");
        return element is null ? RevitResponse.Fail(request, BridgeErrorCodes.ElementNotFound, "Element not found.") : RevitResponse.Ok(request, Parameters(element, document));
    }

    private RevitResponse ListLevels(Document document, RevitRequest request)
        => RevitResponse.Ok(request, new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>().Select(LevelDto).ToArray());

    private RevitResponse GetLevel(Document document, RevitRequest request)
    {
        var level = FindLevel(document, request);
        return level is null ? RevitResponse.Fail(request, BridgeErrorCodes.LevelNotFound, "Level not found by elementId, uniqueId, or name.") : RevitResponse.Ok(request, LevelDto(level));
    }

    private RevitResponse CreateLevel(Document document, RevitRequest request)
    {
        var name = Required(request, "name");
        if (name is null) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidRequest, "name is required.");
        if (!TryLength(request, "elevation", out var elevation)) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidCoordinate, "elevation must be a finite value with valid unit.");
        if (request.DryRun) return Plan(request, $"Create level '{name}' at {LengthText(request, elevation)}.");
        return FromTransaction(request, _transactions.Run(document, "AI Bridge - Create Level", context =>
        {
            var level = Level.Create(context.Document, elevation); level.Name = name; context.TrackCreated(level.Id); return LevelDto(level);
        }));
    }

    private RevitResponse RenameLevel(Document document, RevitRequest request)
    {
        var level = FindLevel(document, request); var name = Required(request, "name");
        if (level is null) return RevitResponse.Fail(request, BridgeErrorCodes.LevelNotFound, "Level not found.");
        if (name is null) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidRequest, "name is required.");
        if (request.DryRun) return Plan(request, $"Rename level {level.Id.Value} to '{name}'.", level.Id.Value);
        return FromTransaction(request, _transactions.Run(document, "AI Bridge - Rename Level", context => { level.Name = name; context.TrackModified(level.Id); return LevelDto(level); }));
    }

    private RevitResponse DeleteLevel(Document document, RevitRequest request)
    {
        var level = FindLevel(document, request); if (level is null) return RevitResponse.Fail(request, BridgeErrorCodes.LevelNotFound, "Level not found.");
        if (request.DryRun) return Plan(request, $"Delete level {level.Id.Value} and report dependent deletions.", level.Id.Value);
        return FromTransaction(request, _transactions.Run(document, "AI Bridge - Delete Level", context =>
        {
            var deleted = context.Document.Delete(level.Id); foreach (var id in deleted) context.TrackDeleted(id); return new { deletedElementIds = deleted.Select(id => id.Value).ToArray() };
        }));
    }

    private RevitResponse CreateWall(Document document, RevitRequest request)
    {
        if (!TryCoordinate(request, "start", out var start, out var startError)) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidCoordinate, startError ?? "start is required.");
        if (!TryCoordinate(request, "end", out var end, out var endError)) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidCoordinate, endError ?? "end is required.");
        if (start.DistanceTo(end) <= 1e-9) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidGeometry, "Wall start and end must differ.");
        var level = FindLevel(document, request); if (level is null) return RevitResponse.Fail(request, BridgeErrorCodes.LevelNotFound, "levelId or level is required.");
        var type = ResolveWallType(document, request, out var typeError); if (type is null) return RevitResponse.Fail(request, typeError ?? BridgeErrorCodes.TypeNotFound, "Wall type not found.");
        if (!TryLength(request, "height", out var height, true) || height <= 0) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidCoordinate, "height must be a positive finite value.");
        if (!TryLength(request, "offset", out var offset, true)) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidCoordinate, "offset must be a finite value with valid unit.");
        var structural = Bool(request, "structural") ?? false; var flip = Bool(request, "flip") ?? false;
        if (request.DryRun) return Plan(request, $"Create wall using type {type.Id.Value} on level {level.Id.Value}.");
        return FromTransaction(request, _transactions.Run(document, "AI Bridge - Create Wall", context =>
        {
            var wall = Wall.Create(context.Document, Line.CreateBound(start, end), type.Id, level.Id, height, offset, flip, structural); context.TrackCreated(wall.Id); return Describe(wall, document);
        }));
    }

    private RevitResponse ModifyWall(Document document, RevitRequest request)
    {
        var wall = FindElement(document, request, "elementId", "uniqueId") as Wall; if (wall is null) return RevitResponse.Fail(request, BridgeErrorCodes.ElementNotFound, "Wall not found.");
        if (wall.Pinned) return RevitResponse.Fail(request, BridgeErrorCodes.ElementPinned, "Wall is pinned.");
        if (request.DryRun) return Plan(request, $"Modify wall {wall.Id.Value}.", wall.Id.Value);
        return FromTransaction(request, _transactions.Run(document, "AI Bridge - Modify Wall", context =>
        {
            if (TryCoordinate(request, "start", out var start, out _) && TryCoordinate(request, "end", out var end, out _))
            { if (wall.Location is not LocationCurve location) throw new InvalidOperationException("Wall has no curve location."); location.Curve = Line.CreateBound(start, end); }
            if (FindLevel(document, request) is { } level) SetParameter(wall, "WALL_BASE_CONSTRAINT", level.Id, context);
            var topRequest = new RevitRequest(); if (request.Parameters.TryGetValue("topLevelId", out var topLevelId)) topRequest.Parameters["levelId"] = topLevelId; else if (request.Parameters.TryGetValue("topConstraint", out var topConstraint)) topRequest.Parameters["level"] = topConstraint; if (FindLevel(document, topRequest) is { } topLevel) SetParameter(wall, "WALL_HEIGHT_TYPE", topLevel.Id, context);
            if (TryLength(request, "height", out var height, true)) SetParameter(wall, "WALL_USER_HEIGHT_PARAM", height, context);
            if (TryLength(request, "offset", out var offset, true)) SetParameter(wall, "WALL_BASE_OFFSET", offset, context);
            if (ResolveWallType(document, request, out _) is { } type) wall.ChangeTypeId(type.Id);
            foreach (var pair in Object(request, "parameters")) SetParameterValue(wall, pair.Key, pair.Value, context);
            context.TrackModified(wall.Id); return Describe(wall, document);
        }));
    }

    private RevitResponse DeleteWall(Document document, RevitRequest request)
    {
        var wall = FindElement(document, request, "elementId", "uniqueId") as Wall; if (wall is null) return RevitResponse.Fail(request, BridgeErrorCodes.ElementNotFound, "Wall not found.");
        if (wall.Pinned) return RevitResponse.Fail(request, BridgeErrorCodes.ElementPinned, "Wall is pinned.");
        if (request.DryRun) return Plan(request, $"Delete wall {wall.Id.Value}.", wall.Id.Value);
        return DeleteByIds(document, request, new[] { wall.Id });
    }

    private RevitResponse CreateFloor(Document document, RevitRequest request)
    {
        if (!TryLoops(request, out var loops, out var error)) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidBoundary, error ?? "boundaryLoops must contain closed loops with at least three vertices.");
        var level = FindLevel(document, request); if (level is null) return RevitResponse.Fail(request, BridgeErrorCodes.LevelNotFound, "levelId or level is required.");
        var type = ResolveElementType<FloorType>(document, request, out var typeError); if (type is null) return RevitResponse.Fail(request, typeError ?? BridgeErrorCodes.TypeNotFound, "Floor type not found.");
        var structural = Bool(request, "structural") ?? false;
        if (request.DryRun) return Plan(request, $"Create floor with {loops.Count} boundary loop(s) on level {level.Id.Value}.");
        return FromTransaction(request, _transactions.Run(document, "AI Bridge - Create Floor", context =>
        { var floor = Floor.Create(context.Document, loops, type.Id, level.Id, structural, null, 0); context.TrackCreated(floor.Id); return Describe(floor, document); }));
    }

    private static RevitResponse ListFamilies(Document document, RevitRequest request)
        => RevitResponse.Ok(request, new FilteredElementCollector(document).OfClass(typeof(Family)).Cast<Family>().Select(f => new RevitFamilySummaryDto(f.Id.Value, f.Name, f.IsInPlace, f.IsEditable)).ToArray());

    private static RevitResponse ListFamilyTypes(Document document, RevitRequest request)
    {
        var familyName = String(request, "familyName");
        var types = new FilteredElementCollector(document).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().Where(s => string.IsNullOrWhiteSpace(familyName) || s.FamilyName.Equals(familyName, StringComparison.OrdinalIgnoreCase)).Select(s => new RevitFamilyTypeSummaryDto(s.Id.Value, s.Name, s.FamilyName, s.IsActive)).ToArray();
        return RevitResponse.Ok(request, types);
    }

    private RevitResponse GetFamilyType(Document document, RevitRequest request)
    {
        var symbol = ResolveElementType<FamilySymbol>(document, request, out var error); return symbol is null ? RevitResponse.Fail(request, error ?? BridgeErrorCodes.TypeNotFound, "Family type not found.") : RevitResponse.Ok(request, new RevitFamilyTypeSummaryDto(symbol.Id.Value, symbol.Name, symbol.FamilyName, symbol.IsActive));
    }

    private RevitResponse PlaceFamilyInstance(Document document, RevitRequest request)
    {
        if (!TryCoordinate(request, "position", out var point, out var error)) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidCoordinate, error ?? "position is required.");
        var symbol = ResolveElementType<FamilySymbol>(document, request, out var typeError); if (symbol is null) return RevitResponse.Fail(request, typeError ?? BridgeErrorCodes.TypeNotFound, "Family type not found.");
        Element? host = null; var hostText = String(request, "hostId") ?? String(request, "host");
        if (!string.IsNullOrWhiteSpace(hostText)) host = FindElementByValue(document, hostText);
        var hasHostRequirement = !string.IsNullOrWhiteSpace(String(request, "hostId")) || Bool(request, "hosted") == true;
        if (hasHostRequirement && host is null) return RevitResponse.Fail(request, BridgeErrorCodes.HostRequired, "Explicit hostId is required and must resolve to a host element.");
        var level = FindLevel(document, request); var structural = Autodesk.Revit.DB.Structure.StructuralType.NonStructural;
        if (Enum.TryParse(String(request, "structuralType"), true, out Autodesk.Revit.DB.Structure.StructuralType parsed)) structural = parsed;
        if (request.DryRun) return Plan(request, $"Place family type {symbol.Id.Value} at external coordinates.");
        return FromTransaction(request, _transactions.Run(document, "AI Bridge - Place Family Instance", context =>
        {
            if (!symbol.IsActive) symbol.Activate();
            FamilyInstance instance = host is not null ? document.Create.NewFamilyInstance(point, symbol, host, structural) : level is not null ? document.Create.NewFamilyInstance(point, symbol, level, structural) : document.Create.NewFamilyInstance(point, symbol, structural);
            context.TrackCreated(instance.Id); return Describe(instance, document);
        }));
    }

    private RevitResponse ChangeType(Document document, RevitRequest request)
    {
        var element = FindElement(document, request, "elementId", "uniqueId"); if (element is null) return RevitResponse.Fail(request, BridgeErrorCodes.ElementNotFound, "Element not found.");
        var type = ResolveElementType<ElementType>(document, request, out var error); if (type is null) return RevitResponse.Fail(request, error ?? BridgeErrorCodes.TypeNotFound, "Type not found.");
        if (element.Pinned) return RevitResponse.Fail(request, BridgeErrorCodes.ElementPinned, "Element is pinned.");
        if (request.DryRun) return Plan(request, $"Change element {element.Id.Value} to type {type.Id.Value}.", element.Id.Value);
        return FromTransaction(request, _transactions.Run(document, "AI Bridge - Change Type", context => { element.ChangeTypeId(type.Id); context.TrackModified(element.Id); return Describe(element, document); }));
    }

    private RevitResponse LoadFamily(Document document, RevitRequest request)
    {
        var path = Required(request, "path"); if (path is null) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidPath, "path is required.");
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path) || !path.EndsWith(".rfa", StringComparison.OrdinalIgnoreCase)) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidPath, "path must be an existing absolute .rfa path.");
        if (request.DryRun) return Plan(request, $"Load family from '{path}'.");
        return FromTransaction(request, _transactions.Run(document, "AI Bridge - Load Family", context =>
        { if (!context.Document.LoadFamily(path, out var family)) throw new InvalidOperationException("Family load returned false."); context.TrackCreated(family.Id); return new { familyId = family.Id.Value, name = family.Name }; }));
    }

    private RevitResponse ActivateFamilyType(Document document, RevitRequest request)
    {
        var symbol = ResolveElementType<FamilySymbol>(document, request, out var error); if (symbol is null) return RevitResponse.Fail(request, error ?? BridgeErrorCodes.TypeNotFound, "Family type not found.");
        if (symbol.IsActive) return RevitResponse.Ok(request, new { elementId = symbol.Id.Value, active = true });
        if (request.DryRun) return Plan(request, $"Activate family type {symbol.Id.Value}.", symbol.Id.Value);
        return FromTransaction(request, _transactions.Run(document, "AI Bridge - Activate Family Type", context => { symbol.Activate(); context.TrackModified(symbol.Id); return new { elementId = symbol.Id.Value, active = symbol.IsActive }; }));
    }

    private RevitResponse Transform(Document document, RevitRequest request, string operation)
    {
        var ids = IntValues(request, "elementIds"); if (ids.Count == 0 && Int(request, "elementId") is { } one) ids = new[] { one };
        if (ids.Count == 0) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidRequest, "elementId or elementIds is required.");
        var elements = ids.Select(id => document.GetElement(new ElementId((long)id))).ToArray(); if (elements.Any(e => e is null)) return RevitResponse.Fail(request, BridgeErrorCodes.ElementNotFound, "One or more elements were not found.");
        if (elements.Any(e => e!.Pinned)) return RevitResponse.Fail(request, BridgeErrorCodes.ElementPinned, "One or more elements are pinned.");
        if (operation == "copy") { var copyTranslation = XYZ.Zero; if (Raw(request, "translation").ValueKind != JsonValueKind.Undefined && !TryCoordinate(request, "translation", out copyTranslation, out var copyError)) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidCoordinate, copyError ?? "translation is invalid."); if (request.DryRun) return Plan(request, $"Copy {ids.Count} element(s)."); return FromTransaction(request, _transactions.Run(document, "AI Bridge - Copy Elements", context => { var created = new List<long>(); foreach (var id in ids) foreach (var copy in ElementTransformUtils.CopyElement(document, new ElementId((long)id), copyTranslation)) { created.Add(copy.Value); context.TrackCreated(copy); } return new { copiedElementIds = created }; })); }
        if (operation == "move")
        {
            if (!TryCoordinate(request, "translation", out var translation, out var moveError)) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidCoordinate, moveError ?? "translation is required.");
            if (request.DryRun) return Plan(request, $"Move {ids.Count} element(s).");
            return FromTransaction(request, _transactions.Run(document, "AI Bridge - Move Elements", context => { foreach (var id in ids) { ElementTransformUtils.MoveElement(document, new ElementId((long)id), translation); context.TrackModified(new ElementId((long)id)); } return new { modifiedElementIds = ids }; }));
        }
        if (operation == "rotate")
        {
            if (!TryCoordinate(request, "axisStart", out var axisStart, out var axisError)) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidCoordinate, axisError ?? "axisStart is required.");
            if (!TryCoordinate(request, "axisEnd", out var axisEnd, out var axisEndError)) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidCoordinate, axisEndError ?? "axisEnd is required.");
            var angle = Angle(request, "angle"); if (angle is null) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidUnit, "angle with unit deg or rad is required.");
            if (request.DryRun) return Plan(request, $"Rotate {ids.Count} element(s).");
            return FromTransaction(request, _transactions.Run(document, "AI Bridge - Rotate Elements", context => { var axis = Line.CreateBound(axisStart, axisEnd); foreach (var id in ids) { ElementTransformUtils.RotateElement(document, new ElementId((long)id), axis, angle.Value); context.TrackModified(new ElementId((long)id)); } return new { modifiedElementIds = ids }; }));
        }
        return RevitResponse.Fail(request, BridgeErrorCodes.UnsupportedOperation, operation);
    }

    private RevitResponse DeleteElements(Document document, RevitRequest request)
    {
        var ids = IntValues(request, "elementIds"); if (ids.Count == 0 && Int(request, "elementId") is { } one) ids = new[] { one }; if (ids.Count == 0) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidRequest, "elementIds is required.");
        var missing = ids.Where(id => document.GetElement(new ElementId((long)id)) is null).ToArray(); if (missing.Length > 0) return RevitResponse.Fail(request, BridgeErrorCodes.ElementNotFound, "One or more elements were not found.", new { missing });
        if (request.DryRun) return Plan(request, $"Delete {ids.Count} element(s).", ids.ToArray());
        return DeleteByIds(document, request, ids.Select(id => new ElementId((long)id)).ToArray());
    }

    private RevitResponse DeleteByIds(Document document, RevitRequest request, IReadOnlyCollection<ElementId> ids)
        => FromTransaction(request, _transactions.Run(document, "AI Bridge - Delete Elements", context => { var deleted = document.Delete(ids.ToList()); foreach (var id in deleted) context.TrackDeleted(id); return new { deletedElementIds = deleted.Select(id => id.Value).ToArray() }; }));

    private RevitResponse SetParameters(Document document, RevitRequest request, bool many)
    {
        var element = FindElement(document, request, "elementId", "uniqueId"); if (element is null) return RevitResponse.Fail(request, BridgeErrorCodes.ElementNotFound, "Element not found.");
        var values = many ? Object(request, "parameters") : new Dictionary<string, JsonElement> { [String(request, "parameter") ?? string.Empty] = Raw(request, "value") };
        if (values.Count == 0 || values.Keys.Any(string.IsNullOrWhiteSpace)) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidRequest, "parameter and value are required.");
        if (request.DryRun) return Plan(request, $"Set {values.Count} parameter(s) on element {element.Id.Value}.", element.Id.Value);
        return FromTransaction(request, _transactions.Run(document, "AI Bridge - Set Parameters", context => { var result = new List<object>(); foreach (var pair in values) result.Add(SetParameterValue(element, pair.Key, pair.Value, context)); return result; }));
    }

    private RevitResponse BulkSetParameters(Document document, RevitRequest request)
    {
        var items = ObjectArray(request, "items"); if (items.Count == 0) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidRequest, "items is required.");
        var targets = new List<(Element element, string name, JsonElement value)>();
        foreach (var item in items)
        {
            if (!item.TryGetProperty("elementId", out var id) || !id.TryGetInt32(out var elementId) || document.GetElement(new ElementId((long)elementId)) is not { } element || !item.TryGetProperty("parameter", out var name) || string.IsNullOrWhiteSpace(name.GetString())) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidRequest, "Each item requires elementId and parameter.");
            targets.Add((element, name.GetString()!, item.TryGetProperty("value", out var value) ? value : default));
        }
        if (request.DryRun) return Plan(request, $"Set parameters on {targets.Select(target => target.element.Id.Value).Distinct().Count()} element(s).");
        return FromTransaction(request, _transactions.Run(document, "AI Bridge - Bulk Set Parameters", context => targets.Select(target => SetParameterValue(target.element, target.name, target.value, context)).ToArray()));
    }

    private RevitResponse ListRooms(Document document, RevitRequest request)
        => RevitResponse.Ok(request, new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().Cast<Autodesk.Revit.DB.Architecture.Room>().Select(room => RoomDto(room, document)).ToArray());

    private RevitResponse GetRoom(Document document, RevitRequest request)
    {
        var room = FindElement(document, request, "elementId", "uniqueId") as Autodesk.Revit.DB.Architecture.Room; return room is null ? RevitResponse.Fail(request, BridgeErrorCodes.ElementNotFound, "Room not found.") : RevitResponse.Ok(request, RoomDto(room, document));
    }

    private RevitResponse CreateRoom(Document document, RevitRequest request)
    {
        var level = FindLevel(document, request); if (level is null) return RevitResponse.Fail(request, BridgeErrorCodes.LevelNotFound, "levelId or level is required.");
        if (!TryCoordinate(request, "position", out var point, out var error)) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidCoordinate, error ?? "position is required.");
        if (request.DryRun) return Plan(request, $"Create room on level {level.Id.Value}.");
        return FromTransaction(request, _transactions.Run(document, "AI Bridge - Create Room", context => { var room = document.Create.NewRoom(level, new UV(point.X, point.Y)); if (room.Area <= 0) throw new RevitOperationException(BridgeErrorCodes.RoomNotEnclosed, "Room was placed but has no enclosed boundary. Add room-bounding elements and retry."); context.TrackCreated(room.Id); return RoomDto(room, document); }));
    }

    private RevitResponse SetRoomParameters(Document document, RevitRequest request) => SetParameters(document, request, true);

    private static RevitResponse ListViews(Document document, RevitRequest request)
        => RevitResponse.Ok(request, new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>().Where(view => !view.IsTemplate).Select(ViewDto).ToArray());

    private RevitResponse GetView(Document document, RevitRequest request)
    {
        var view = FindElement(document, request, "elementId", "uniqueId") as View; return view is null ? RevitResponse.Fail(request, BridgeErrorCodes.ViewNotFound, "View not found.") : RevitResponse.Ok(request, ViewDto(view));
    }

    private RevitResponse CreateFloorPlan(Document document, RevitRequest request)
    {
        var level = FindLevel(document, request); if (level is null) return RevitResponse.Fail(request, BridgeErrorCodes.LevelNotFound, "levelId or level is required.");
        var type = new FilteredElementCollector(document).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().FirstOrDefault(candidate => candidate.ViewFamily == ViewFamily.FloorPlan); if (type is null) return RevitResponse.Fail(request, BridgeErrorCodes.TypeNotFound, "No floor plan view family type exists.");
        if (request.DryRun) return Plan(request, $"Create floor plan for level {level.Id.Value}.");
        return FromTransaction(request, _transactions.Run(document, "AI Bridge - Create Floor Plan", context => { var view = ViewPlan.Create(document, type.Id, level.Id); context.TrackCreated(view.Id); return ViewDto(view); }));
    }

    private RevitResponse CreateView(Document document, RevitRequest request)
        => (String(request, "viewType") ?? "floor_plan").Equals("floor_plan", StringComparison.OrdinalIgnoreCase) ? CreateFloorPlan(document, request) : RevitResponse.Fail(request, BridgeErrorCodes.UnsupportedOperation, "create_view supports only viewType=floor_plan.");

    private RevitResponse DuplicateView(Document document, RevitRequest request)
    {
        var view = FindElement(document, request, "elementId", "uniqueId") as View; if (view is null) return RevitResponse.Fail(request, BridgeErrorCodes.ViewNotFound, "View not found.");
        var option = Enum.TryParse(String(request, "duplicateOption"), true, out ViewDuplicateOption parsed) ? parsed : ViewDuplicateOption.Duplicate;
        if (!view.CanViewBeDuplicated(option)) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidRequest, "View cannot be duplicated with requested option.");
        if (request.DryRun) return Plan(request, $"Duplicate view {view.Id.Value}.", view.Id.Value);
        return FromTransaction(request, _transactions.Run(document, "AI Bridge - Duplicate View", context => { var id = view.Duplicate(option); context.TrackCreated(id); return ViewDto((View)document.GetElement(id)!); }));
    }

    private RevitResponse RenameView(Document document, RevitRequest request)
    {
        var view = FindElement(document, request, "elementId", "uniqueId") as View; var name = Required(request, "name"); if (view is null) return RevitResponse.Fail(request, BridgeErrorCodes.ViewNotFound, "View not found."); if (name is null) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidRequest, "name is required."); if (request.DryRun) return Plan(request, $"Rename view {view.Id.Value}.", view.Id.Value); return FromTransaction(request, _transactions.Run(document, "AI Bridge - Rename View", context => { view.Name = name; context.TrackModified(view.Id); return ViewDto(view); }));
    }

    private static RevitResponse ListSheets(Document document, RevitRequest request)
        => RevitResponse.Ok(request, new FilteredElementCollector(document).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Select(SheetDto).ToArray());

    private RevitResponse GetSheet(Document document, RevitRequest request)
    {
        var sheet = FindSheet(document, request); return sheet is null ? RevitResponse.Fail(request, BridgeErrorCodes.SheetNotFound, "Sheet not found.") : RevitResponse.Ok(request, SheetDto(sheet));
    }

    private RevitResponse CreateSheet(Document document, RevitRequest request)
    {
        var number = Required(request, "sheetNumber") ?? Required(request, "number"); var name = Required(request, "sheetName") ?? Required(request, "name"); if (number is null || name is null) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidRequest, "sheetNumber and sheetName are required.");
        if (string.IsNullOrWhiteSpace(String(request, "titleBlockId")) && string.IsNullOrWhiteSpace(String(request, "titleBlockName"))) return RevitResponse.Fail(request, BridgeErrorCodes.TypeNotFound, "titleBlockId or titleBlockName is required; no title block is guessed.", new { available = new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType().Cast<FamilySymbol>().Select(symbol => new { elementId = symbol.Id.Value, name = symbol.Name, family = symbol.FamilyName }).ToArray() });
        var titleBlock = ResolveElementType<FamilySymbol>(document, request, out var titleError, BuiltInCategory.OST_TitleBlocks); if (titleBlock is null) return RevitResponse.Fail(request, string.IsNullOrWhiteSpace(String(request, "titleBlockId")) && string.IsNullOrWhiteSpace(String(request, "titleBlockName")) ? BridgeErrorCodes.TypeNotFound : titleError ?? BridgeErrorCodes.TypeNotFound, "Supply titleBlockId or titleBlockName. Available title block types are returned in details.", new { available = new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType().Cast<FamilySymbol>().Select(symbol => new { elementId = symbol.Id.Value, name = symbol.Name, family = symbol.FamilyName }).ToArray() });
        if (request.DryRun) return Plan(request, $"Create sheet {number} - {name}.");
        return FromTransaction(request, _transactions.Run(document, "AI Bridge - Create Sheet", context => { var sheet = ViewSheet.Create(document, titleBlock.Id); sheet.SheetNumber = number; sheet.Name = name; context.TrackCreated(sheet.Id); return SheetDto(sheet); }));
    }

    private RevitResponse PlaceViewOnSheet(Document document, RevitRequest request)
    {
        var sheet = FindSheet(document, request); if (sheet is null) return RevitResponse.Fail(request, BridgeErrorCodes.SheetNotFound, "Sheet not found."); var view = FindElement(document, request, "viewId", "view") as View; if (view is null) return RevitResponse.Fail(request, BridgeErrorCodes.ViewNotFound, "View not found."); if (!Viewport.CanAddViewToSheet(document, sheet.Id, view.Id)) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidRequest, "View cannot be placed on this sheet."); if (!TryCoordinate(request, "position", out var position, out var error)) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidCoordinate, error ?? "position is required."); if (request.DryRun) return Plan(request, $"Place view {view.Id.Value} on sheet {sheet.Id.Value}."); return FromTransaction(request, _transactions.Run(document, "AI Bridge - Place View On Sheet", context => { var viewport = Viewport.Create(document, sheet.Id, view.Id, position); context.TrackCreated(viewport.Id); return new { viewportId = viewport.Id.Value, sheetId = sheet.Id.Value, viewId = view.Id.Value }; }));
    }

    private RevitResponse CreateTextNote(Document document, RevitRequest request)
    {
        var view = FindElement(document, request, "viewId", "view") as View; var text = Required(request, "text"); if (view is null) return RevitResponse.Fail(request, BridgeErrorCodes.ViewNotFound, "viewId is required and must resolve to a view."); if (text is null) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidRequest, "text is required."); if (!TryCoordinate(request, "position", out var position, out var error)) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidCoordinate, error ?? "position is required."); var textType = ResolveElementType<TextNoteType>(document, request, out var typeError); if (textType is null && string.IsNullOrWhiteSpace(String(request, "typeId")) && string.IsNullOrWhiteSpace(String(request, "typeName"))) textType = new FilteredElementCollector(document).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().FirstOrDefault(); if (textType is null) return RevitResponse.Fail(request, typeError ?? BridgeErrorCodes.TypeNotFound, "No compatible text note type exists."); if (request.DryRun) return Plan(request, $"Create text note in view {view.Id.Value}."); return FromTransaction(request, _transactions.Run(document, "AI Bridge - Create Text Note", context => { var note = TextNote.Create(document, view.Id, position, text, textType.Id); context.TrackCreated(note.Id); return Describe(note, document); }));
    }

    private static RevitResponse Save(Document document, RevitRequest request)
    {
        if (request.DryRun) return Plan(request, "Save active Revit document."); if (document.IsReadOnly) return RevitResponse.Fail(request, BridgeErrorCodes.DocumentReadOnly, "Active document is read-only."); document.Save(); return RevitResponse.Ok(request, new { path = document.PathName, saved = true });
    }

    private static RevitResponse SaveAs(Document document, RevitRequest request)
    {
        var path = Required(request, "path"); if (path is null || !Path.IsPathFullyQualified(path) || !path.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".rfa", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".rte", StringComparison.OrdinalIgnoreCase)) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidPath, "path must be an absolute .rvt, .rfa, or .rte path."); path = Path.GetFullPath(path); var overwrite = Bool(request, "overwrite") ?? false; if (File.Exists(path) && !overwrite) return RevitResponse.Fail(request, BridgeErrorCodes.FileAlreadyExists, "Destination exists; set overwrite=true explicitly."); if (request.DryRun) return Plan(request, $"Save active document as '{path}'."); document.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = overwrite }); return RevitResponse.Ok(request, new { path, saved = true });
    }

    private RevitResponse ExecuteBatch(UIApplication application, Document document, RevitRequest request)
    {
        if (_insideBatch) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidRequest, "Recursive execute_batch is not allowed."); var operations = ObjectArray(request, "operations"); if (operations.Count == 0) return RevitResponse.Fail(request, BridgeErrorCodes.InvalidRequest, "operations is required."); var atomic = Bool(request, "atomic") ?? true; var results = new List<object>(); if (request.DryRun) { foreach (var operation in operations) { var child = ChildRequest(operation, true); results.Add(Dispatch(application, child)); } return RevitResponse.Ok(request, new { atomic, dryRun = true, operations = results }); }
        _insideBatch = true; using var group = atomic ? new TransactionGroup(document, "AI Bridge - Execute Batch") : null; try { if (group is not null && group.Start() != TransactionStatus.Started) return RevitResponse.Fail(request, BridgeErrorCodes.TransactionFailed, "Batch transaction group could not start."); foreach (var operation in operations) { var child = ChildRequest(operation, false); var result = Dispatch(application, child); results.Add(result); if (!result.Success && atomic) { group!.RollBack(); return RevitResponse.Fail(request, result.ErrorCode, $"Batch rolled back after '{child.Operation}': {result.ErrorMessage}"); } } if (group is not null && group.Assimilate() != TransactionStatus.Committed) return RevitResponse.Fail(request, BridgeErrorCodes.TransactionFailed, "Batch transaction group could not commit."); return RevitResponse.Ok(request, new { atomic, operations = results }); } finally { _insideBatch = false; }
    }

    private static RevitRequest ChildRequest(JsonElement operation, bool dryRun)
    {
        var name = operation.TryGetProperty("tool", out var tool) ? tool.GetString() : null; if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Each batch operation requires tool."); var child = new RevitRequest { Operation = name!, DryRun = dryRun }; if (operation.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.Object) foreach (var property in args.EnumerateObject()) child.Parameters[property.Name] = property.Value.GetRawText(); return child;
    }

    private object SetParameterValue(Element element, string name, JsonElement value, RevitTransactionContext context)
    {
        var parameter = FindParameter(element, name); if (parameter is null) throw new ParameterOperationException(BridgeErrorCodes.ParameterNotFound, $"Parameter '{name}' was not found."); if (parameter.IsReadOnly) throw new ParameterOperationException(BridgeErrorCodes.ParameterReadOnly, $"Parameter '{name}' is read-only."); if (element is not ElementType && element.GetTypeId() is { } typeId && typeId != ElementId.InvalidElementId && element.Document.GetElement(typeId)?.LookupParameter(name) is not null && element.LookupParameter(name) is null) throw new ParameterOperationException(BridgeErrorCodes.TypeParameterRequiresTypeTarget, $"Parameter '{name}' is a type parameter; target type element explicitly."); var previous = ParameterValue(parameter); try { SetParameter(parameter, value, element.Document); context.TrackModified(element.Id); return new { elementId = element.Id.Value, parameter = name, previous, value = ParameterValue(parameter) }; } catch (ParameterOperationException) { throw; } catch (Exception exception) { throw new ParameterOperationException(BridgeErrorCodes.InvalidParameterValue, $"Parameter '{name}' rejected value: {exception.Message}"); }
    }

    private static Parameter? FindParameter(Element element, string name)
    {
        if (Enum.TryParse(name, true, out BuiltInParameter builtIn)) return element.get_Parameter(builtIn); return element.LookupParameter(name) ?? (element is ElementType ? null : element.Document.GetElement(element.GetTypeId())?.LookupParameter(name));
    }

    private static void SetParameter(Element element, string name, ElementId value, RevitTransactionContext context)
    {
        var parameter = FindParameter(element, name); if (parameter is null) throw new ParameterOperationException(BridgeErrorCodes.ParameterNotFound, $"Parameter '{name}' was not found."); if (parameter.IsReadOnly) throw new ParameterOperationException(BridgeErrorCodes.ParameterReadOnly, $"Parameter '{name}' is read-only."); parameter.Set(value); context.TrackModified(element.Id);
    }

    private static void SetParameter(Element element, string name, double value, RevitTransactionContext context)
    {
        var parameter = FindParameter(element, name); if (parameter is null) throw new ParameterOperationException(BridgeErrorCodes.ParameterNotFound, $"Parameter '{name}' was not found."); if (parameter.IsReadOnly) throw new ParameterOperationException(BridgeErrorCodes.ParameterReadOnly, $"Parameter '{name}' is read-only."); parameter.Set(value); context.TrackModified(element.Id);
    }

    private void SetParameter(Parameter parameter, JsonElement value, Document document)
    {
        switch (parameter.StorageType)
        {
            case StorageType.String: if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) parameter.Set(string.Empty); else if (value.ValueKind == JsonValueKind.String) parameter.Set(value.GetString() ?? string.Empty); else parameter.Set(value.ToString()); break;
            case StorageType.Integer: if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False) parameter.Set(value.GetBoolean() ? 1 : 0); else if (value.TryGetInt32(out var integer)) parameter.Set(integer); else if (int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out integer)) parameter.Set(integer); else throw new FormatException("Integer expected."); break;
            case StorageType.Double:
                double number;
                if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("value", out var externalValue) && externalValue.TryGetDouble(out var externalNumber))
                {
                    var unit = value.TryGetProperty("unit", out var unitValue) ? unitValue.GetString() ?? "mm" : "mm";
                    number = unit.StartsWith("deg", StringComparison.OrdinalIgnoreCase) || unit.StartsWith("rad", StringComparison.OrdinalIgnoreCase) ? _units.ToInternalAngle(externalNumber, unit) : _units.ToInternalLength(externalNumber, unit);
                }
                else if (!value.TryGetDouble(out number)) throw new FormatException("Finite number or {value,unit} expected.");
                if (double.IsNaN(number) || double.IsInfinity(number)) throw new FormatException("Finite number expected."); parameter.Set(number); break;
            case StorageType.ElementId: if (!int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) throw new FormatException("ElementId integer expected."); if (document.GetElement(new ElementId((long)id)) is null) throw new FormatException("ElementId does not exist."); parameter.Set(new ElementId((long)id)); break;
            default: throw new FormatException("Unsupported parameter storage type.");
        }
    }

    private static RevitResponse FromTransaction<T>(RevitRequest request, RevitTransactionResult<T> result)
    {
        var response = result.Success ? RevitResponse.Ok(request, result.Value) : RevitResponse.Fail(request, result.ErrorCode, result.ErrorMessage); response.Warnings.AddRange(result.Warnings); response.ChangedElementIds.AddRange(result.ChangedElementIds.Select(id => id.Value)); response.CreatedElementIds.AddRange(result.CreatedElementIds.Select(id => id.Value)); response.ModifiedElementIds.AddRange(result.ModifiedElementIds.Select(id => id.Value)); response.DeletedElementIds.AddRange(result.DeletedElementIds.Select(id => id.Value)); return response;
    }

    private static RevitResponse Plan(RevitRequest request, string effect, params long[] ids) => RevitResponse.Ok(request, new RevitChangePlanDto(request.Operation, ids, new[] { effect }, true));
    private static bool IsWriteOperation(string operation) => operation.StartsWith("revit.create_", StringComparison.OrdinalIgnoreCase) || operation.StartsWith("revit.modify_", StringComparison.OrdinalIgnoreCase) || operation.StartsWith("revit.delete_", StringComparison.OrdinalIgnoreCase) || operation.StartsWith("revit.set_", StringComparison.OrdinalIgnoreCase) || operation.StartsWith("revit.move", StringComparison.OrdinalIgnoreCase) || operation.StartsWith("revit.copy", StringComparison.OrdinalIgnoreCase) || operation.StartsWith("revit.rotate", StringComparison.OrdinalIgnoreCase) || operation is "revit.rename_level" or "revit.change_type" or "revit.load_family" or "revit.activate_family_type" or "revit.save" or "revit.save_as" or "revit.execute_batch";
    private static RevitLevelSummary LevelDto(Level level) => new() { ElementId = level.Id.Value, UniqueId = level.UniqueId, Name = level.Name, ElevationMm = UnitUtils.ConvertFromInternalUnits(level.Elevation, UnitTypeId.Millimeters) };
    private static object ViewDto(View view) => new RevitViewSummaryDto(view.Id.Value, view.UniqueId, view.Name, view.ViewType.ToString(), view.IsTemplate, view.GenLevel?.Id.Value);
    private static object SheetDto(ViewSheet sheet) => new RevitSheetSummaryDto(sheet.Id.Value, sheet.UniqueId, sheet.Name, sheet.SheetNumber, sheet.GetAllPlacedViews().Select(id => id.Value).FirstOrDefault());
    private static object RoomDto(Autodesk.Revit.DB.Architecture.Room room, Document document) => new RevitRoomSummaryDto(room.Id.Value, room.UniqueId, room.Name, room.Number, room.LevelId == ElementId.InvalidElementId ? null : room.LevelId.Value, room.Level?.Name, UnitUtils.ConvertFromInternalUnits(room.Area, UnitTypeId.SquareMeters), Mm(room.Perimeter), room.Location is LocationPoint point ? new { x = Mm(point.Point.X), y = Mm(point.Point.Y), z = Mm(point.Point.Z), unit = "mm" } : null, room.Area > 0, Array.Empty<object>());
    private RevitElementDto Describe(Element element, Document document) { var type = element.GetTypeId() != ElementId.InvalidElementId ? document.GetElement(element.GetTypeId()) as ElementType : null; var symbol = type as FamilySymbol; var level = LevelOf(element, document); var box = element.get_BoundingBox(null); return new RevitElementDto(element.Id.Value, element.UniqueId, element.Name, element.Category?.Name, element.Category?.Id.Value, symbol?.FamilyName, type?.Name, type?.Id.Value, level?.Name, level?.Id.Value, LocationDto(element.Location), box is null ? null : new { min = new { x = Mm(box.Min.X), y = Mm(box.Min.Y), z = Mm(box.Min.Z) }, max = new { x = Mm(box.Max.X), y = Mm(box.Max.Y), z = Mm(box.Max.Z) }, unit = "mm" }, Parameters(element, document).ToDictionary(parameter => parameter.Name, parameter => parameter.Value), element.Pinned, WorksetName(element, document)); }
    private static double Mm(double internalFeet) => UnitUtils.ConvertFromInternalUnits(internalFeet, UnitTypeId.Millimeters);
    private static object? LocationDto(Location location) => location switch { LocationPoint point => new { kind = "point", x = Mm(point.Point.X), y = Mm(point.Point.Y), z = Mm(point.Point.Z), unit = "mm" }, LocationCurve curve => new { kind = "curve", start = new { x = Mm(curve.Curve.GetEndPoint(0).X), y = Mm(curve.Curve.GetEndPoint(0).Y), z = Mm(curve.Curve.GetEndPoint(0).Z), unit = "mm" }, end = new { x = Mm(curve.Curve.GetEndPoint(1).X), y = Mm(curve.Curve.GetEndPoint(1).Y), z = Mm(curve.Curve.GetEndPoint(1).Z), unit = "mm" } }, _ => null };
    private static IReadOnlyList<RevitParameterDto> Parameters(Element element, Document document) => element.Parameters.Cast<Parameter>().Select(parameter => new RevitParameterDto(parameter.Definition.Name, parameter.Id.Value, parameter.StorageType.ToString(), ParameterValue(parameter), ParameterText(parameter), parameter.IsReadOnly, parameter.IsShared, element is not ElementType)).ToArray();
    private static object? ParameterValue(Parameter parameter) => parameter.StorageType switch { StorageType.String => parameter.AsString(), StorageType.Integer => parameter.AsInteger(), StorageType.Double => parameter.AsDouble(), StorageType.ElementId => parameter.AsElementId().Value, _ => null };
    private static string ParameterText(Parameter parameter) => parameter.AsValueString() ?? ParameterValue(parameter)?.ToString() ?? string.Empty;
    private static string? WorksetName(Element element, Document document) { try { return element.WorksetId != WorksetId.InvalidWorksetId ? document.GetWorksetTable().GetWorkset(element.WorksetId)?.Name : null; } catch { return null; } }
    private static Level? LevelOf(Element element, Document document) { try { return element.LevelId != ElementId.InvalidElementId ? document.GetElement(element.LevelId) as Level : null; } catch { return null; } }
    private static Level? FindLevel(Document document, RevitRequest request) { var value = String(request, "levelId") ?? String(request, "level") ?? String(request, "elementId"); if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) return document.GetElement(new ElementId((long)id)) as Level; if (!string.IsNullOrWhiteSpace(value)) return new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>().FirstOrDefault(level => level.Name.Equals(value, StringComparison.OrdinalIgnoreCase) || level.UniqueId.Equals(value, StringComparison.OrdinalIgnoreCase)); return null; }
    private static ViewSheet? FindSheet(Document document, RevitRequest request) { var value = String(request, "sheetId") ?? String(request, "elementId") ?? String(request, "uniqueId") ?? String(request, "sheetNumber"); if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) return document.GetElement(new ElementId((long)id)) as ViewSheet; return new FilteredElementCollector(document).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().FirstOrDefault(sheet => sheet.UniqueId.Equals(value, StringComparison.OrdinalIgnoreCase) || sheet.SheetNumber.Equals(value, StringComparison.OrdinalIgnoreCase)); }
    private static Element? FindElement(Document document, RevitRequest request, params string[] keys) { foreach (var key in keys) { var value = String(request, key); if (string.IsNullOrWhiteSpace(value)) continue; var result = FindElementByValue(document, value); if (result is not null) return result; } return null; }
    private static Element? FindElementByValue(Document document, string value) { if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) return document.GetElement(new ElementId((long)id)); try { return document.GetElement(value); } catch { return null; } }
    private static T? ResolveElementType<T>(Document document, RevitRequest request, out string? error, BuiltInCategory? category = null) where T : ElementType { error = null; var idText = String(request, "typeId") ?? String(request, "familyTypeId") ?? String(request, "titleBlockId"); var name = String(request, "typeName") ?? String(request, "familyTypeName") ?? String(request, "titleBlockName"); var familyName = String(request, "familyName"); var collector = new FilteredElementCollector(document).WhereElementIsElementType().OfClass(typeof(T)); if (category is { } builtInCategory) collector = new FilteredElementCollector(document).OfCategory(builtInCategory).WhereElementIsElementType().OfClass(typeof(T)); var matches = collector.Cast<T>().Where(type => (string.IsNullOrWhiteSpace(idText) || type.Id.Value.ToString(CultureInfo.InvariantCulture) == idText) && (string.IsNullOrWhiteSpace(name) || type.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) && (string.IsNullOrWhiteSpace(familyName) || type is FamilySymbol symbol && symbol.FamilyName.Equals(familyName, StringComparison.OrdinalIgnoreCase))).ToArray(); if (matches.Length == 1) return matches[0]; error = matches.Length == 0 ? BridgeErrorCodes.TypeNotFound : BridgeErrorCodes.AmbiguousType; return null; }
    private static WallType? ResolveWallType(Document document, RevitRequest request, out string? error) => ResolveElementType<WallType>(document, request, out error);
    private static RevitResponse? ValidateWrite(Document document, RevitRequest request) => document.IsReadOnly && !request.DryRun ? RevitResponse.Fail(request, BridgeErrorCodes.DocumentReadOnly, "Document is read-only.") : null;
    private static string? Required(RevitRequest request, string key) => String(request, key) is { Length: > 0 } value ? value : null;
    private static string? String(RevitRequest request, string key) => request.Parameters.TryGetValue(key, out var value) ? value.Trim().Trim('"') : null;
    private static bool? Bool(RevitRequest request, string key) { var value = String(request, key); return bool.TryParse(value, out var result) ? result : null; }
    private static long? Int(RevitRequest request, string key) { var value = String(request, key); return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null; }
    private static IReadOnlyList<long> IntValues(RevitRequest request, string key) { var raw = Raw(request, key); if (raw.ValueKind != JsonValueKind.Array) return Int(request, key) is { } one ? new[] { one } : Array.Empty<long>(); return raw.EnumerateArray().Where(value => value.TryGetInt64(out _)).Select(value => value.GetInt64()).ToArray(); }
    private static IReadOnlyList<string> StringValues(RevitRequest request, string key) { var raw = Raw(request, key); return raw.ValueKind == JsonValueKind.Array ? raw.EnumerateArray().Select(value => value.ToString()).Where(value => value.Length > 0).ToArray() : String(request, key)?.Split(',').Select(value => value.Trim()).Where(value => value.Length > 0).ToArray() ?? Array.Empty<string>(); }
    private static JsonElement Raw(RevitRequest request, string key) { if (!request.Parameters.TryGetValue(key, out var value)) return default; try { using var document = JsonDocument.Parse(value); return document.RootElement.Clone(); } catch { return JsonSerializer.SerializeToElement(value); } }
    private static Dictionary<string, JsonElement> Object(RevitRequest request, string key) { var raw = Raw(request, key); return raw.ValueKind == JsonValueKind.Object ? raw.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase); }
    private static List<JsonElement> ObjectArray(RevitRequest request, string key) { var raw = Raw(request, key); return raw.ValueKind == JsonValueKind.Array ? raw.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.Object).Select(value => value.Clone()).ToList() : new List<JsonElement>(); }
    private bool TryLength(RevitRequest request, string key, out double value, bool optional = false) { var number = Raw(request, key); if (number.ValueKind == JsonValueKind.Undefined && optional) { value = 0; return true; } var raw = number.ValueKind == JsonValueKind.Object && number.TryGetProperty("value", out var nested) ? nested : number; if (!raw.TryGetDouble(out var external) || double.IsNaN(external) || double.IsInfinity(external)) { value = 0; return false; } var unit = number.ValueKind == JsonValueKind.Object && number.TryGetProperty("unit", out var unitValue) ? unitValue.GetString() : String(request, "unit") ?? "mm"; try { value = _units.ToInternalLength(external, unit ?? "mm"); return true; } catch { value = 0; return false; } }
    private string LengthText(RevitRequest request, double internalValue) { try { return _units.FromInternalLength(internalValue, "mm").ToString("G17", CultureInfo.InvariantCulture) + " mm"; } catch { return internalValue.ToString(CultureInfo.InvariantCulture); } }
    private bool TryCoordinate(RevitRequest request, string key, out XYZ point, out string? error) { point = XYZ.Zero; error = null; var raw = Raw(request, key); if (raw.ValueKind != JsonValueKind.Object || !raw.TryGetProperty("x", out var x) || !raw.TryGetProperty("y", out var y)) { error = $"{key} requires x and y."; return false; } var z = raw.TryGetProperty("z", out var zValue) ? zValue : default; var unit = raw.TryGetProperty("unit", out var unitValue) ? unitValue.GetString() ?? "mm" : "mm"; if (!x.TryGetDouble(out var xv) || !y.TryGetDouble(out var yv) || (z.ValueKind != JsonValueKind.Undefined && !z.TryGetDouble(out _))) { error = $"{key} coordinates must be finite numbers."; return false; } var zv = z.ValueKind == JsonValueKind.Undefined ? 0 : z.GetDouble(); try { point = new XYZ(_units.ToInternalLength(xv, unit), _units.ToInternalLength(yv, unit), _units.ToInternalLength(zv, unit)); return true; } catch { error = $"{key} has invalid unit '{unit}'."; return false; } }
    private bool TryCoordinateBox(RevitRequest request, string key, out (XYZ Min, XYZ Max) box, out string? error) { box = default; error = null; var raw = Raw(request, key); if (raw.ValueKind != JsonValueKind.Object || !raw.TryGetProperty("min", out var min) || !raw.TryGetProperty("max", out var max)) { error = "boundingBox requires min and max."; return false; } var minRequest = new RevitRequest(); minRequest.Parameters[key] = min.GetRawText(); var maxRequest = new RevitRequest(); maxRequest.Parameters[key] = max.GetRawText(); if (!TryCoordinate(minRequest, key, out var minPoint, out error) || !TryCoordinate(maxRequest, key, out var maxPoint, out error)) return false; box = (minPoint, maxPoint); return true; }
    private double? Angle(RevitRequest request, string key) { var raw = Raw(request, key); if (!raw.TryGetDouble(out var value)) return null; var unit = String(request, "angleUnit") ?? "deg"; try { return unit.StartsWith("deg", StringComparison.OrdinalIgnoreCase) ? value * Math.PI / 180d : value; } catch { return null; } }
    private static bool Compare(string actual, string expected, string operation) => operation.ToLowerInvariant() switch { "contains" => actual.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0, "starts_with" => actual.StartsWith(expected, StringComparison.OrdinalIgnoreCase), "not_equals" => !actual.Equals(expected, StringComparison.OrdinalIgnoreCase), _ => actual.Equals(expected, StringComparison.OrdinalIgnoreCase) };
    private static int Clamp(int value, int minimum, int maximum) => Math.Min(maximum, Math.Max(minimum, value));
    private static bool TryLoops(RevitRequest request, out IList<CurveLoop> loops, out string? error) { loops = new List<CurveLoop>(); error = null; var raw = Raw(request, "boundaryLoops"); if (raw.ValueKind != JsonValueKind.Array) { error = "boundaryLoops must be an array."; return false; } foreach (var loop in raw.EnumerateArray()) { if (loop.ValueKind != JsonValueKind.Array || loop.GetArrayLength() < 3) { error = "Each boundary loop needs at least three vertices."; return false; } var points = new List<XYZ>(); foreach (var vertex in loop.EnumerateArray()) { if (vertex.ValueKind != JsonValueKind.Object || !vertex.TryGetProperty("x", out var x) || !vertex.TryGetProperty("y", out var y)) { error = "Boundary vertices require x and y."; return false; } var unit = vertex.TryGetProperty("unit", out var unitValue) ? unitValue.GetString() ?? "mm" : "mm"; if (!x.TryGetDouble(out var xv) || !y.TryGetDouble(out var yv)) { error = "Boundary coordinates must be finite numbers."; return false; } var z = vertex.TryGetProperty("z", out var zv) && zv.TryGetDouble(out var zValue) ? zValue : 0; var converter = new RevitVersionAdapter("runtime"); try { points.Add(new XYZ(converter.ToInternalLength(xv, unit), converter.ToInternalLength(yv, unit), converter.ToInternalLength(z, unit))); } catch { error = "Boundary vertex unit is invalid."; return false; } } if (points[0].DistanceTo(points[points.Count - 1]) > 1e-8) { error = "Boundary loops must be explicitly closed."; return false; } var segments = new List<Curve>(); for (var index = 0; index < points.Count - 1; index++) segments.Add(Line.CreateBound(points[index], points[index + 1])); try { loops.Add(CurveLoop.Create(segments)); } catch (Exception exception) { error = $"Invalid boundary loop: {exception.Message}"; return false; } } return loops.Count > 0; }
}

internal sealed class ParameterOperationException : Exception
{
    public ParameterOperationException(string code, string message) : base(message) => Code = code;
    public string Code { get; }
}

internal sealed class RevitOperationException : Exception
{
    public RevitOperationException(string code, string message) : base(message) => Code = code;
    public string Code { get; }
}
