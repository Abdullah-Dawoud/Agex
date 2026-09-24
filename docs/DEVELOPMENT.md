# Development

## Build and test

Requirements: the .NET 10 SDK. Nothing else.

```sh
export DOTNET_CLI_TELEMETRY_OPTOUT=1 AVALONIA_TELEMETRY_OPTOUT=1   # PowerShell: $env:... = '1'
dotnet build Agex.slnx
dotnet test tests/Agex.Tests
dotnet run --project src/Agex.Desktop          # the app
dotnet run --project src/Agex.Cli -- help      # the agex command
```

Use `AGEX_HOME=<some folder>` to run with isolated data. The tests always do this and use `tests/Agex.FakeAgent`, a program that speaks the Codex, Antigravity, Claude Code and Gemini CLI protocols (plus a small fake Ollama HTTP server in the tests), so no account or real agent is needed.

`AGEX_ONLINE_TESTS=1 dotnet test tests/Agex.Tests --filter Online` downloads every curated instruction skill from GitHub and checks every file hash.

## Release packages

```sh
pwsh tools/build-release.ps1                                   # this OS
pwsh tools/build-release.ps1 -Runtime win-x64,osx-arm64,linux-x64
```

Output in `dist/`: per-platform packages, the two installers, `skills-catalog.json` and `SHA256SUMS.txt`. Packages are self-contained (no .NET needed on the user's machine).

Cross-building works from any OS, with one important limit: **macOS packages built on Windows or Linux are unsigned, and Apple Silicon Macs refuse to run unsigned code.** Official macOS packages must be built on a Mac (the release workflow does this), where `tools/sign-macos.sh` signs them — ad-hoc at minimum.

### Releases

Pushing a tag `vX.Y.Z` that matches `VERSION` runs `.github/workflows/release.yml`: Windows, macOS and Linux runners each test and build their packages (Windows x64/ARM64, macOS arm64/x64 zip + DMG, Linux x64/ARM64), then one job writes `SHA256SUMS.txt`, signs it if a key is configured, and publishes the GitHub release. `.github/workflows/ci.yml` builds and tests on all three OSes for every push.

### Signing

Nothing is ever faked: without these secrets the release is built unsigned and says so.

| Secret | Purpose | How to get it |
| --- | --- | --- |
| `WINDOWS_CERT_PFX_BASE64`, `WINDOWS_CERT_PASSWORD` | Authenticode signing of `AgexDesktop.exe` and `agex.exe` (`tools/sign-windows.ps1`) | A code-signing certificate from a CA (OV/EV), exported as PFX, base64-encoded. |
| `MACOS_CERT_P12_BASE64`, `MACOS_CERT_PASSWORD`, `MACOS_SIGN_IDENTITY` | Developer ID signing with hardened runtime (`tools/sign-macos.sh`, entitlements in `tools/macos-entitlements.plist`) | Apple Developer Program membership; "Developer ID Application" certificate exported as .p12; identity string such as `Developer ID Application: Name (TEAMID)`. |
| `APPLE_ID`, `APPLE_TEAM_ID`, `APPLE_APP_PASSWORD` | Notarization and stapling of the DMG | Apple ID with an app-specific password. |
| `AGEX_RELEASE_SIGNING_KEY` | ECDSA P-256 signature of `SHA256SUMS.txt` (`SHA256SUMS.txt.sig`) | `dotnet run tools/sign-release.cs -- new-key private.pem`; store the private key as the secret, delete the file, and paste the printed public key into `UpdateService.ReleasePublicKeyPem`. From then on AGEX refuses unsigned updates. |

## Skill catalog

`src/Agex.Core/Skills/skills-catalog.json` is embedded in AGEX. Regenerate it with `tools/update-skill-catalog.ps1` (clones the sources, pins commits, hashes files) and review the diff. The online test verifies every hash against GitHub. Criteria for what belongs in the catalog: [reports/skills-research.md](../reports/skills-research.md).

## Adding an agent adapter

1. Check the agent has a documented non-interactive mode that takes the prompt on stdin or through an API and returns a final result reliably. No stable interface, no adapter.
2. Derive from `CliAgentAdapter` (command-line agents) or implement `IAgentAdapter` (APIs; see `OllamaAdapter`):
   - identity, provider, description, `Capabilities`, `CanWriteFiles`, `SupportedPlatforms`, `MaxConcurrentRuns`, `PrivacyFor(model)`, `DataDestination(model)`;
   - `CommandNames` (and `KnownToolLocations` entries in the platform services);
   - `RunAsync`: build a fixed argument list, pass the prompt through `ProcessRunner` on stdin, map read-only / write / no-commands to the agent's own switches, turn its structured events into `AgentActivity` (tool steps only — never reasoning), return the final text and any usage it reports, and set `FallbackEligible` only when the agent failed before doing meaningful work.
3. Register it in `AgentRegistry.CreateDefault` and add a default to `AgexSettings.AgentOptions`.
4. Add its protocol to `tests/Agex.FakeAgent` and an engine test (the Unicode test runs every protocol).
5. Mark it `Beta` until it has run against the real agent; document it in [AGENTS.md](AGENTS.md).

## Creating a skill

A skill for AGEX is a standard Agent Skills folder:

```text
my-skill/
  SKILL.md          front matter (name, description) + instructions
  references/...    optional
  scripts/...       optional; agents may run them, AGEX never does
```

Share it as a GitHub folder link or a `.zip`. To propose it for the curated catalog, open an issue with: what it does, licence (open source), maintenance, permissions it needs, which agents it works with, and evidence it is useful. Catalog entries use this schema (see the existing file for complete examples):

```json
{
  "id": "my-skill", "name": "My Skill", "kind": "instructions", "description": "...",
  "author": "...", "version": "abc1234", "license": "MIT", "homepage": "https://...",
  "categories": ["Developer"], "recommended": false, "trust": "curated",
  "permissions": ["read_files"], "supported_agents": ["codex", "claude-code", "antigravity", "gemini-cli"],
  "supported_platforms": ["windows", "macos", "linux"], "required_tools": [],
  "min_agex_version": "2.0.0", "tested_agex_version": "",
  "source": { "repository": "owner/repo", "commit": "<40-char sha>", "base_path": "skills/my-skill",
              "files": [{ "path": "SKILL.md", "sha256": "<64 hex>", "size": 1234 }], "extra_files": [] }
}
```

MCP skills use `"kind": "mcp"` and an `"mcp"` object: `transport` (`stdio` or `http`), `command` + `args` with a pinned version, or `url` (https) + `bearer_secret`, and `secret_env` (names only).

Compatibility fields are enforced: an entry whose `min_agex_version` is newer than the running AGEX, or whose `supported_platforms` excludes this OS, cannot be installed and says why; `tested_agex_version` produces a warning.

## Settings migrations

`AgexSettings.CurrentSchema` is the settings version. When a field changes meaning, increase it and add one step in `Settings/Migrations.cs` (`if (schema < N) { … }`). Never edit an old step. Load backs up the file before migrating. A file written by a newer AGEX is read as far as possible and not rewritten until the user changes a setting (Diagnostics shows a notice).

## Code guidelines

- Core stays platform-independent: put OS differences behind `IPlatformService`.
- Start programs only through `ProcessRunner`; never through a shell.
- Never show or store model reasoning; never invent agent messages.
- User-visible text is plain language; technical detail belongs in Diagnostics.
