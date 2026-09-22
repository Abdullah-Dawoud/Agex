# Architecture

## Information authority

```text
SOURCE OF TRUTH
current repository files and tests
        |
PROJECT RULES
scoped AGENTS.md files
        |
PROJECT KNOWLEDGE
architecture, database, security, decisions docs
        |
RETRIEVAL
rg first; Codex and focused tests for code inspection
        |
CURRENT DOCUMENTATION
official docs; Context7 only after benchmark
        |
MEMORY
 advisory why/history; candidate not installed
        |
VERIFICATION
tests, Git, browser, Playwright where appropriate
```

Lower layers never silently override higher layers. Memory can suggest a path; current source, schema, tests, and official documentation decide truth.

## Runtime flow

1. Agent reads the nearest applicable `AGENTS.md`.
2. Agent identifies task risk and opens only relevant project documentation.
3. Agent searches current source with `rg` and focused file reads.
4. Agent uses semantic retrieval only when search or a measured benchmark shows a gap.
5. Agent validates behavior with focused tests, Git diff, and browser checks when relevant.
6. Agent records durable architectural decisions in project documentation.

## Worker mode

Developer mode and worker mode share the same project, source, Git, and verification boundaries. Worker mode adds browser/computer control, document/PDF/spreadsheet artifact workflows, screenshots, and browser-scoped demo capture. Connected services remain explicit, task-scoped, and user-authorized.

## Setup repository boundaries

This repository manages reproducible guidance and checks. It does not own Codex credentials, session state, project repositories, SSH keys, or arbitrary user directories. Scripts use explicit paths and allowlists.

## Global capability versus project knowledge

Global tools provide reusable capability. They do not provide shared project knowledge.

- Global: Codex, Caveman skills, Git, browser tooling, doctor scripts, and approved MCP launch definitions.
- Project-local: `AGENTS.md`, architecture/security/database/decision docs, dependency manifests, and tests.
- Hybrid: Context7 and any future memory engine. Runtime can be global; project path, language/version context, permissions, namespace, and retrieval scope stay local.

## Memory boundary

The repository is the source of truth. Memory stores concise reasoning and durable lessons, not source copies. Retrieve narrowly, then verify high-risk or potentially stale claims against current code, tests, schema, and configuration. Stable knowledge should graduate to project documentation; Git already stores exact diffs and history.

Codex local memory is optional and managed by Codex; its internal database is not a repository dependency. A future Cavemem test must use explicit project namespaces and a disposable fixture before any production project is connected.

No external semantic-code MCP is retained. Codex uses repository inspection, `rg`, Git, and normal project tools.

## Performance goals

- Prefer targeted retrieval over full-repository loading.
- Keep MCP servers off unless their task-specific value exceeds context and permission cost.
- Avoid duplicate indexes, browser controllers, runtimes, and memory systems.
- Use reports for evidence, not as an always-loaded prompt.
