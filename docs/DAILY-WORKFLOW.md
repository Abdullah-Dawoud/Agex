# Daily Workflow

## Start development

```powershell
cd <project>
codex
```

Codex reads project `AGENTS.md`, source, tests, and docs. Current source remains truth.

## Choose mode

Run from setup repository. Each command launches separate Codex session with scoped native profile:

```powershell
agex codex          # Plain Codex
agex caveman        # Caveman communication, normal coding tools
agex coworker       # Browser, Computer Use, Playwright, and artifact workflows when needed
agex orchestrator   # Codex boss with optional finite Antigravity workers
```

For one menu covering modes and projects, run:

```powershell
agex
```

Choose one mode or a combination, then choose Current Directory, any saved project, Add Project, or Open Project by Path. Add Project validates the directory, records only non-secret metadata, and never copies project files.

Fast combination launch:

```powershell
agex launch -Modes caveman,orchestrator -Project "C:\path\to\project"
```

Plain Codex stays clean: choose Plain in the menu or run `agex codex`.

Use `agex orchestrator-dispatch -WorkerCommand dispatch -Task "..."` for independent worker analysis. Check `agex orchestrator-dispatch -WorkerCommand status` after dispatch.

## Initialize project

```powershell
cd <setup-repository>
agex init-project C:\path\to\project -DryRun
agex init-project C:\path\to\project
```

Use `-CreateDocs` when wanted. Serena is removed and is never added by initialization. Existing files are never overwritten.

## Check environment

```powershell
agex doctor
agex status
agex dashboard -Open
```

## UI verification

Use Playwright MCP for repeatable browser work: navigate localhost, snapshot accessibility/DOM, fill/click, inspect console, resize, and screenshot. Use bundled browser/computer-use for exploratory interactive work.

## General worker tasks

Ask for outcomes, not tool names. Examples:

```text
Research these companies and produce a source-backed spreadsheet.
Run this project and create an illustrated user guide with screenshots.
Test the complete booking flow and produce a QA report with screenshots.
Create a browser-scoped product demo using synthetic data.
Find jobs matching this CV and prepare applications without submitting them.
Inspect this repository, fix the issue, test it visually, and create release notes.
```

Codex should inspect available tools, execute safe read/draft/prepare work, verify outputs, and pause only for login, MFA, CAPTCHA, missing identity facts, payment, legal consent, destructive actions, or consequential submission.

Read [docs/WORKER.md](WORKER.md) for routing and privacy boundaries. Use [docs/WORKER-TESTS.md](WORKER-TESTS.md) for disposable capability checks.

## Current documentation

For current version-sensitive docs, use Context7 on demand:

    npx -y ctx7 library <library> "<question>"
    npx -y ctx7 docs /org/project "<question>"

Codex can use the configured local Context7 MCP automatically; do not paste secrets or enable hosted credentials.

## Memory

No external memory engine retained. Store durable project knowledge in project docs and decision records. Never treat memory as source authority.

## Routing

No OmniRoute provider configured. Normal direct Codex remains default and critical path.
