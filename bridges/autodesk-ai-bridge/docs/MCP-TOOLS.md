# MCP tools

Transport: MCP JSON-RPC 2.0 over stdio. `initialize`, `tools/list`, `tools/call`, and `ping` are implemented. Tool results include `success`, structured data, warnings, changes, and structured errors.

Discovery tools:

- `autodesk.list_instances`
- `autodesk.get_active_instance`
- `autodesk.select_instance`
- `autodesk.list_documents`
- `autodesk.get_active_document`
- `autodesk.capabilities`
- `autodesk.health`
- `revit.health`, `revit.get_document_info`
- `autocad.health`, `autocad.get_document_info`

The host registers core Revit and AutoCAD operation names with JSON schemas, risk metadata, target product, implementation status, and document requirements. `tools/list` includes `implementationStatus`; connected capability reports use `SUPPORTED` or `SOURCE_IMPLEMENTED_RUNTIME_UNVERIFIED`. Unknown names fail with `UNKNOWN_TOOL`. Non-MVP names are not advertised. The host never calls Autodesk APIs.

Every edit is tagged with risk category. Safe mode permits only read tools. File writes and destructive operations are disabled by default in `PermissionOptions`.

Target selection:

```json
{
  "name": "revit.get_document_info",
  "arguments": {"instanceId": "revit-instance-id", "documentId": "document-id"}
}
```

When multiple instances match and no `instanceId` is supplied, result code is `AMBIGUOUS_TARGET`. The bridge never guesses.
