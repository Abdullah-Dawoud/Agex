# Autodesk AI Bridge: source flow

This document records the source path for two core MVP operations. Runtime validation remains pending because the current machine has no Revit API assemblies and no usable build toolchain.

## Flow A: `revit.create_wall`

1. MCP receives `tools/call` with `name=revit.create_wall`.
2. `McpStdioServer` converts JSON into plain values and creates `ToolCallContext`, including target and `dryRun`.
3. `ToolDispatcher` resolves the registered descriptor, applies permission policy, validates required fields and coordinate object shape, then invokes the forwarded tool.
4. `BuiltInTools.ResolveTarget` selects the explicit instance, selected instance, or the only connected Revit instance. Multiple matches return `AMBIGUOUS_TARGET`.
5. `IpcAutodeskAdapter` creates a typed `BridgeRequest` and serializes parameters through `ProtocolJson`.
6. `IpcServerConnection` authenticates and correlates the request over the named pipe.
7. `RevitPluginSession` converts protocol values into `RevitRequest` and enqueues the request into `RevitRequestQueue`.
8. `RevitExternalEventHandler` drains the queue on Revit's API thread and calls `RevitCommandDispatcher`.
9. `RevitCommandDispatcher` validates coordinates, resolves level and wall type, converts external units through `IRevitVersionAdapter`, and checks dry-run and pinned/read-only conditions.
10. `RevitTransactionRunner` starts a named transaction, installs the failure preprocessor, calls `Wall.Create`, tracks the created `ElementId`, commits, or rolls back on any exception.
11. `RevitResponse` carries the wall DTO, warnings, and created IDs back through `BridgeResponse` and `ChangeSetDto`.
12. Host returns structured MCP content with success or concise mapped error.

## Flow B: `autocad.create_polyline`

1. MCP validates the operation schema. `points` is an array with at least two coordinate objects. Each point has `x`, `y`, and optional `z` and `unit`; default unit is `mm`.
2. Host resolves the target AutoCAD instance and forwards a typed `BridgeRequest` over authenticated IPC.
3. `AutoCadPluginSession` maps protocol parameters into `AutoCadRequest` and calls `AutoCadDispatcher`.
4. `AutoCadDispatcher` enters `DocumentCollection.ExecuteInCommandContextAsync`, resolves the requested or active document, and rejects missing documents.
5. Dispatcher validates points, converts units through `IAutoCadVersionAdapter`, and returns a validation plan without mutation when `dryRun=true`.
6. `AutoCadTransactionRunner` acquires `DocumentLock`, starts a database transaction, opens current space for write, constructs `Polyline`, applies layer and common properties, appends the entity, and tracks its handle.
7. Transaction commits on success. Any exception disposes the lock and transaction and reports rollback with a stable error code.
8. Response returns compact entity DTO plus created handle. No raw AutoCAD database object crosses IPC.

## Capability truth

Host advertises only the core operations with source implementations. Product plugin capabilities mark source implementation as runtime-unverified until execution against matching Autodesk versions. Non-MVP catalog entries are not advertised by the host.

## Installed user flow

1. User runs `AutodeskAIBridge-Setup.exe`.
2. Installer detects supported products and writes only matching owned files.
3. Manager creates per-user IPC settings and discovers AntiGravity MCP settings.
4. Manager backs up, merges, validates, and atomically writes the Bridge entry.
5. AntiGravity starts self-contained Host through stdio.
6. Host reads shared per-user IPC settings and accepts authenticated Revit or AutoCAD plug-in sessions.
7. User continues using AntiGravity chat. Bridge Manager is only for status and repair.
