# Agents

## Supported

| Agent | Adapter | Can | Notes |
| --- | --- | --- | --- |
| Codex CLI (OpenAI) | `codex` | read files, write files (only when allowed), run commands, code review, testing, planning, debugging, MCP | Runs `codex exec` non-interactively. Read-only sandbox by default. |
| Antigravity CLI (Google) | `antigravity` | read and write files, run commands, web research, browser, code review, testing, planning, debugging, images, documents | Runs `agy` in stream-json mode through a supervised worker; up to 2 in parallel. |

Both are cloud services: requests you send through AGEX go to their providers under their terms. AGEX does not install, update or sign in to agents.

## Discovery

AGEX finds tools with an allowlist of known locations and command names (PATH, `%LOCALAPPDATA%\Programs`, npm global folders, known install folders). It never crawls the disk and never runs a program that is not a supported adapter. Supported adapters get one bounded health check (`--version`). Everything else is file presence only; versions come from file metadata or the tool's `package.json`.

Each result has one status:

- **Supported**: AGEX has an adapter; it can be enabled.
- **Detected, not integrated**: installed, but AGEX has no adapter. Shown for information only.
- **Unavailable**: not found (or not ready).

Detected today when present: Claude Code CLI, Gemini CLI, GitHub Copilot CLI, Cursor Agent CLI, Aider, OpenCode, Qwen Code, Ollama; IDEs: Visual Studio Code, Cursor, Windsurf, Antigravity IDE, Visual Studio, JetBrains IDEs, Zed. IDEs are never treated as agents.

Integrations shown: Git (diffs of changed files) and MCP servers configured for Codex, Claude Desktop, VS Code or Cursor (names only; configurations are read, never changed).

## Capabilities and routing

Capabilities: `READ_FILES`, `WRITE_FILES`, `RUN_COMMANDS`, `WEB_RESEARCH`, `BROWSER`, `CODE_REVIEW`, `TESTING`, `PLANNING`, `DEBUGGING`, `MCP`, `IMAGE`, `DOCUMENTS`.

The leader plans tasks and names an agent for each. Before a task starts, AGEX checks that the agent is enabled, healthy and able to do it. A task that declares files to change is never sent to an agent that cannot write (Codex in read-only mode); if no capable agent is available, the task fails with that reason instead of running uselessly. The leader is told which agents are available, so it plans accordingly. Workload percentages are preference weights, not quotas.

Adding a new agent: see [DEVELOPMENT.md](DEVELOPMENT.md#adding-an-agent-adapter).
