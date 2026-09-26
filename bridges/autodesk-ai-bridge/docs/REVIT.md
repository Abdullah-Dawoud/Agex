# Revit integration

`RevitBridgeApplication` implements `IExternalApplication`. Startup creates `ExternalEvent` and `RevitRequestQueue`, then adds optional Reconnect and Diagnostics ribbon commands.

Source-implemented core operations include inspection, filtered element queries, parameters, levels, walls, floors, family types and instances, transforms, rooms, views, sheets, text notes, save, and atomic/non-atomic batches.

Request payloads use explicit `elevationMm`, `elementId`, `uniqueId`, `name`, and `dryRun` fields. Level elevation converts through `UnitUtils`; no scattered conversion constants exist.

Dispatcher allow-lists operations. It never exposes reflection, arbitrary code, or arbitrary Revit API calls. Extend tool coverage through typed handlers and transaction tests.

Runtime execution and version-specific API verification remain pending on a machine with matching Revit API assemblies and Revit itself. Non-MVP operations are not advertised.
