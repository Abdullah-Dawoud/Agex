# Agents

An **agent adapter** connects AGEX to one AI agent through that agent's documented non-interactive interface. AGEX never runs arbitrary commands: each adapter builds a fixed command line, sends the prompt on stdin (never as an argument), and reads the agent's structured output.

## Supported agents

| Agent | Adapter | Interface used | Can | Stability | Where data goes |
| --- | --- | --- | --- | --- | --- |
| Codex CLI (OpenAI) | `codex` | `codex exec --json -o <file> -` | read, edit (only when allowed: `workspace-write` sandbox), commands, review, tests, planning, MCP, skills | Stable | OpenAI cloud |
| Antigravity CLI (Google) | `antigravity` | `agy --input-format stream-json --output-format stream-json --sandbox` | read, edit, commands, web, browser, review, tests, planning, images, documents, skills | Stable | Google cloud |
| Claude Code (Anthropic) | `claude-code` | `claude -p --output-format stream-json` | read, edit (`acceptEdits`), commands, web, review, tests, planning, MCP, skills | Beta | Anthropic cloud |
| Gemini CLI (Google) | `gemini-cli` | `gemini --output-format json --approval-mode …` | read, edit, commands, web, review, planning, documents | Beta | Google cloud |
| Ollama | `ollama` | `http://127.0.0.1:11434/api/chat` (or `OLLAMA_HOST`) | text answers, planning, review of provided text | Beta | This computer; models named `…:cloud` / `…-cloud` run on Ollama's servers and are shown as cloud |

"Stable" adapters were run against the real agents during AGEX's own testing (see [reports/agex-product-maturity.md](../reports/agex-product-maturity.md)). "Beta" adapters follow the tools' documented flags and pass AGEX's protocol tests with simulated agents, but were not exercised with a real signed-in account here (Ollama was exercised with a real local model).

AGEX can install Codex, Claude Code and Gemini CLI from their official npm packages after you confirm, shows the official instructions for Antigravity and Ollama, and opens each agent's own sign-in. It never sees passwords, never installs during the first-run scan, and never changes an agent's own configuration. Sources, sign-in checks and model discovery per agent: [AGENT_INSTALLATION.md](AGENT_INSTALLATION.md).

### What each adapter restricts

| Setting | Codex | Antigravity | Claude Code | Gemini CLI |
| --- | --- | --- | --- | --- |
| May not change files | `--sandbox read-only` | prompt instruction only (no read-only switch exists) | `--disallowedTools Edit MultiEdit Write NotebookEdit` | `--approval-mode default` |
| May change files | `--sandbox workspace-write` | always | `--permission-mode acceptEdits` | `auto_edit` (or `yolo` with commands) |
| No shell commands | sandboxed by Codex | `--sandbox` restricts the terminal | `--disallowedTools Bash` | tools needing approval are unavailable |

Because Antigravity has no read-only mode, AGEX never assigns it a task that must not change files when a read-only agent is available, and tasks that change files only run after your approval.

### Usage and cost

AGEX shows token counts and cost only as the agent reports them: Codex (tokens per turn), Antigravity (tokens per step), Claude Code (tokens and `total_cost_usd`), Gemini CLI (tokens per model), Ollama (prompt and output tokens, cost 0 for local models). Anything not reported is shown as "not reported"; AGEX never estimates.

## Discovery

AGEX looks for tools in this order: an explicit override (`AGEX_<ID>_PATH`, for example `AGEX_CODEX_PATH`), the commands on your PATH (on macOS/Linux also the PATH of your login shell, because apps started from the Dock or a menu get a shorter one), then known install folders. It never crawls the disk. Supported adapters get one bounded health check (`--version`, or `/api/version` for Ollama) and a quota-free sign-in check (`codex login status`, local markers for Claude Code and Gemini CLI; `agy models` only on request). Model lists come from the agents themselves (`codex debug models`, `agy models`, Ollama `/api/tags`) and are cached for 12 hours. No prompt is ever sent to a model for these checks. Versions of other tools come from file metadata or `package.json`.

The Agents page combines both checks into one state: Ready, Sign-in required, Not working, Not installed, Not available on this system, or Detected - not integrated, with only the actions that apply (Install, Sign in, Check sign-in, Test connection, Refresh models, Retry detection).

Statuses:

| Status | Meaning |
| --- | --- |
| Ready (`SUPPORTED`) | Adapter available, installed, health check passed. |
| Available (`AVAILABLE`) | Installed, not checked yet. |
| Not installed (`NOT_INSTALLED`) | Adapter available, tool not found. |
| Detected — not integrated (`DETECTED_UNSUPPORTED`) | Tool found; AGEX has no adapter for it. |
| Not available on this system (`PLATFORM_UNSUPPORTED`) | The tool does not exist for this OS (for example Visual Studio on macOS). |
| Sign-in required (`AUTH_REQUIRED`) | Installed, but the agent reported it is not signed in. |
| Not working (`BROKEN`) | Installed, health check failed (the reason is shown). |
| Unknown | Not scanned yet. |

Detected, not integrated today: OpenCode, GitHub Copilot CLI, Cursor Agent CLI, Aider, Qwen Code; editors VS Code, Cursor, Windsurf, Antigravity IDE, Visual Studio (Windows only), JetBrains IDEs, Zed. Editors are never treated as agents; Projects offers "Open in <editor>" for VS Code, Cursor, Windsurf and Zed. Tools: Git, Node.js (npx), uv (uvx), Docker. MCP servers configured for Codex, Claude Desktop, VS Code, Cursor or Gemini CLI are listed by name only; their files are read, never changed.

## Health and fallback

If an agent fails to start or is not signed in, AGEX stops using it for 5 minutes (two ordinary failures in a row do the same). Before a task starts, AGEX checks the assigned agent; if it is unavailable, the task moves once to another enabled agent that can do it (an agent that can edit files, if the task changes files). A call that fails before doing meaningful work is retried once on another agent. Every switch is shown in the timeline and the result says whether it recovered.

## Leader

The leader plans and reviews. **Automatic** picks the first enabled agent that can plan, preferring agents that can read files. You can fix the leader in Agents → How work is shared. If every agent in the team is text-only (Ollama), AGEX skips planning and asks for a direct answer, because small local models do not follow the planning protocol reliably; the result says it was not checked against the project.

## Adding an adapter

See [DEVELOPMENT.md](DEVELOPMENT.md#adding-an-agent-adapter). Add an adapter only for an agent with a documented, non-interactive interface that takes the prompt on stdin or through an API and returns a final result reliably.
