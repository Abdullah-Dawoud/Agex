# Current limitations

Source implementation is complete for the shared bridge path, mocks, contracts, lifecycle logic, and static validation. This machine cannot compile or run Autodesk integrations.

- Revit and AutoCAD API calls require matching proprietary assemblies and valid product context.
- Non-MVP Revit and AutoCAD operations are not advertised. Manually crafted non-MVP requests return `UNSUPPORTED_OPERATION`.
- Core product source is implemented but runtime validation is pending on machines with matching Autodesk applications and assemblies.
- Revit/AutoCAD runtime integration, product-side IPC, and destructive-document tests remain unverified.
- Diagnostics detects product/API presence but does not prove every assembly is loadable.
- Installer compilation requires preinstalled Inno Setup `iscc.exe`.
- Installer and Bridge Manager configure AntiGravity automatically when a supported settings location is found. Live AntiGravity and rollback behavior still require release acceptance testing.
