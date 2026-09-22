# Environment Audit

Audit date: 2026-09-19
Host: Windows
Repository: `C:\Users\isc\OneDrive\Documents\ChatGPT\setup`

## Scope and safety

This phase is read-only. No software was installed. No existing Codex, Git, SSH, repository, MCP, or environment configuration was changed. No credentials or secret values were read into this report.

The setup repository had no tracked files and no commits at audit start. Working tree was clean.

## Installed and detected

| Component | Result | Evidence | Assessment |
|---|---|---|---|
| Codex desktop/CLI | Installed and running | `C:\Users\isc\AppData\Local\OpenAI\Codex\bin\...\codex.exe`; active Codex processes | Preserve. Normal `codex` path exists. CLI version probe was blocked by non-TTY `TERM=dumb`; Codex metadata reports latest checked version `0.155.1`, not a direct installed-version proof. |
| Codex configuration | Present | `C:\Users\isc\.codex\config.toml`, `AGENTS.md`, `auth.json`, state databases | Sensitive. Never copy credentials or state into this repository. Existing config enables elevated Windows sandbox and several bundled plugins. |
| Codex MCP | Present | `node_repl` server in `config.toml` | Existing MCP is Codex-owned runtime infrastructure. Do not replace. No user-added Serena, Context7, Caveman, or Graphiti server detected. |
| Codex plugins | Present | Bundled browser, Chrome, computer-use, Codex app tools, visualization; document/PDF/spreadsheet/presentation runtime plugins | Prefer these capabilities before adding duplicate MCP servers. |
| Caveman | Installed as agent skills | `C:\Users\isc\.agents\skills\caveman`; skill lock source `JuliusBrussee/caveman` | Keep. This is a skill installation, not a discoverable `caveman.exe` command. Installed skills include Caveman, compression, learning, review, setup, and related workflow skills. |
| Cavemem | Not verified | No `cavemem` command; installed skill documentation references Cavemem offload | Treat memory capability as unverified until a supported Caveman command or service is tested. Do not add Graphiti yet. |
| Git | Installed | `C:\Program Files\Git\cmd\git.exe` | Keep. Global Git identity exists. No global identity changes needed. |
| GitHub CLI | Missing | `gh` not found | Optional later. GitHub app/plugin may be preferable when available and explicitly enabled. |
| Node.js | Installed, shell selection broken | NVM shim path exists; shell reports “No active Node.js version is configured”; running processes use `v22.23.2` | Resolve in a later, additive phase before npm-based integrations. Do not install a second Node distribution. |
| npm / pnpm / npx | Shims present, unusable in current shell | NVM shim paths; same inactive-version error | Dependent on Node version-manager state. |
| Python | Launcher detected, runtime unverified | WindowsApps `python.exe`; no `py` launcher | Verify later. Do not assume usable Python installation. |
| uv | Installed | `C:\Users\isc\AppData\Local\hermes\bin\uv.exe` | Keep. Verify version through a clean shell later. |
| Docker | CLI installed | `C:\Program Files\Docker\Docker\resources\bin\docker.exe` | Docker config access returned `Access is denied`; daemon/Desktop health not proven. No need to modify now. |
| WSL | Present but inaccessible to audit shell | `wsl --status` returned `E_ACCESSDENIED` while enumerating distributions | Document as unknown. Do not create a second Linux toolchain. |
| Browsers | Chrome and Edge installed/running | Chrome and Edge processes detected | Existing Codex browser/Chrome plugins provide immediate browser-control capability. |
| Playwright | CLI not found | `playwright` and `npx playwright` unavailable because Node selection is broken | Evaluate after Node repair. Avoid installing a second browser-control stack before comparison. |
| Serena | Not found | No command detected | Evaluate in Phase 3. Installation not approved in Phase 0. |
| Context7 | Not found | No command detected | Evaluate in Phase 3. Prefer targeted documentation retrieval only if needed. |
| Graphiti | Not found | No command detected | Defer. Requires stronger proof that Cavemem is insufficient. |
| OmniRoute | Not found | No command detected | Defer until foundation is stable. Normal Codex must remain independent. |
| Editors | Present | VS Code, Cursor, Antigravity paths/processes detected | Avoid adding editor-specific agent integrations without a concrete need. |
| Ollama | PATH entry detected | `C:\Users\isc\AppData\Local\Programs\Ollama` appears in PATH | Runtime/version not verified. Outside current foundation scope. |
| ripgrep | Installed | WinGet path in PATH; used for repository inspection | Keep as primary search tool. |

## Existing Codex configuration

Observed safe metadata only:

- `C:\Users\isc\.codex\config.toml` exists.
- Model and reasoning settings are already configured.
- Elevated Windows sandbox is configured.
- Several local projects are marked trusted.
- Bundled marketplaces and plugins are enabled.
- Existing `node_repl` MCP uses Codex-managed runtime paths and a restricted environment allowlist.
- `C:\Users\isc\.codex\auth.json` exists and was not read.
- Codex state databases, logs, session history, and memory databases exist and were not copied or inspected for content.
- User-level Codex `AGENTS.md` exists and is empty at audit time.

Security implication: Codex configuration has high privilege and trusted-project scope. Any future setup script must back up targeted files, use additive edits, redact output, and preserve the plain `codex` command.

## Existing Caveman configuration and capability

- Skills are installed under `C:\Users\isc\.agents\skills`.
- `.skill-lock.json` records a GitHub source and installed hashes for Caveman-related skills.
- Caveman skill supports response compression modes and persists mode until stopped.
- `caveman-compress` supports reversible memory-file compression with out-of-tree backups.
- `caveman-learn` documents consent-gated token analysis and a `cavemem_offload` path.
- No standalone Caveman executable, MCP server, or Cavemem service was discoverable through PATH.

Conclusion: Caveman is part of intended stack as an agent-skill layer. Cavemem remains an unverified integration, not a proven persistent-memory service.

## Configuration and duplicate-function findings

- Setup repository is empty; no existing project files need preservation here.
- Codex already provides browser control, Chrome integration, computer use, JavaScript runtime, document tooling, and visualization. These overlap with several proposed MCPs.
- Node is installed through a version manager, but the active shell has no selected version. This blocks npm-based discovery and can create duplicate installations if handled carelessly.
- Docker CLI exists, but Docker daemon access is unproven.
- No Serena, Context7, Graphiti, OmniRoute, GitHub CLI, or Playwright CLI was detected.
- No user MCP configuration was found in VS Code; its `mcp.json` is empty.

## Security concerns

1. Codex sandbox mode is `elevated`; preserve this as an explicit risk boundary in documentation.
2. Codex trusts multiple local project paths; future scripts must not broaden trust automatically.
3. Docker CLI could not read its config due to access denial. Never weaken ACLs automatically.
4. Existing auth, session, state, and database files are sensitive. Exclude them from Git and backups unless encrypted and explicitly requested.
5. Third-party MCPs may receive filesystem, shell, browser, repository, or network access. Add one only after least-privilege review.
6. Git global identity is configured. Do not modify it as part of setup.
7. Remote installation commands need current official-source verification before execution.

## Missing prerequisites and blockers

- Active Node version selection.
- Reliable Python runtime verification.
- Docker daemon/Desktop health verification.
- WSL distribution/access verification.
- Safe, supported Cavemem test path.
- Official-source evaluation for Serena and Context7.
- GitHub integration choice: CLI, Codex plugin, or both only if non-overlapping.

## Recommended next phase

Proceed to Phase 2 only after approval:

1. Add repository documentation and reusable templates.
2. Add a read-only PowerShell doctor framework.
3. Add backup/restore helpers that target explicit files only.
4. Add `.gitignore` rules for credentials, caches, reports with sensitive data, and local machine state.
5. Repair or document NVM selection without installing another Node distribution.
6. Verify Codex plain-command continuity after each additive change.

Do not install Serena, Context7, Playwright, Graphiti, GitHub CLI, Docker components, or OmniRoute in Phase 2.

## Phase 2 diagnostic update

Additional read-only checks completed after Phase 2 approval:

- NVM for Windows reports version `2.0.1-hotfix.1`, shim mode, one installed version `22.23.2`, and no default version. `nvm use 22.23.2` was attempted because it is an existing-version selection, not an installation. The operation failed with registry write access denied. No Node installation, PATH entry, registry setting, or security permission changed.
- A fresh PowerShell process sees the same NVM shim and the same no-active-version error. Git remains usable. Normal `codex --help` exits successfully.
- Python resolves only to the WindowsApps alias. No real Python runtime was verified. `uv` is installed at version `0.12.10`; its cache probe reports access denied, so no cache permission was changed.
- Docker Desktop executable is installed. Docker CLI client version is `27.0.3`. Docker daemon connection fails because the `docker_engine` pipe is unavailable. Docker config access also reports access denied. No ACL or autostart setting changed.
- WSL version is `2.2.4.0`; distribution listing returns `E_ACCESSDENIED`. No distribution or security setting changed.
- Doctor script parses successfully and runs read-only. It reports Codex/Git/Caveman skills/uv/MCP as available; Node/NVM/npm/npx/pnpm, Python, Docker, WSL as warnings; Cavemem, Serena, Context7, Playwright, GitHub CLI, and OmniRoute as optional unavailable.
- Backup helper dry-run passed for allowlisted Codex config. No backup was created.

## Phase 3 update

- Official Serena documentation was reviewed. Serena Agent `1.7.0` was installed through existing uv with isolated CPython `3.13.15`.
- Exact Codex config backup created outside repository before integration.
- One additive global Serena MCP entry added. Existing `node_repl` MCP entry remained present.
- Setup project generated `.serena/project.yml` and was set `read_only: true`.
- Python disposable fixture indexed successfully. Setup PowerShell health check failed because PSScriptAnalyzer provisioning under `C:\Users\isc\.serena` returned access denied.
- Context7 official CLI/MCP/plugin paths reviewed. Context7 remains uninstalled and unconfigured because Node selection and credential/network decisions remain unresolved.
- No Graphiti, OmniRoute, Playwright, GitHub CLI, extra MCP, ACL, PATH, registry, or existing project change was made.
