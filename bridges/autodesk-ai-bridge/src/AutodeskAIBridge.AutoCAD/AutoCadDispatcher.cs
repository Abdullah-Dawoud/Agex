#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AutodeskAIBridge.Core;

namespace AutodeskAIBridge.AutoCAD;

/// <summary>Executes allow-listed AutoCAD operations inside command context and safe document transactions.</summary>
public sealed class AutoCadDispatcher
{
    private readonly DocumentCollection _documents;
    // One AutoCAD session per process: the unit adapter is shared by the static parsing helpers.
    private static IAutoCadVersionAdapter _units = new AutoCadVersionAdapter("runtime");

    public AutoCadDispatcher(DocumentCollection documents, IAutoCadVersionAdapter? units = null) { _documents = documents; _units = units ?? new AutoCadVersionAdapter("runtime"); }

    public async Task<AutoCadResponse> DispatchAsync(AutoCadRequest request, CancellationToken cancellationToken)
    {
        AutoCadResponse? response = null;
        try
        {
            await _documents.ExecuteInCommandContextAsync(_ => ExecuteInCommandContextAsync(request, cancellationToken, value => response = value), null);
            return response ?? AutoCadResponse.Fail(request, "NO_RESPONSE", "AutoCAD command context returned no response.");
        }
        catch (OperationCanceledException) { return AutoCadResponse.Fail(request, BridgeErrorCodes.Cancelled, "Operation was cancelled."); }
        catch (Exception exception) { return AutoCadResponse.Fail(request, exception.Message.IndexOf("lock", StringComparison.OrdinalIgnoreCase) >= 0 ? BridgeErrorCodes.DocumentLockFailed : BridgeErrorCodes.TransactionFailed, exception.Message); }
    }

    private async Task ExecuteInCommandContextAsync(AutoCadRequest request, CancellationToken cancellationToken, Action<AutoCadResponse> complete)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!AutoCadOperationCatalog.IsKnown(request.Operation)) { complete(AutoCadResponse.Fail(request, BridgeErrorCodes.UnsupportedOperation, request.Operation)); return; }
        if (request.Operation.Equals("autocad.get_application_info", StringComparison.OrdinalIgnoreCase)) { complete(GetApplicationInfo(request)); return; }
        var document = ResolveDocument(request) ?? Application.DocumentManager.MdiActiveDocument;
        if (document is null) { complete(AutoCadResponse.Fail(request, BridgeErrorCodes.NoOpenDocument, "No active AutoCAD document exists.")); return; }
        if (document.IsReadOnly && !request.DryRun && IsWriteOperation(request.Operation) && !request.Operation.Equals("autocad.save_as", StringComparison.OrdinalIgnoreCase)) { complete(AutoCadResponse.Fail(request, BridgeErrorCodes.DocumentReadOnly, "Active AutoCAD document is read-only.")); return; }
        AutoCadResponse response = request.Operation.ToLowerInvariant() switch
        {
            "autocad.health" => AutoCadResponse.Ok(request, new AutoCadHealth { ActiveDocument = document.Name }),
            "autocad.list_documents" => ListDocuments(request),
            "autocad.get_document_info" or "autocad.get_active_document" => GetDocumentInfo(document, request),
            "autocad.get_database_info" => GetDatabaseInfo(document, request),
            "autocad.get_units" => GetUnits(document, request),
            "autocad.list_layers" => ListLayers(document, request),
            "autocad.get_layer" => GetLayer(document, request),
            "autocad.create_layer" => CreateLayer(document, request),
            "autocad.modify_layer" => ModifyLayer(document, request),
            "autocad.query_entities" => QueryEntities(document, request),
            "autocad.get_entity" => GetEntity(document, request),
            "autocad.get_entities" => GetEntities(document, request),
            "autocad.create_line" => CreateGeometry(document, request, "line"),
            "autocad.create_polyline" => CreateGeometry(document, request, "polyline"),
            "autocad.create_circle" => CreateGeometry(document, request, "circle"),
            "autocad.create_arc" => CreateGeometry(document, request, "arc"),
            "autocad.create_rectangle" => CreateGeometry(document, request, "rectangle"),
            "autocad.create_text" => CreateText(document, request, false),
            "autocad.create_mtext" => CreateText(document, request, true),
            "autocad.list_blocks" => ListBlocks(document, request),
            "autocad.get_block_definition" => GetBlockDefinition(document, request),
            "autocad.list_block_references" => ListBlockReferences(document, request),
            "autocad.insert_block" => InsertBlock(document, request),
            "autocad.modify_block_reference" => ModifyBlockReference(document, request),
            "autocad.move_entities" => TransformEntities(document, request, "move"),
            "autocad.copy_entities" => TransformEntities(document, request, "copy"),
            "autocad.rotate_entities" => TransformEntities(document, request, "rotate"),
            "autocad.scale_entities" => TransformEntities(document, request, "scale"),
            "autocad.erase_entities" => EraseEntities(document, request),
            "autocad.change_layer" => ChangeLayer(document, request),
            "autocad.set_properties" => SetProperties(document, request),
            "autocad.create_dimension" => CreateDimension(document, request),
            "autocad.regen" => Regen(document, request),
            "autocad.save" => Save(document, request),
            "autocad.save_as" => SaveAs(document, request),
            _ => AutoCadResponse.Fail(request, BridgeErrorCodes.UnsupportedOperation, request.Operation)
        };
        complete(response);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private AutoCadResponse GetApplicationInfo(AutoCadRequest request) => AutoCadResponse.Ok(request, new { product = "AutoCAD", version = Application.GetSystemVariable("ACADVER")?.ToString(), documentCount = _documents.Count });
    private AutoCadResponse ListDocuments(AutoCadRequest request) => AutoCadResponse.Ok(request, _documents.Cast<Document>().Where(document => document is not null).Select(document => new AutoCadDocumentDto(document!.Name, document.Database.Filename, document == _documents.MdiActiveDocument, document.IsReadOnly, false)).ToArray());
    private static AutoCadResponse GetDocumentInfo(Document document, AutoCadRequest request) => AutoCadResponse.Ok(request, new AutoCadDocumentDto(document.Name, document.Database.Filename, document == Application.DocumentManager.MdiActiveDocument, document.IsReadOnly, false));
    private static AutoCadResponse GetDatabaseInfo(Document document, AutoCadRequest request) => AutoCadResponse.Ok(request, new { path = document.Database.Filename, insUnits = document.Database.Insunits.ToString(), currentLayerId = document.Database.Clayer.Handle.ToString(), currentSpaceId = document.Database.CurrentSpaceId.Handle.ToString() });
    private AutoCadResponse GetUnits(Document document, AutoCadRequest request) => AutoCadResponse.Ok(request, new AutoCadUnitDto(document.Database.Insunits.ToString(), document.Database.Measurement.ToString(), document.Database.Lunits.ToString(), document.Database.Aunits.ToString(), 1));

    private static AutoCadResponse ListLayers(Document document, AutoCadRequest request)
    {
        using var transaction = document.Database.TransactionManager.StartTransaction(); var table = (LayerTable)transaction.GetObject(document.Database.LayerTableId, OpenMode.ForRead); var layers = table.Cast<ObjectId>().Select(id => (LayerTableRecord)transaction.GetObject(id, OpenMode.ForRead)).Select(layer => LayerDto(layer)).ToArray(); transaction.Commit(); return AutoCadResponse.Ok(request, layers);
    }

    private static AutoCadResponse GetLayer(Document document, AutoCadRequest request)
    {
        var name = Text(request, "name") ?? Text(request, "layer"); using var transaction = document.Database.TransactionManager.StartTransaction(); var table = (LayerTable)transaction.GetObject(document.Database.LayerTableId, OpenMode.ForRead); ObjectId id = ObjectId.Null; if (!string.IsNullOrWhiteSpace(name) && table.Has(name)) id = table[name]; else if (HandleId(document.Database, Text(request, "handle")) is { } handle) id = handle; if (id.IsNull) return AutoCadResponse.Fail(request, BridgeErrorCodes.EntityNotFound, "Layer not found."); return AutoCadResponse.Ok(request, LayerDto((LayerTableRecord)transaction.GetObject(id, OpenMode.ForRead)));
    }

    private static AutoCadResponse CreateLayer(Document document, AutoCadRequest request)
    {
        var name = Text(request, "name"); if (string.IsNullOrWhiteSpace(name)) return AutoCadResponse.Fail(request, BridgeErrorCodes.InvalidRequest, "name is required."); if (request.DryRun) return Plan(request, $"Create layer '{name}'."); return FromTransaction(request, new AutoCadTransactionRunner().Run(document, context => { var table = (LayerTable)context.Transaction.GetObject(document.Database.LayerTableId, OpenMode.ForRead); if (table.Has(name)) throw new AutoCadOperationException("LAYER_EXISTS", $"Layer '{name}' already exists."); table.UpgradeOpen(); var layer = new LayerTableRecord { Name = name }; ApplyLayerProperties(layer, request, document.Database, context.Transaction); var id = table.Add(layer); context.Transaction.AddNewlyCreatedDBObject(layer, true); context.TrackCreated(id); return LayerDto(layer); }));
    }

    private static AutoCadResponse ModifyLayer(Document document, AutoCadRequest request)
    {
        var id = ResolveLayerId(document.Database, request); if (id.IsNull) return AutoCadResponse.Fail(request, BridgeErrorCodes.EntityNotFound, "Layer not found."); if (id == document.Database.Clayer) return AutoCadResponse.Fail(request, BridgeErrorCodes.ElementNotModifiable, "Current layer cannot be modified by this operation."); if (request.DryRun) return Plan(request, "Modify layer properties."); return FromTransaction(request, new AutoCadTransactionRunner().Run(document, context => { var layer = (LayerTableRecord)context.Transaction.GetObject(id, OpenMode.ForWrite); if (layer.Name.Equals("0", StringComparison.OrdinalIgnoreCase)) throw new AutoCadOperationException(BridgeErrorCodes.ElementNotModifiable, "System layer 0 cannot be modified."); ApplyLayerProperties(layer, request, document.Database, context.Transaction); context.TrackModified(id); return LayerDto(layer); }));
    }

    private AutoCadResponse QueryEntities(Document document, AutoCadRequest request)
    {
        using var transaction = document.Database.TransactionManager.StartTransaction(); var space = (BlockTableRecord)transaction.GetObject(document.Database.CurrentSpaceId, OpenMode.ForRead); var all = space.Cast<ObjectId>().Select(id => transaction.GetObject(id, OpenMode.ForRead) as Entity).Where(entity => entity is not null).Cast<Entity>().Where(entity => Matches(entity, document.Database, request)).ToList(); var offset = Math.Max(0, Number(request, "offset") ?? 0); var limit = Clamp(Number(request, "limit") ?? 100, 1, 1000); var page = all.Skip(offset).Take(limit).Select(entity => EntityDto(entity, document.Database, Bool(request, "compact") ?? false)).ToArray(); transaction.Commit(); return AutoCadResponse.Ok(request, new { items = page, offset, limit, totalCount = all.Count, hasMore = offset + page.Length < all.Count });
    }

    private static AutoCadResponse GetEntity(Document document, AutoCadRequest request)
    {
        var id = ResolveEntityId(document.Database, request); if (id.IsNull) return AutoCadResponse.Fail(request, BridgeErrorCodes.EntityNotFound, "Entity not found."); using var transaction = document.Database.TransactionManager.StartTransaction(); var entity = transaction.GetObject(id, OpenMode.ForRead) as Entity; if (entity is null) return AutoCadResponse.Fail(request, BridgeErrorCodes.InvalidEntity, "Object is not an entity."); return AutoCadResponse.Ok(request, EntityDto(entity, document.Database, Bool(request, "compact") ?? false));
    }

    private static AutoCadResponse GetEntities(Document document, AutoCadRequest request)
    {
        var ids = StringValues(request, "entityIds"); if (ids.Count == 0) return AutoCadResponse.Fail(request, BridgeErrorCodes.InvalidRequest, "entityIds is required."); using var transaction = document.Database.TransactionManager.StartTransaction(); var entities = ids.Select(value => HandleId(document.Database, value)).Where(id => id is { IsNull: false }).Select(id => transaction.GetObject(id!.Value, OpenMode.ForRead) as Entity).Where(entity => entity is not null).Select(entity => EntityDto(entity!, document.Database, Bool(request, "compact") ?? false)).ToArray(); return AutoCadResponse.Ok(request, entities);
    }

    private AutoCadResponse CreateGeometry(Document document, AutoCadRequest request, string kind)
    {
        if (request.DryRun) { if (!ValidateGeometry(request, kind, document.Database, out var error)) return AutoCadResponse.Fail(request, error!.Code, error.Message); return Plan(request, $"Create {kind} entity."); }
        return FromTransaction(request, new AutoCadTransactionRunner().Run(document, context => { if (!ValidateGeometry(request, kind, document.Database, out var validationError)) throw validationError!; var entity = BuildGeometry(kind, request, document.Database); ApplyEntityProperties(entity, request, document.Database, context.Transaction); var space = (BlockTableRecord)context.Transaction.GetObject(document.Database.CurrentSpaceId, OpenMode.ForWrite); var id = space.AppendEntity(entity); context.Transaction.AddNewlyCreatedDBObject(entity, true); context.TrackCreated(id); return EntityDto(entity, document.Database, false); }));
    }

    private static AutoCadResponse CreateText(Document document, AutoCadRequest request, bool mtext)
    {
        var content = Text(request, "content") ?? Text(request, "text"); if (string.IsNullOrWhiteSpace(content)) return AutoCadResponse.Fail(request, BridgeErrorCodes.InvalidRequest, "content is required."); if (!TryPoint(request, "position", document.Database, out _, out var error)) return AutoCadResponse.Fail(request, BridgeErrorCodes.InvalidCoordinate, error ?? "position is required."); if (request.DryRun) return Plan(request, $"Create {(mtext ? "mtext" : "text")} entity."); return FromTransaction(request, new AutoCadTransactionRunner().Run(document, context => { TryPoint(request, "position", document.Database, out var point, out _); Entity entity = mtext ? new MText { Location = point, Contents = content, TextHeight = Length(request, "height", document.Database, 2.5) } : new DBText { Position = point, TextString = content, Height = Length(request, "height", document.Database, 2.5) }; ApplyTextStyle(entity, request, document.Database, context.Transaction); var rotation = Angle(request, "rotation"); if (rotation is { } angle) entity.TransformBy(Matrix3d.Rotation(angle, Vector3d.ZAxis, point)); ApplyEntityProperties(entity, request, document.Database, context.Transaction); var space = (BlockTableRecord)context.Transaction.GetObject(document.Database.CurrentSpaceId, OpenMode.ForWrite); var id = space.AppendEntity(entity); context.Transaction.AddNewlyCreatedDBObject(entity, true); context.TrackCreated(id); return EntityDto(entity, document.Database, false); }));
    }

    private static AutoCadResponse ListBlocks(Document document, AutoCadRequest request)
    {
        using var transaction = document.Database.TransactionManager.StartTransaction(); var table = (BlockTable)transaction.GetObject(document.Database.BlockTableId, OpenMode.ForRead); var blocks = table.Cast<ObjectId>().Select(id => (BlockTableRecord)transaction.GetObject(id, OpenMode.ForRead)).Where(block => !block.IsAnonymous || Bool(request, "includeAnonymous") == true).Select(block => new AutoCadBlockSummary(block.Name, block.ObjectId.Handle.ToString(), block.IsAnonymous, block.IsLayout)).ToArray(); transaction.Commit(); return AutoCadResponse.Ok(request, blocks);
    }

    private static AutoCadResponse GetBlockDefinition(Document document, AutoCadRequest request)
    {
        var block = FindBlock(document.Database, Text(request, "blockName") ?? Text(request, "name")); if (block.IsNull) return AutoCadResponse.Fail(request, BridgeErrorCodes.BlockNotFound, "Block definition not found."); using var transaction = document.Database.TransactionManager.StartTransaction(); var definition = (BlockTableRecord)transaction.GetObject(block, OpenMode.ForRead); var entities = definition.Cast<ObjectId>().Select(id => transaction.GetObject(id, OpenMode.ForRead) as Entity).Where(entity => entity is not null).Select(entity => EntityDto(entity!, document.Database, true)).ToArray(); return AutoCadResponse.Ok(request, new { block = new AutoCadBlockSummary(definition.Name, block.Handle.ToString(), definition.IsAnonymous, definition.IsLayout), entities });
    }

    private static AutoCadResponse ListBlockReferences(Document document, AutoCadRequest request)
    {
        var blockName = Text(request, "blockName"); using var transaction = document.Database.TransactionManager.StartTransaction(); var space = (BlockTableRecord)transaction.GetObject(document.Database.CurrentSpaceId, OpenMode.ForRead); var refs = space.Cast<ObjectId>().Select(id => transaction.GetObject(id, OpenMode.ForRead) as BlockReference).Where(reference => reference is not null).Where(reference => string.IsNullOrWhiteSpace(blockName) || ((BlockTableRecord)transaction.GetObject(reference!.BlockTableRecord, OpenMode.ForRead)).Name.Equals(blockName, StringComparison.OrdinalIgnoreCase)).Select(reference => EntityDto(reference!, document.Database, false)).ToArray(); return AutoCadResponse.Ok(request, refs);
    }

    private static AutoCadResponse InsertBlock(Document document, AutoCadRequest request)
    {
        var name = Text(request, "blockName") ?? Text(request, "name"); var block = FindBlock(document.Database, name); if (block.IsNull) return AutoCadResponse.Fail(request, BridgeErrorCodes.BlockNotFound, $"Block '{name}' was not found."); if (!TryPoint(request, "position", document.Database, out _, out var error)) return AutoCadResponse.Fail(request, BridgeErrorCodes.InvalidCoordinate, error ?? "position is required."); if (request.DryRun) return Plan(request, $"Insert block '{name}'."); return FromTransaction(request, new AutoCadTransactionRunner().Run(document, context => { TryPoint(request, "position", document.Database, out var point, out _); var reference = new BlockReference(point, block) { Rotation = Angle(request, "rotation") ?? 0, ScaleFactors = new Scale3d(NumberDouble(request, "scale") ?? 1) }; ApplyEntityProperties(reference, request, document.Database, context.Transaction); var space = (BlockTableRecord)context.Transaction.GetObject(document.Database.CurrentSpaceId, OpenMode.ForWrite); var id = space.AppendEntity(reference); context.Transaction.AddNewlyCreatedDBObject(reference, true); context.TrackCreated(id); AddAttributes(reference, block, request, context.Transaction); return EntityDto(reference, document.Database, false); }));
    }

    private static AutoCadResponse ModifyBlockReference(Document document, AutoCadRequest request)
    {
        var id = ResolveEntityId(document.Database, request); if (id.IsNull) return AutoCadResponse.Fail(request, BridgeErrorCodes.EntityNotFound, "Block reference not found."); if (request.DryRun) return Plan(request, "Modify block reference."); return FromTransaction(request, new AutoCadTransactionRunner().Run(document, context => { var reference = context.Transaction.GetObject(id, OpenMode.ForWrite) as BlockReference; if (reference is null) throw new AutoCadOperationException(BridgeErrorCodes.InvalidEntity, "Object is not a block reference."); if (TryPoint(request, "position", document.Database, out var point, out _)) reference.Position = point; if (NumberDouble(request, "scale") is { } scale) reference.ScaleFactors = new Scale3d(scale); if (Angle(request, "rotation") is { } angle) reference.Rotation = angle; ApplyEntityProperties(reference, request, document.Database, context.Transaction); context.TrackModified(id); return EntityDto(reference, document.Database, false); }));
    }

    private static AutoCadResponse TransformEntities(Document document, AutoCadRequest request, string operation)
    {
        var ids = ResolveEntityIds(document.Database, request); if (ids.Count == 0) return AutoCadResponse.Fail(request, BridgeErrorCodes.EntityNotFound, "entityIds is required."); Matrix3d matrix; if (operation == "move") { if (!TryVector(request, "translation", document.Database, out var vector, out var error)) return AutoCadResponse.Fail(request, BridgeErrorCodes.InvalidCoordinate, error ?? "translation is required."); matrix = Matrix3d.Displacement(vector); } else if (operation == "rotate") { var angle = Angle(request, "angle"); if (angle is null) return AutoCadResponse.Fail(request, BridgeErrorCodes.InvalidCoordinate, "angle is required."); if (!TryPoint(request, "basePoint", document.Database, out var basePoint, out var baseError)) return AutoCadResponse.Fail(request, BridgeErrorCodes.InvalidCoordinate, baseError ?? "basePoint is required."); matrix = Matrix3d.Rotation(angle.Value, Vector3d.ZAxis, basePoint); } else if (operation == "scale") { if (!TryPoint(request, "basePoint", document.Database, out var scalePoint, out var scaleError)) return AutoCadResponse.Fail(request, BridgeErrorCodes.InvalidCoordinate, scaleError ?? "basePoint is required."); if (NumberDouble(request, "scale") is not { } factor || factor <= 0) return AutoCadResponse.Fail(request, BridgeErrorCodes.InvalidCoordinate, "positive scale is required."); matrix = Matrix3d.Scaling(factor, scalePoint); } else matrix = Matrix3d.Identity;
        if (request.DryRun) return Plan(request, $"{operation} {ids.Count} entit(y/ies)."); return FromTransaction(request, new AutoCadTransactionRunner().Run(document, context => { var space = (BlockTableRecord)context.Transaction.GetObject(document.Database.CurrentSpaceId, OpenMode.ForWrite); var output = new List<string>(); foreach (var id in ids) { var entity = context.Transaction.GetObject(id, OpenMode.ForWrite) as Entity; if (entity is null) throw new AutoCadOperationException(BridgeErrorCodes.InvalidEntity, "Object is not an entity."); if (operation == "copy") { var clone = (Entity)entity.Clone(); clone.TransformBy(matrix); var copyId = space.AppendEntity(clone); context.Transaction.AddNewlyCreatedDBObject(clone, true); context.TrackCreated(copyId); output.Add(copyId.Handle.ToString()); } else { entity.TransformBy(matrix); context.TrackModified(id); output.Add(id.Handle.ToString()); } } return new { operation, handles = output }; }));
    }

    private static AutoCadResponse EraseEntities(Document document, AutoCadRequest request)
    {
        var ids = ResolveEntityIds(document.Database, request); if (ids.Count == 0) return AutoCadResponse.Fail(request, BridgeErrorCodes.EntityNotFound, "entityIds is required."); if (request.DryRun) return Plan(request, $"Erase {ids.Count} entit(y/ies)."); return FromTransaction(request, new AutoCadTransactionRunner().Run(document, context => { foreach (var id in ids) { var entity = context.Transaction.GetObject(id, OpenMode.ForWrite) as Entity; if (entity is null) throw new AutoCadOperationException(BridgeErrorCodes.InvalidEntity, "Object is not an entity."); entity.Erase(true); context.TrackDeleted(id); } return new { erasedHandles = ids.Select(id => id.Handle.ToString()).ToArray() }; }));
    }

    private static AutoCadResponse ChangeLayer(Document document, AutoCadRequest request)
    {
        var layer = Text(request, "layer") ?? Text(request, "layerName"); if (string.IsNullOrWhiteSpace(layer)) return AutoCadResponse.Fail(request, BridgeErrorCodes.InvalidRequest, "layer is required."); var layerId = ResolveLayerId(document.Database, layer); if (layerId.IsNull) return AutoCadResponse.Fail(request, BridgeErrorCodes.EntityNotFound, "Target layer not found."); var ids = ResolveEntityIds(document.Database, request); if (ids.Count == 0) return AutoCadResponse.Fail(request, BridgeErrorCodes.EntityNotFound, "entityIds is required."); if (request.DryRun) return Plan(request, $"Change layer for {ids.Count} entit(y/ies)."); return FromTransaction(request, new AutoCadTransactionRunner().Run(document, context => { var name = ((LayerTableRecord)context.Transaction.GetObject(layerId, OpenMode.ForRead)).Name; foreach (var id in ids) { var entity = (Entity)context.Transaction.GetObject(id, OpenMode.ForWrite); entity.Layer = name; context.TrackModified(id); } return new { layer = name, handles = ids.Select(id => id.Handle.ToString()).ToArray() }; }));
    }

    private static AutoCadResponse SetProperties(Document document, AutoCadRequest request)
    {
        var ids = ResolveEntityIds(document.Database, request); if (ids.Count == 0) return AutoCadResponse.Fail(request, BridgeErrorCodes.EntityNotFound, "entityIds is required."); if (request.DryRun) return Plan(request, $"Set properties on {ids.Count} entit(y/ies)."); return FromTransaction(request, new AutoCadTransactionRunner().Run(document, context => { var output = new List<object>(); foreach (var id in ids) { var entity = context.Transaction.GetObject(id, OpenMode.ForWrite) as Entity; if (entity is null) throw new AutoCadOperationException(BridgeErrorCodes.InvalidEntity, "Object is not an entity."); ApplyEntityProperties(entity, request, document.Database, context.Transaction); context.TrackModified(id); output.Add(EntityDto(entity, document.Database, false)); } return output; }));
    }

    private static AutoCadResponse CreateDimension(Document document, AutoCadRequest request)
    {
        if (!TryPoint(request, "start", document.Database, out var start, out var error)) return AutoCadResponse.Fail(request, BridgeErrorCodes.InvalidCoordinate, error ?? "start is required."); if (!TryPoint(request, "end", document.Database, out var end, out var endError)) return AutoCadResponse.Fail(request, BridgeErrorCodes.InvalidCoordinate, endError ?? "end is required."); if (!TryPoint(request, "dimensionLine", document.Database, out var dimensionLine, out var lineError)) return AutoCadResponse.Fail(request, BridgeErrorCodes.InvalidCoordinate, lineError ?? "dimensionLine is required."); if (request.DryRun) return Plan(request, "Create aligned dimension."); return FromTransaction(request, new AutoCadTransactionRunner().Run(document, context => { var dimension = new AlignedDimension(start, end, dimensionLine, Text(request, "text") ?? string.Empty, document.Database.Dimstyle); ApplyEntityProperties(dimension, request, document.Database, context.Transaction); var space = (BlockTableRecord)context.Transaction.GetObject(document.Database.CurrentSpaceId, OpenMode.ForWrite); var id = space.AppendEntity(dimension); context.Transaction.AddNewlyCreatedDBObject(dimension, true); context.TrackCreated(id); return EntityDto(dimension, document.Database, false); }));
    }

    private static AutoCadResponse Regen(Document document, AutoCadRequest request) { document.Editor.Regen(); return AutoCadResponse.Ok(request, new { regenerated = true }); }
    private static AutoCadResponse Save(Document document, AutoCadRequest request) { if (request.DryRun) return Plan(request, "Save active drawing."); if (document.IsReadOnly) return AutoCadResponse.Fail(request, BridgeErrorCodes.DocumentReadOnly, "Drawing is read-only."); using var documentLock = document.LockDocument(); document.Database.SaveAs(document.Name, true, DwgVersion.Current, document.Database.SecurityParameters); return AutoCadResponse.Ok(request, new { path = document.Name, saved = true }); }
    private static AutoCadResponse SaveAs(Document document, AutoCadRequest request) { var path = Text(request, "path"); if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !path.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)) return AutoCadResponse.Fail(request, BridgeErrorCodes.InvalidPath, "path must be an absolute .dwg path."); path = Path.GetFullPath(path); var overwrite = Bool(request, "overwrite") ?? false; if (File.Exists(path) && !overwrite) return AutoCadResponse.Fail(request, BridgeErrorCodes.FileAlreadyExists, "Destination exists; set overwrite=true explicitly."); if (request.DryRun) return Plan(request, $"Save drawing as '{path}'."); using var documentLock = document.LockDocument(); document.Database.SaveAs(path, true, DwgVersion.Current, document.Database.SecurityParameters); return AutoCadResponse.Ok(request, new { path, saved = true }); }

    private Document? ResolveDocument(AutoCadRequest request) { var requested = Text(request, "documentId") ?? Text(request, "document") ?? Text(request, "path"); if (string.IsNullOrWhiteSpace(requested)) return _documents.MdiActiveDocument; return _documents.Cast<Document>().FirstOrDefault(document => document is not null && (document.Name.Equals(requested, StringComparison.OrdinalIgnoreCase) || document.Database.Filename.Equals(requested, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool Matches(Entity entity, Database database, AutoCadRequest request)
    {
        var type = Text(request, "entityType") ?? Text(request, "dxfType"); if (!string.IsNullOrWhiteSpace(type) && !entity.GetRXClass().DxfName.Equals(type, StringComparison.OrdinalIgnoreCase)) return false; var layer = Text(request, "layer"); if (!string.IsNullOrWhiteSpace(layer) && !entity.Layer.Equals(layer, StringComparison.OrdinalIgnoreCase)) return false; var handle = Text(request, "handle"); var objectId = Text(request, "objectId"); if (!string.IsNullOrWhiteSpace(handle) && !entity.ObjectId.Handle.ToString().Equals(handle, StringComparison.OrdinalIgnoreCase)) return false; if (!string.IsNullOrWhiteSpace(objectId) && !entity.ObjectId.ToString().Equals(objectId, StringComparison.OrdinalIgnoreCase)) return false; var color = Number(request, "color"); if (color is { } colorIndex && entity.ColorIndex != colorIndex) return false; var linetype = Text(request, "linetype"); if (!string.IsNullOrWhiteSpace(linetype) && !entity.Linetype.Equals(linetype, StringComparison.OrdinalIgnoreCase)) return false; var text = Text(request, "textContent") ?? Text(request, "text"); if (!string.IsNullOrWhiteSpace(text) && EntityText(entity).IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) return false; var block = Text(request, "blockName"); if (!string.IsNullOrWhiteSpace(block) && entity is BlockReference reference) { using var transaction = database.TransactionManager.StartOpenCloseTransaction(); var definition = transaction.GetObject(reference.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord; if (definition is null || !definition.Name.Equals(block, StringComparison.OrdinalIgnoreCase)) return false; } if (TryRegion(request, database, out var min, out var max) && (entity.GeometricExtents.MaxPoint.X < min.X || entity.GeometricExtents.MinPoint.X > max.X || entity.GeometricExtents.MaxPoint.Y < min.Y || entity.GeometricExtents.MinPoint.Y > max.Y || entity.GeometricExtents.MaxPoint.Z < min.Z || entity.GeometricExtents.MinPoint.Z > max.Z)) return false; return true;
    }

    private static string EntityText(Entity entity) => entity switch { AttributeReference attribute => attribute.TextString, DBText text => text.TextString, MText mtext => mtext.Contents, _ => string.Empty };
    private static AutoCadLayerSummary LayerDto(LayerTableRecord layer) => new(layer.Name, layer.ObjectId.Handle.ToString(), layer.IsOff, layer.IsFrozen, layer.IsLocked, layer.IsPlottable, layer.Color?.ToString(), layer.LinetypeObjectId.IsNull ? null : layer.LinetypeObjectId.Handle.ToString(), layer.LineWeight.ToString());
    private static AutoCadEntityDto EntityDto(Entity entity, Database database, bool compact) { var bounds = entity.GeometricExtents; var geometry = compact ? new { type = entity.GetRXClass().DxfName } : GeometrySummary(entity); return new AutoCadEntityDto(entity.ObjectId.ToString(), entity.ObjectId.Handle.ToString(), entity.GetRXClass().DxfName, entity.Layer, entity.Color?.ToString() ?? string.Empty, entity.Linetype, entity.LineWeight.ToString(), new { min = new { x = bounds.MinPoint.X, y = bounds.MinPoint.Y, z = bounds.MinPoint.Z }, max = new { x = bounds.MaxPoint.X, y = bounds.MaxPoint.Y, z = bounds.MaxPoint.Z } }, geometry, EntityText(entity), entity is BlockReference reference ? reference.Name : null); }
    private static object GeometrySummary(Entity entity) => entity switch { Line line => new { start = PointDto(line.StartPoint), end = PointDto(line.EndPoint) }, Circle circle => new { center = PointDto(circle.Center), radius = circle.Radius }, Arc arc => new { center = PointDto(arc.Center), radius = arc.Radius, startAngle = arc.StartAngle, endAngle = arc.EndAngle }, Polyline polyline => new { vertices = polyline.NumberOfVertices, closed = polyline.Closed }, _ => new { type = entity.GetRXClass().DxfName } };
    private static object PointDto(Point3d point) => new { x = point.X, y = point.Y, z = point.Z, unit = "drawing" };
    private static Entity BuildGeometry(string kind, AutoCadRequest request, Database database)
    {
        if (kind == "line") { TryPoint(request, "start", database, out var start, out _); TryPoint(request, "end", database, out var end, out _); return new Line(start, end); }
        if (kind == "circle") { TryPoint(request, "center", database, out var center, out _); return new Circle(center, Vector3d.ZAxis, Length(request, "radius", database, 1)); }
        if (kind == "arc") { TryPoint(request, "center", database, out var center, out _); return new Arc(center, Length(request, "radius", database, 1), Angle(request, "startAngle") ?? 0, Angle(request, "endAngle") ?? Math.PI / 2); }
        if (kind == "rectangle") { var points = Points(request, "points", database); if (points.Count != 2) throw new AutoCadOperationException(BridgeErrorCodes.InvalidGeometry, "rectangle points requires two opposite corners."); var polyline = new Polyline(4); polyline.AddVertexAt(0, new Point2d(points[0].X, points[0].Y), 0, 0, 0); polyline.AddVertexAt(1, new Point2d(points[1].X, points[0].Y), 0, 0, 0); polyline.AddVertexAt(2, new Point2d(points[1].X, points[1].Y), 0, 0, 0); polyline.AddVertexAt(3, new Point2d(points[0].X, points[1].Y), 0, 0, 0); polyline.Closed = true; return polyline; }
        var vertices = Points(request, "points", database); if (vertices.Count < 2) throw new AutoCadOperationException(BridgeErrorCodes.InvalidGeometry, "points requires at least two coordinates."); var result = new Polyline(vertices.Count); for (var index = 0; index < vertices.Count; index++) result.AddVertexAt(index, new Point2d(vertices[index].X, vertices[index].Y), 0, NumberDouble(request, "constantWidth") ?? 0, NumberDouble(request, "constantWidth") ?? 0); result.Closed = Bool(request, "closed") ?? false; return result;
    }

    private static void AddAttributes(BlockReference reference, ObjectId definitionId, AutoCadRequest request, Transaction transaction) { var definition = (BlockTableRecord)transaction.GetObject(definitionId, OpenMode.ForRead); var attributes = Object(request, "attributes"); foreach (var id in definition) if (transaction.GetObject(id, OpenMode.ForRead) is AttributeDefinition attributeDefinition && !attributeDefinition.Constant) { var attribute = new AttributeReference(); attribute.SetAttributeFromBlock(attributeDefinition, reference.BlockTransform); if (attributes.TryGetValue(attributeDefinition.Tag, out var value)) attribute.TextString = value.ToString(); reference.AttributeCollection.AppendAttribute(attribute); transaction.AddNewlyCreatedDBObject(attribute, true); } }
    private static void ApplyLayerProperties(LayerTableRecord layer, AutoCadRequest request, Database database, Transaction transaction) { var rename = Text(request, "newName") ?? (Text(request, "layer") is not null || Text(request, "layerName") is not null ? Text(request, "name") : null); if (rename is { Length: > 0 }) layer.Name = rename; if (Number(request, "color") is { } color) { if (color < 0 || color > 256) throw new AutoCadOperationException(BridgeErrorCodes.InvalidParameterValue, "color must be an ACI value from 0 to 256."); layer.Color = Color.FromColorIndex(ColorMethod.ByAci, (short)color); } if (Text(request, "linetype") is { Length: > 0 } linetype) { var table = (LinetypeTable)transaction.GetObject(database.LinetypeTableId, OpenMode.ForRead); if (!table.Has(linetype)) throw new AutoCadOperationException(BridgeErrorCodes.InvalidParameterValue, $"Linetype '{linetype}' not found."); layer.LinetypeObjectId = table[linetype]; } if (Bool(request, "frozen") is { } frozen) layer.IsFrozen = frozen; if (Bool(request, "locked") is { } locked) layer.IsLocked = locked; if (Bool(request, "plottable") is { } plottable) layer.IsPlottable = plottable; }
    private static void ApplyEntityProperties(Entity entity, AutoCadRequest request, Database database, Transaction transaction) { if (Text(request, "layer") is { Length: > 0 } layer) { var layerId = ResolveLayerId(database, layer); if (layerId.IsNull) throw new AutoCadOperationException(BridgeErrorCodes.EntityNotFound, $"Layer '{layer}' not found."); entity.Layer = layer; } if (Number(request, "color") is { } color) { if (color < 0 || color > 256) throw new AutoCadOperationException(BridgeErrorCodes.InvalidParameterValue, "color must be an ACI value from 0 to 256."); entity.Color = Color.FromColorIndex(ColorMethod.ByAci, (short)color); } if (Text(request, "linetype") is { Length: > 0 } linetype) { var table = (LinetypeTable)transaction.GetObject(database.LinetypeTableId, OpenMode.ForRead); if (!table.Has(linetype)) throw new AutoCadOperationException(BridgeErrorCodes.InvalidParameterValue, $"Linetype '{linetype}' not found."); entity.LinetypeId = table[linetype]; } if (Text(request, "lineweight") is { Length: > 0 } lineweight) { if (!Enum.TryParse(lineweight, true, out LineWeight parsed)) throw new AutoCadOperationException(BridgeErrorCodes.InvalidParameterValue, "Invalid lineweight."); entity.LineWeight = parsed; } if (Number(request, "transparency") is { } transparency) { if (transparency < 0 || transparency > 255) throw new AutoCadOperationException(BridgeErrorCodes.InvalidParameterValue, "transparency must be from 0 to 255."); entity.Transparency = new Transparency((byte)transparency); } }
    private static void ApplyTextStyle(Entity entity, AutoCadRequest request, Database database, Transaction transaction) { var style = Text(request, "textStyle"); if (string.IsNullOrWhiteSpace(style)) return; var table = (TextStyleTable)transaction.GetObject(database.TextStyleTableId, OpenMode.ForRead); if (!table.Has(style)) throw new AutoCadOperationException(BridgeErrorCodes.InvalidParameterValue, $"Text style '{style}' not found."); var id = table[style]; if (entity is DBText text) text.TextStyleId = id; if (entity is MText mtext) mtext.TextStyleId = id; }
    private static ObjectId ResolveLayerId(Database database, AutoCadRequest request) => HandleId(database, Text(request, "handle")) ?? ResolveLayerId(database, Text(request, "layer") ?? Text(request, "name") ?? Text(request, "layerName"));
    private static ObjectId ResolveLayerId(Database database, string? name) { if (string.IsNullOrWhiteSpace(name)) return ObjectId.Null; using var transaction = database.TransactionManager.StartOpenCloseTransaction(); var table = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead); return table.Has(name) ? table[name] : ObjectId.Null; }
    private static ObjectId ResolveEntityId(Database database, AutoCadRequest request) => HandleId(database, Text(request, "handle") ?? Text(request, "entityId") ?? Text(request, "objectId")) ?? ObjectId.Null;
    private static List<ObjectId> ResolveEntityIds(Database database, AutoCadRequest request) => StringValues(request, "entityIds").Select(value => HandleId(database, value)).Where(id => id is { IsNull: false }).Cast<ObjectId>().ToList();
    private static ObjectId? HandleId(Database database, string? value) { if (string.IsNullOrWhiteSpace(value)) return null; if (!long.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var number)) return null; try { return database.GetObjectId(false, new Handle(number), 0); } catch { return null; } }
    private static ObjectId FindBlock(Database database, string? name) { if (string.IsNullOrWhiteSpace(name)) return ObjectId.Null; using var transaction = database.TransactionManager.StartOpenCloseTransaction(); var table = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead); return table.Has(name) ? table[name] : ObjectId.Null; }
    private static bool ValidateGeometry(AutoCadRequest request, string kind, Database database, out AutoCadOperationException? error) { error = null; if (kind == "circle") { if (!TryPoint(request, "center", database, out _, out var centerError)) { error = new AutoCadOperationException(BridgeErrorCodes.InvalidCoordinate, centerError ?? "center is required."); return false; } if (NumberDouble(request, "radius") is not { } radius || radius <= 0) { error = new AutoCadOperationException(BridgeErrorCodes.InvalidGeometry, "positive radius is required."); return false; } } if (kind == "arc" && (NumberDouble(request, "radius") is not { } arcRadius || arcRadius <= 0)) { error = new AutoCadOperationException(BridgeErrorCodes.InvalidGeometry, "positive radius is required."); return false; } if (kind == "line") { if (!TryPoint(request, "start", database, out _, out var startError)) { error = new AutoCadOperationException(BridgeErrorCodes.InvalidCoordinate, startError ?? "start is required."); return false; } if (!TryPoint(request, "end", database, out _, out var endError)) { error = new AutoCadOperationException(BridgeErrorCodes.InvalidCoordinate, endError ?? "end is required."); return false; } } if (kind is "polyline" or "rectangle" && Points(request, "points", database).Count < 2) { error = new AutoCadOperationException(BridgeErrorCodes.InvalidGeometry, "points are required."); return false; } return true; }
    private static AutoCadResponse FromTransaction<T>(AutoCadRequest request, AutoCadTransactionResult<T> result) { var response = result.Success ? AutoCadResponse.Ok(request, result.Value) : AutoCadResponse.Fail(request, result.ErrorCode, result.ErrorMessage); response.Warnings.AddRange(result.Warnings); response.CreatedHandles.AddRange(result.CreatedHandles); response.ModifiedHandles.AddRange(result.ModifiedHandles); response.DeletedHandles.AddRange(result.DeletedHandles); return response; }
    private static AutoCadResponse Plan(AutoCadRequest request, string effect) => AutoCadResponse.Ok(request, new { operation = request.Operation, dryRun = true, effects = new[] { effect } });
    private static bool IsWriteOperation(string operation) => operation.StartsWith("autocad.create_", StringComparison.OrdinalIgnoreCase) || operation.StartsWith("autocad.modify_", StringComparison.OrdinalIgnoreCase) || operation.StartsWith("autocad.move", StringComparison.OrdinalIgnoreCase) || operation.StartsWith("autocad.copy", StringComparison.OrdinalIgnoreCase) || operation.StartsWith("autocad.rotate", StringComparison.OrdinalIgnoreCase) || operation.StartsWith("autocad.scale", StringComparison.OrdinalIgnoreCase) || operation.StartsWith("autocad.erase", StringComparison.OrdinalIgnoreCase) || operation.StartsWith("autocad.change_", StringComparison.OrdinalIgnoreCase) || operation.StartsWith("autocad.set_", StringComparison.OrdinalIgnoreCase) || operation is "autocad.insert_block" or "autocad.save";
    private static string? Text(AutoCadRequest request, string key) => request.Parameters.TryGetValue(key, out var value) ? value.Trim().Trim('"') : null;
    private static bool? Bool(AutoCadRequest request, string key) { var value = Text(request, key); return bool.TryParse(value, out var parsed) ? parsed : null; }
    private static int? Number(AutoCadRequest request, string key) { var value = Text(request, key); return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null; }
    private static double? NumberDouble(AutoCadRequest request, string key) { var value = Text(request, key); return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && !double.IsNaN(parsed) && !double.IsInfinity(parsed) ? parsed : null; }
    private static double Length(AutoCadRequest request, string key, Database database, double fallback) { var raw = Raw(request, key); if (raw.ValueKind == JsonValueKind.Object && raw.TryGetProperty("value", out var value)) { var unit = raw.TryGetProperty("unit", out var unitValue) ? unitValue.GetString() ?? "mm" : "mm"; return _units.ToDrawingUnits(value.GetDouble(), unit, database); } var number = NumberDouble(request, key); return number is { } result ? _units.ToDrawingUnits(result, Text(request, "unit") ?? "mm", database) : fallback; }
    private static double? Angle(AutoCadRequest request, string key) { var raw = Raw(request, key); if (!raw.TryGetDouble(out var value)) return null; return (Text(request, "angleUnit") ?? "deg").StartsWith("deg", StringComparison.OrdinalIgnoreCase) ? value * Math.PI / 180d : value; }
    private static bool TryPoint(AutoCadRequest request, string key, Database database, out Point3d point, out string? error) { point = Point3d.Origin; error = null; var raw = Raw(request, key); if (raw.ValueKind != JsonValueKind.Object || !raw.TryGetProperty("x", out var x) || !raw.TryGetProperty("y", out var y) || !x.TryGetDouble(out var xv) || !y.TryGetDouble(out var yv)) { error = $"{key} requires finite x and y."; return false; } var z = raw.TryGetProperty("z", out var zValue) && zValue.TryGetDouble(out var zv) ? zv : 0; var unit = raw.TryGetProperty("unit", out var unitValue) ? unitValue.GetString() ?? "mm" : "mm"; try { point = _units.ToDrawingPoint(xv, yv, z, unit, database); return true; } catch { error = $"{key} has invalid unit '{unit}'."; return false; } }
    private static bool TryVector(AutoCadRequest request, string key, Database database, out Vector3d vector, out string? error) { vector = new Vector3d(0, 0, 0); if (!TryPoint(request, key, database, out var point, out error)) return false; vector = new Vector3d(point.X, point.Y, point.Z); return true; }
    private static bool TryRegion(AutoCadRequest request, Database database, out Point3d min, out Point3d max) { min = Point3d.Origin; max = Point3d.Origin; var raw = Raw(request, "boundingRegion"); if (raw.ValueKind != JsonValueKind.Object || !raw.TryGetProperty("min", out var minValue) || !raw.TryGetProperty("max", out var maxValue)) return false; var child = new AutoCadRequest(); child.Parameters["min"] = minValue.GetRawText(); child.Parameters["max"] = maxValue.GetRawText(); return TryPoint(child, "min", database, out min, out _) && TryPoint(child, "max", database, out max, out _); }
    private static List<Point3d> Points(AutoCadRequest request, string key, Database database) { var raw = Raw(request, key); return raw.ValueKind == JsonValueKind.Array ? raw.EnumerateArray().Select(value => { var child = new AutoCadRequest(); child.Parameters[key] = value.GetRawText(); return TryPoint(child, key, database, out var point, out _) ? point : Point3d.Origin; }).ToList() : new List<Point3d>(); }
    private static List<string> StringValues(AutoCadRequest request, string key) { var raw = Raw(request, key); return raw.ValueKind == JsonValueKind.Array ? raw.EnumerateArray().Select(value => value.ToString()).Where(value => value.Length > 0).ToList() : Text(request, key)?.Split(',').Select(value => value.Trim()).Where(value => value.Length > 0).ToList() ?? new List<string>(); }
    private static Dictionary<string, JsonElement> Object(AutoCadRequest request, string key) { var raw = Raw(request, key); return raw.ValueKind == JsonValueKind.Object ? raw.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase); }
    private static JsonElement Raw(AutoCadRequest request, string key) { if (!request.Parameters.TryGetValue(key, out var value)) return default; try { using var document = JsonDocument.Parse(value); return document.RootElement.Clone(); } catch { return JsonSerializer.SerializeToElement(value); } }
    private static int Clamp(int value, int minimum, int maximum) => Math.Min(maximum, Math.Max(minimum, value));
}

internal sealed class AutoCadOperationException : Exception
{
    public AutoCadOperationException(string code, string message) : base(message) => Code = code;
    public string Code { get; }
}
