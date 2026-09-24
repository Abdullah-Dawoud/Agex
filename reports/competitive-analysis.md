# Competitive analysis

Date: 2026-09-24. Done after AGEX 2.0 was implemented and tested, as the brief asked. Numbers are from the GitHub API on the research date (stars, licence, last push); feature descriptions come from each project's README and docs, not from running the products. None of these projects was installed or run, so feature claims are theirs, not verified by us.

## Projects examined

| Project | Licence | Stars | Stack | What it is | Leader plans for other agents? |
| --- | --- | --- | --- | --- | --- |
| **iOfficeAI/AionUi** | Apache-2.0 | 33,080 | Electron + SQLite | "Cowork" desktop app for 20+ CLI agents and 30+ model providers; **Team Mode**: a Leader agent breaks the request into subtasks and delegates to teammate agents through a built-in Team MCP server, with an async mailbox and shared task board; per-agent permission dialogs; skills (built-in, custom, extension SDK); MCP management; file preview panel; Windows/macOS/Linux | **Yes** |
| **777genius/agent-teams-ai** | **AGPL-3.0** | 2,164 | Electron + React | A lead agent coordinates a team (Claude Code, Codex, OpenCode, Cursor, Copilot…); agents message each other, create tasks and review each other on a live kanban; hunk-level accept/reject review; configurable autonomy; MCP | **Yes** |
| **Untrivial-ai/agent-orchestrator** (formerly ComposioHQ) | Apache-2.0 | 12,329 | Go daemon + Electron | Persistent project orchestrator plans work and spawns workers across 27 agent harnesses; every worker gets its own branch and worktree; kanban by status (Working, Needs You, In Review, Ready to Merge); CI and PR review loop | **Yes** |
| **Orkas-AI/Orkas** | MIT | 2,115 | Electron + Node + Python | Commander LLM dispatches to specialist sub-agents (research, writing, slides, code…); can drive Claude Code, Codex and OpenCode as subprocesses; bring-your-own keys; local knowledge base | **Yes** (mostly its own agents) |
| **generalaction/emdash** (YC W26) | Apache-2.0 | 5,814 | Electron | Run many coding agents in parallel, each in its own worktree; diff review, PR, CI, merge | No |
| **BloopAI/vibe-kanban** | Apache-2.0 | 28,177 | Rust + web | Kanban of issues executed by agents in worktrees; **announced as sunsetting** | No |
| getpaseo/paseo | NOASSERTION | 18,294 | TypeScript | Orchestrate coding agents from desktop and mobile | Not established |
| superset-sh/superset | NOASSERTION | 14,559 | TypeScript | Agentic IDE for many agents in parallel | No |
| smtg-ai/claude-squad | AGPL-3.0 | 8,528 | Go (terminal) | Manage several terminal agents in tmux/worktrees | No |
| bradygaster/squad | MIT | 3,233 | TypeScript | AI agent teams for a project | Partly |
| nimbalyst/nimbalyst | MIT | 1,773 | TypeScript | Visual workspace for Claude Code, Codex, OpenCode | No |
| raysonmeng/agent-bridge | MIT | 358 | TypeScript | Two-way bridge between Claude Code and Codex | Peer-to-peer |
| aaif-goose/goose | Apache-2.0 | 54,606 | Rust | A single extensible agent (not an orchestrator of other agents) | — |

A community list (andyrewlee/awesome-agent-orchestrators) names dozens more desktop orchestrators; the category is crowded and growing monthly.

## Comparison with AGEX 2.0

| Capability | AGEX 2.0 | AionUi | agent-teams-ai | Agent Orchestrator | Emdash |
| --- | --- | --- | --- | --- | --- |
| Leader decomposes and delegates to other CLI agents | Yes | Yes | Yes | Yes | No |
| Agents message each other, visible to user | Yes (Agent Room, max 8 turns) | Yes (mailbox) | Yes | Within worker sessions | No |
| Number of agents | 5 (2 stable, 3 beta) | 20+ CLIs, 30+ providers | ~9 | 27 | ~9 |
| Parallel work isolation | File ownership + verification | Shared folder | — | Worktree per worker | Worktree per task |
| Diff review | Changed-files list, open file | Preview panel | Hunk-level accept/reject | Per worker | Diff, PR, merge |
| Independent check of files agents claim to have changed | **Yes** | Not documented | Not documented | Not documented | Not documented |
| One-click undo of a whole request | **Yes** (Git snapshot) | Not documented | Hunk reject | Branch per worker (discard) | Branch per task |
| Approval before changes | Yes, per request / trusted project | Per-agent permission dialogs | Configurable | Via review | — |
| Skills | Curated, commit-pinned, SHA-256-checked catalog; permissions; trust labels | Built-in + custom + extension SDK | MCP; plugins planned | Repository skills | — |
| Local models | Ollama, with local/cloud labels | Ollama, LM Studio | Free model option | Via harnesses | — |
| PR / CI loop | No | No | No | **Yes** | Yes |
| Mobile | No | No | No | No | No |
| Runtime | .NET + Avalonia (no Electron) | Electron | Electron | Go + Electron | Electron |
| Platforms verified by the project | Windows x64 only (this pass) | Win/macOS/Linux releases | Win/macOS/Linux releases | Win/macOS/Linux | Win/macOS/Linux |
| Community and releases | None yet | Large | Small but very active | Large | Medium, funded |

## The eight questions

**1. Which project is closest to AGEX?**
AionUi's Team Mode. It does the same thing AGEX was built for: a leader agent splits a request and delegates to other installed CLI agents, which run in parallel, exchange messages and report to a shared task board, with permission prompts and a skills/MCP system, on all three desktop OSes. agent-teams-ai is equally close in concept (lead, messaging, review) but smaller.

**2. How much of AGEX already exists there?**
Roughly 75% of AGEX's user-facing features have an equivalent in AionUi, and it covers many things AGEX does not (far more agents and providers, document preview, extension SDK, mature releases). This is a judgement from documentation, not a measured number.

**3. What does AGEX do that they do not (as far as their documentation shows)?**
- AGEX verifies every file an agent claims to have created or changed, and marks the request *Needs repair* / *Partly complete* instead of trusting the agent's report.
- A Git snapshot before the first write of every request, with one-click undo, without touching the user's index or branches.
- A curated skill catalog where every file is pinned to a commit and hash-checked, with per-skill permissions and trust levels, and skills re-verified at every start.
- A strict "no fabricated dialogue, no hidden reasoning" rule in the Agent Room, plus an honest status taxonomy (Not installed / Sign-in required / Not working / Not available on this system).
- A native .NET + Avalonia app instead of Electron (smaller memory footprint expected; not benchmarked).
- An Antigravity adapter built on its stream-JSON interface (not listed by the others).

None of these is large enough on its own to be a product; together they form a "safe and verifiable" angle.

**4. What do competitors do better?**
Nearly everything that depends on breadth and maturity: number of agents and providers; releases actually tested on macOS and Linux; worktree isolation (Agent Orchestrator, Emdash); hunk-level review (agent-teams-ai); PR and CI integration (Agent Orchestrator, Emdash); file previews (AionUi); community, documentation and support. AGEX has zero users besides its author.

**5. What should AGEX adopt? (ideas, not code)**
- **Agent Client Protocol (ACP)**, which AionUi uses for external agents: one standard adapter could replace much per-agent work. Evaluate first.
- Worktree per writing task as an option (planned in the roadmap).
- A "Needs you" grouping of everything waiting for the user (approvals, questions) across sessions.
- Hunk-level review of changes before accepting (idea only: agent-teams-ai is AGPL and its code must not be copied).
- PR creation and CI feedback through the GitHub skill.

**6. Is AGEX worth continuing?**
As a general product competing with AionUi or Agent Orchestrator: **no**, not on current resources. They are ahead by a large margin and moving fast. As a personal tool, or as a small project focused on the "verified and reversible" angle, it is worth continuing. Keep the scope narrow and do not chase agent count.

**7. Build or contribute?**
For most users' needs, using AionUi (or Agent Orchestrator for PR-centred work) is better today. The distinctive AGEX pieces — claim verification, per-request snapshot and undo, the pinned skill catalog — would be more valuable as contributions to one of those Apache-licensed projects than as reasons for a separate app. Recommended path: keep AGEX for the author's own Codex + Antigravity workflow, and propose verification and snapshot/undo upstream to AionUi.

**8. Who is better today?**
AionUi for most users; Agent Orchestrator for developers who work through PRs and CI; Emdash for parallel independent attempts. AGEX is better only for a user who specifically wants an independent check of agent claims plus one-click undo, and who uses Codex and Antigravity.

## Code reuse

No code from any of these projects was copied into AGEX. agent-teams-ai and claude-squad are AGPL-3.0 and must not be copied; projects with NOASSERTION licences (Paseo, Superset) have unclear terms and were not read for code.
