# Security and privacy

## What AGEX sends, and to whom

- **AGEX itself** has no server, no account and no telemetry. It connects to the internet only to check GitHub for AGEX updates (if enabled; at most once a day), download updates you accept, and download skills you install.
- **Cloud agents** (Codex → OpenAI, Antigravity → Google, Claude Code → Anthropic, Gemini CLI → Google) receive your request and any project files they read, under their providers' terms. Before the first request in a project AGEX lists which services will receive data. **Local only** routing removes every cloud agent.
- **Ollama** with a local model keeps everything on your computer. Models named `…:cloud` run on Ollama's servers; AGEX labels them as cloud.
- **Skills** may use the network (for example the Fetch and Context7 skills). Their cards say so.

## Local data

Settings, projects, sessions and logs are plain JSON in your user folder (see [INSTALL.md](INSTALL.md#where-agex-keeps-data)). Logs and diagnostics pass through a redactor that removes API keys, tokens, bearer headers, passwords and private keys, and diagnostics replace your home folder with `~`. Tokens you give skills live in the OS secure store (DPAPI / Keychain / Secret Service), never in settings, logs, diagnostics or exports. On Linux without a Secret Service (`secret-tool`), AGEX falls back to a file readable only by your user account that is **not encrypted**; Diagnostics shows this.

## What agents may do

- AGEX starts only the five known agents, with fixed arguments, the prompt on stdin, and the project folder as working directory. There is no "run any command" feature.
- Before the first file change of a request AGEX asks you (unless the project is trusted). For Git projects it first saves a snapshot that **Undo changes** can restore.
- Codex runs in its `read-only` or `workspace-write` sandbox. Claude Code and Gemini CLI get their permission modes (see [AGENTS.md](AGENTS.md#what-each-adapter-restricts)). Antigravity has no read-only switch; AGEX tells it not to change files in read-only turns and never gives it file-changing work without your approval.
- AGEX independently checks every file an agent claims to have changed, and rejects claims outside the project folder.
- Agent output is treated as data: it is shown as plain text (no HTML or Markdown rendering, no clickable links executed), and only `http`/`https` links can be opened.

## Skills

- Catalog skills are pinned to a commit and each file's SHA-256 is checked on install; hosted MCP servers must use `https`; MCP commands may not contain shell characters; secrets travel only through environment variables.
- Custom skills are copied (never linked), size-limited, and zip packages are extracted with path, link, device-name and size checks. Community and local skills are labelled as not reviewed.
- Enabled skills are re-checked at every start; changed or missing files turn the skill off.

## Updates

The installer and the in-app updater install only a package whose SHA-256 matches the release's `SHA256SUMS.txt`. When the project publishes a release signing key, AGEX also requires a valid ECDSA signature on that file, which protects against a tampered release. Until then the checksum protects against damaged or swapped downloads but not against someone who can publish a release on the repository; the Diagnostics page says which mode is active.

## Build-time telemetry

AGEX's build scripts and CI set `DOTNET_CLI_TELEMETRY_OPTOUT=1` and `AVALONIA_TELEMETRY_OPTOUT=1`, so building AGEX does not send .NET or Avalonia build telemetry. If you build AGEX yourself, set both variables too.

## Security review (AGEX 2.0)

The findings of the review done for this release are in [reports/agex-product-maturity.md](../reports/agex-product-maturity.md#security-review). Report vulnerabilities as described in [../SECURITY.md](../SECURITY.md).
