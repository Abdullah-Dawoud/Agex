# Roadmap

Each idea was checked against four questions: does it solve a common problem, does it belong in AGEX, can it be done well now, and would it weaken security or reliability? Items that failed the third or fourth question were deferred rather than half-built.

## Done in 2.0 (needed now)

Cross-platform desktop app and CLI; five agent adapters (two stable, three beta); local models via Ollama with privacy labels; teams and routing presets including Local only; approval before file changes and Git snapshots with undo; agent questions to the user; task dependencies with parallel work; artifacts and changed files; local search; session export, clone, continue, retry-with; retention settings; command palette and shortcuts; notifications; safe mode and crash recovery; skills (curated catalog, one-click install, permissions, trust levels, custom skills, MCP servers, updates, startup isolation); diagnostics, repair, settings export/import and backups; versioned settings with migrations; update checks with checksum (and optional signature) verification.

## Next

| Item | Why | Notes |
| --- | --- | --- |
| Real-hardware validation on macOS and Linux | 2.0 is runtime-tested only on Windows x64 | Run the CI workflow on a tagged release; fix what a real Mac and a Linux desktop show. |
| Signed and notarized releases | Removes the macOS first-launch step and SmartScreen warnings | Needs a code-signing certificate and an Apple Developer ID ([DEVELOPMENT.md](DEVELOPMENT.md#signing)). |
| Worktree isolation for parallel writers | Comparable tools give each agent its own Git worktree; AGEX avoids conflicts by file ownership, which is weaker for broad changes | Optional per project; merge step shown in the Agent Room. |
| GitHub issues and pull requests | Common workflow: "fix issue #12", "open a PR" | Through the GitHub skill plus a small PR view. |
| More adapters | OpenCode, GitHub Copilot CLI, Cursor Agent CLI | Only once each has a stable non-interactive mode AGEX can verify. Promote Claude Code and Gemini CLI from beta after real-account runs. |
| Budget limits | Stop or warn when reported usage passes a limit | Only where agents report usage; never estimated. |
| Native Windows toast notifications | Today: in-app toast + taskbar flash | Needs an app identity registration. |
| Session timeline view in the Agent Room | Timeline is on Home and in Sessions | Small. |

## Later

| Item | Reason for waiting |
| --- | --- |
| Remote projects over SSH, WSL and dev containers | Useful, but every agent would need to run inside the remote environment; design first. |
| Scheduled requests | Needs clear rules for unattended file changes. |
| Project templates | Low demand compared to the items above. |
| Optional sync of settings between computers | Settings are already portable (no machine identity, no secrets); needs an end-to-end-encrypted design. |
| Organisation skill catalogs and enterprise policies | Catalog format supports it; needs signing of catalogs. |
| Agent benchmarking | Needs repeatable tasks; easy to mislead with. |
| Voice input | Platform speech APIs differ; low priority for coding. |

## Not appropriate for AGEX

| Item | Reason |
| --- | --- |
| Running arbitrary commands or scripts as "agents" | Defeats the adapter security model. |
| A hosted AGEX cloud or account | AGEX is local-first. |
| Showing model reasoning | Not reliably available, not the agent's explicit output, and misleading. |
| Telemetry by default | Only ever opt-in, if at all. |
| A mobile companion | Out of scope for a desktop control center today; would need a secure relay. |
| Bundling or installing agents | Agents are the user's choice and account. |

## Who AGEX is for

| User | Main workflow | What 2.0 gives them | Gap |
| --- | --- | --- | --- |
| Non-technical student | "Summarise these notes", "make a study plan" | Guided setup, plain language, approval before changes | Needs at least one agent account; installing agents is still up to them. |
| Junior developer | "Fix this failing test" | Plan and changes visible, undo, explanations in the Agent Room | — |
| Senior developer | Parallel work across agents, review | Task graph, verification, CLI, diffs | Worktree isolation (Next). |
| Freelancer | Several client projects | Per-project settings, instructions, trust, history per project | Budget limits (Next). |
| AI power user | Mix agents and models | Teams, presets, custom shares, skills, MCP | More adapters (Next). |
| Privacy-focused local-model user | Everything offline | Ollama adapter, Local only preset, clear local/cloud labels | Local models are text-only: they cannot edit files. |
| Team lead | Shared practices | Settings export/import, project instructions | Organisation catalogs and policies (Later). |
