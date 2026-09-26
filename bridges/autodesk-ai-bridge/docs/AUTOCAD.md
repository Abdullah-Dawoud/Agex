# AutoCAD integration

`AutoCadBridgePlugin` implements `IExtensionApplication` and loads through `AutodeskAIBridge.bundle/PackageContents.xml`. The plugin uses official Managed .NET API references resolved from `AutoCADManagedApiPath`; Autodesk binaries are never copied into source or installer templates.

Source-implemented core operations include document/database inspection, units, layers, filtered entity queries, line/polyline/circle/arc/rectangle geometry, text, blocks, transforms, properties, aligned dimensions, regeneration, and save.

Writes acquire `DocumentLock` and use a database transaction. Requests marshal through `DocumentCollection.ExecuteInCommandContextAsync`.

Runtime execution and AutoCAD-version validation remain pending. AutoCAD 2027 managed assemblies are present locally but no usable build toolchain is installed. Non-MVP operations are not advertised.
