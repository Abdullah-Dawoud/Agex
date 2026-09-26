# Development and release building

This document is for contributors and trusted release builders. It does not describe normal user installation.

## Required build environment

- Windows release builder.
- .NET SDK matching project targets.
- Matching Autodesk Revit API assemblies obtained through Autodesk-authorized development channels.
- Matching AutoCAD managed API assemblies obtained through Autodesk-authorized development channels.
- Inno Setup `iscc.exe` already installed on release builder.
- Autodesk products and test projects for runtime acceptance tests.

Do not install these on an end user's machine. Do not commit Autodesk-owned API assemblies or redistribute them unless licensing permits it.

## Shared and Autodesk builds

Shared Host and Manager components publish self-contained for `win-x64`:

```powershell
dotnet publish src/AutodeskAIBridge.Host/AutodeskAIBridge.Host.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish src/AutodeskAIBridge.Diagnostics/AutodeskAIBridge.Diagnostics.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

`win-arm64` is not a release target yet. Autodesk Revit and AutoCAD plug-in compatibility and runtime acceptance evidence are currently x64-only. Add an ARM64 artifact only after Autodesk support and the full acceptance checklist are verified.

Build each Revit version against its own installed API directory. Build AutoCAD against its installed managed API directory. Keep Autodesk references private so they do not enter release package.

## Release build

Run `package.ps1 -Version 1.0.0` on prepared release builder. Script refuses to install tools and fails when required inputs are missing. It creates installer source package, publishes Host and Manager self-contained, builds installer, and leaves artifacts under `artifacts/`.

Use trusted/self-hosted release workflow for real release. Generic GitHub-hosted runners cannot compile or test Autodesk plug-ins without legally supplied references and installed products.

## Validation

Run `validate.ps1` when available, mock test harness, and [Release acceptance tests](RELEASE_ACCEPTANCE_TESTS.md). Never call release production-ready from source checks alone.
