# AGEX — AI Control Center

AGEX lets several AI agents work together on your projects. You describe what you want; a leader agent plans the work, AGEX hands tasks to the agents you chose, checks what they did, and shows every step. Everything AGEX stores stays on your computer.

![AGEX Agent Room](docs/images/agent-room.png)

*The Agent Room during a demo request (recorded with AGEX's test agents).*

## Install

**Windows 10/11** (x64 or ARM64) — in PowerShell:

```powershell
irm https://raw.githubusercontent.com/Abdullah-Dawoud/Ai-COGY/main/install/agex-install.ps1 -OutFile "$env:TEMP\agex-install.ps1"; powershell -ExecutionPolicy Bypass -File "$env:TEMP\agex-install.ps1"
```

**macOS 14+** (Apple Silicon or Intel) and **Linux** (x64 or ARM64) — in Terminal:

```sh
curl -fsSL https://raw.githubusercontent.com/Abdullah-Dawoud/Ai-COGY/main/install/agex-install.sh | sh
```

The installer checks the download's SHA-256 before it installs anything, needs no administrator rights and no developer tools. Details, manual install and the macOS first-launch step: [docs/INSTALL.md](docs/INSTALL.md).

> **Status: AGEX 2.0.0 is a pre-release.** Runtime-tested on Windows x64 only. Windows ARM64, macOS (Intel and Apple Silicon) and Linux packages are built, and the test suite passes on Windows, macOS and Linux CI runners, but the app has not yet been run on real ARM64 Windows, Mac or Linux desktops. macOS and Windows packages are not code-signed. Details: [reports/platform-compatibility.md](reports/platform-compatibility.md), [reports/release-2.0.0.md](reports/release-2.0.0.md). To help test: [docs/MAC_TEST.md](docs/MAC_TEST.md), [docs/LINUX_TEST.md](docs/LINUX_TEST.md).

## Use

1. Open AGEX. The first start scans your computer for AI agents and walks you through setup.
2. Choose a project folder.
3. Type what you want done and press **Send**.
4. Watch the plan, the agents' messages and the results live. Answer when an agent asks you something.
5. Every request is saved under **Sessions**, with an **Undo changes** button when your project uses Git.

AGEX works with agents you already have:

| Agent | What AGEX uses | Status |
| --- | --- | --- |
| Codex CLI (OpenAI) | `codex exec` | Supported |
| Antigravity CLI (Google) | `agy` stream mode | Supported |
| Claude Code (Anthropic) | `claude -p` | Beta |
| Gemini CLI (Google) | `gemini` non-interactive | Beta |
| Ollama (local models) | local HTTP API | Beta — private, offline, text answers only |

Other tools (OpenCode, Copilot CLI, Aider, IDEs…) are detected and shown as "detected, not integrated". AGEX never installs, signs in to or changes your agents. More: [docs/AGENTS.md](docs/AGENTS.md).

**Skills** add know-how and tools, installed with one click from a small curated catalog (browser automation, library docs, code review, testing, GitHub…). Every catalog entry is pinned to an exact version and checked by checksum. See [docs/SKILLS.md](docs/SKILLS.md).

Prefer the terminal? `agex run "add a README"` runs a request without the window; `agex help` lists every command.

## Privacy in one paragraph

AGEX has no account and sends no telemetry. Your settings, sessions and logs stay on this computer. Cloud agents (Codex, Antigravity, Claude Code, Gemini CLI) send your request and the files they read to their own providers — AGEX tells you which ones before the first request in a project. Ollama with a local model keeps everything on your machine. See [docs/SECURITY.md](docs/SECURITY.md).

## Documentation

- [Install](docs/INSTALL.md) · [Using AGEX](docs/USAGE.md) · [Agents](docs/AGENTS.md) · [Skills](docs/SKILLS.md)
- [Troubleshooting](docs/TROUBLESHOOTING.md) · [Security and privacy](docs/SECURITY.md)
- [Architecture](docs/ARCHITECTURE.md) · [Development and extension API](docs/DEVELOPMENT.md) · [Roadmap](docs/ROADMAP.md)
- [Changelog](CHANGELOG.md) · Reports: [product maturity](reports/agex-product-maturity.md), [platforms](reports/platform-compatibility.md), [skills research](reports/skills-research.md), [comparison with similar projects](reports/competitive-analysis.md)

This repository also keeps the developer-environment tools it started from (Codex mode profiles, environment doctor); see [docs/environment](docs/environment).

License: [MIT](LICENSE).
