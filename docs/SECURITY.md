# Security and privacy

## What AGEX sends, and to whom

- **AGEX itself** has no server, no account and no telemetry. It connects to the internet only to check GitHub for AGEX updates (if enabled; at most once a day), download updates you accept, and download skills you install.
- **Cloud agents** (Codex → OpenAI, Antigravity → Google, Claude Code → Anthropic, Gemini CLI → Google) receive your request and any project files they read, under their providers' terms. Before the first request in a project AGEX lists which services will receive data. **Local only** routing removes every cloud agent.
- **Ollama** with a local model keeps everything on your computer. Models named `…:cloud` run on Ollama's servers; AGEX labels them as cloud.
- **Skills** may use the network (for example the Fetch and Context7 skills). Their cards say so.
- **Attachments** go only to the agents of that request, after a confirmation that names each destination. AGEX copies them into its own data folder, extracts Office text and video frames on this computer, and refuses programs and unknown binary files.
- **Existing MCP connections** in other tools are read only to list their names, commands and setting names. Setting values (tokens, keys) are read only when you import a server and tick "Copy its settings"; they then go to the system key store. AGEX never changes those files.
- **Public MCP Registry** servers are not reviewed. AGEX labels them, shows what will run, accepts only npm and PyPI packages and https servers (no shell characters), and starts them with "ask each time".
- **Local web server**: when a request needs a page on this computer (or you preview a project HTML file), AGEX serves the project folder at `http://127.0.0.1:<port>`. It listens on the loopback address only, answers GET and HEAD, serves only files inside the project, never serves hidden files or folders (`.env`, `.git`), never lists folders, and stops with AGEX. Requests are classified by target: your own computer (localhost, 127.0.0.1) is allowed with the Browser permission; other private-network addresses (10.x, 192.168.x, 169.254.x, single-label names) are treated as external network, not as your own computer.
- **Account usage** for Codex is read with `codex app-server` (`account/rateLimits/read`), which uses Codex's stored sign-in and no model quota. AGEX keeps only the plan and usage percentages; account identifiers and e-mail in the reply are not read or stored.
- **Web preview** loads only local files from the previewed folder and servers on this computer; other navigation is blocked. Its cache lives in AGEX's cache folder.
- **Live screen** (Computer tab) takes screenshots of the main screen only while a request runs and the tab is shown; they stay in memory and are never written to disk or sent anywhere. It can be switched off.
- **Providers** (Routing & Providers) receive Codex's requests only when you select one for Codex. Their API keys are kept in the system key store and passed as an environment variable, never on a command line or in settings.
- **Teams** that act outside the computer (Computer Operator, Job Search & Applications, Marketing & Growth, DevOps & Release) tell agents to stop and ask before submitting, sending, paying, uploading, deleting or changing an account; AGEX's own approvals still apply. The Revit/AutoCAD connection is only offered when the Autodesk AI Bridge is installed, and every use asks first.

## Local data

Settings, projects, sessions and logs are plain JSON in your user folder (see [INSTALL.md](INSTALL.md#where-agex-keeps-data)). Logs and diagnostics pass through a redactor that removes API keys, tokens, bearer headers, passwords and private keys, and diagnostics replace your home folder with `~`. Tokens you give skills live in the OS secure store (DPAPI / Keychain / Secret Service), never in settings, logs, diagnostics or exports. On Linux without a Secret Service (`secret-tool`), AGEX falls back to a file readable only by your user account that is **not encrypted**; Diagnostics shows this.

## What agents may do

- AGEX starts only the five known agents, with fixed arguments, the prompt on stdin, and the project folder as working directory. There is no "run any command" feature.
- AGEX asks according to the approval mode (Ask every time, Smart approvals, Trust this session). Payments, sending messages, deleting significant data, account and security changes, publishing and changing secrets always ask, and every prompt tells agents to ask the user before them. For Git projects AGEX saves a snapshot before changes that **Undo changes** can restore. "Trust this session" ends when AGEX restarts.
- Permissions that are off are never given to agents: no browser or computer tools without Browser or Computer control, no MCP servers without MCP tools, no writes without Write project files, no commands where the agent supports turning them off.
- `codex exec` cannot ask questions, so MCP tool calls that need approval would be refused. For the MCP servers AGEX itself passes (after its own approvals and each skill's permissions), AGEX sets `default_tools_approval_mode = "approve"`. Servers from your own Codex configuration are not changed.
- Codex gets `sandbox_workspace_write.network_access` only in writing tasks of requests that need the internet or a page on this computer. Antigravity runs with `--sandbox` unless you allowed commands and network for such a request.
- Codex runs in its `read-only` or `workspace-write` sandbox. Claude Code and Gemini CLI get their permission modes (see [AGENTS.md](AGENTS.md#what-each-adapter-restricts)). Antigravity has no read-only switch; AGEX tells it not to change files in read-only turns and never gives it file-changing work without your approval.
- AGEX independently checks every file an agent claims to have changed, and rejects claims outside the project folder.
- Agent output is treated as data: it is shown as text with basic formatting (headings, lists, bold, code), never as HTML; links are shown as their text and are not opened from answers, and only `http`/`https` links can be opened anywhere in AGEX.

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
