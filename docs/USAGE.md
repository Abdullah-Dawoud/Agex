# Using AGEX

## Desktop app

Open AGEX from the Start Menu or run `agex`. The window has three parts:

- **Sidebar**: Home, Projects, Agents, Sessions, Agent Room, Settings, Diagnostics.
- **Main area**: the page you chose.
- **Right panel**: each agent's live state (Ready, Running, Waiting, Done, Failed, Unavailable, Off) and the current request. It hides on narrow windows.

### Home

1. Check the project at the top (Change project to switch).
2. Type what you want done. Press **Send** or **Ctrl+Enter**.
3. Watch "Now:" and the progress bar. Nothing blocks: you can open other pages while agents work.
4. When the request ends, the **Result** tab shows the status, the answer or summary, what AGEX did (for example an automatic fallback) and statistics.

Tabs: **Result**, **Tasks** (every task, its agent, status and any error), **Changes** (added, modified and deleted files with `+/-` line counts; select a file to see its diff; Open file / Open folder), **Activity** (what AGEX did, step by step; "Show technical log" adds executor details).

Buttons: **Cancel** stops the request and the agent processes it started. **Retry** runs the last request again (and clears a temporary agent pause).

### Agent Room

The live collaboration feed: task assignments from the leader, results the agents returned, questions, answers, reviews and handoff messages the agents chose to send, and AGEX system events (fallbacks, failures). Filter by agent or task, follow live, expand long messages, copy or open any message. **Graph** shows User, AGEX and each agent with their state and the message flows between them.

AGEX shows only content the agents actually produced or AGEX actually did. It never shows hidden model reasoning and never invents messages.

### Agents

Turn agents on or off, test a connection, choose the leader (Auto, Codex, Antigravity) and a workload strategy:

| Strategy | Preference |
| --- | --- |
| Balanced | Codex 50% / Antigravity 50% |
| Coding-heavy | Antigravity 80% (implementation) |
| Research-heavy | Codex 70% (analysis and review) |
| Custom | your own split |

The strategy is a preference. AGEX still routes by agent health, whether the agent is turned on, and what it can do: a task that edits files goes only to an agent allowed to write. Advanced: models, reasoning effort, and whether Codex may edit project files (off by default: Codex is read-only and edits go to Antigravity).

The page also lists tools AGEX detected but does not integrate yet, development environments, Git and MCP configuration.

### Projects, Sessions, Settings, Diagnostics

- **Projects**: open a folder, start without a project (`%USERPROFILE%\AGEX-Workspace`), recent projects.
- **Sessions**: every request is saved. Open one to see its request, status, tasks, changed files and result; open its messages in the Agent Room; copy the result; run it again.
- **Settings**: start with Windows, start minimized, update checks, theme, time limits, how many sessions to keep, privacy notes.
- **Diagnostics**: engine status, open logs or data folder, restart the engine, run repair, and "What ran" (commands, exit codes, errors of the last request).

## Results

| Status | Meaning |
| --- | --- |
| Complete | The leader verified the goal. |
| Recovered | Complete, after AGEX switched to another agent automatically. |
| Partial | At least one task succeeded and at least one failed or was cancelled. |
| Failed | Work started but no task succeeded. |
| Could not start | No agent could plan the request. Nothing was changed. |
| Not verified | Tasks finished without errors, but the leader did not confirm the goal. |
| Cancelled | You cancelled it. Agent processes were stopped. |

## Automatic fallback and agent health

If an agent cannot start or crashes before doing any work, AGEX tries the other available agent once for that step and says so ("Codex could not start planning. Trying Antigravity automatically... Recovered using Antigravity."). It never bounces between agents. An agent that fails to start, or is not signed in, is paused for 5 minutes in this session; Retry, Test connection or Rescan try it again at once.

## Terminal

```powershell
agex             # opens the desktop app
agex --cli       # terminal control center
agex doctor      # installation and agent checks
agex agents      # supported and detected agents
agex project C:\path\to\project
agex update | agex repair | agex uninstall | agex version
```

The terminal control center has the same engine. Type a request and press **Ctrl+Enter** (or Ctrl+S, or `:send` on its own line). Commands: `:help`, `:retry`, `:codex`, `:agy`, `:details`, `:log [N]`, `:result`, `:agents`, `:project [path]`, `:clear`, `:cancel`, `:quit`. Ctrl+C cancels a running request (it asks first).

Environment variables: `AGEX_HOME` (data folder), `AGEX_CODEX_PATH` / `AGEX_AGY_PATH` (non-standard agent locations), `AGEX_CODEX_TIMEOUT_SECONDS` / `AGEX_AGY_TIMEOUT_SECONDS`.
