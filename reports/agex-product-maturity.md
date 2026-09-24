# AGEX product-maturity pass

Date: 2026-09-24. Version: 2.0.0. Branch: `codex-live-agent-dashboard`. Test machine: Windows 10 Pro 22H2 x64.

## Summary

| Field | Result |
| --- | --- |
| PRODUCT | AGEX AI Control Center 2.0.0: cross-platform desktop app and `agex` command, rewritten in C# (.NET 10, Avalonia 12). The Windows-only WPF app and PowerShell engine were removed. |
| WINDOWS | x64: **TESTED** (build, tests, installer, desktop app, real agents). ARM64: **BUILD VERIFIED**, not run. |
| MACOS INTEL | **BUILD VERIFIED** (osx-x64 package cross-built, unsigned), not run. |
| MACOS ARM64 | **BUILD VERIFIED** (osx-arm64 package cross-built, unsigned), not run. Cross-built unsigned packages will not start on Apple Silicon; the release workflow builds and ad-hoc-signs on a macOS runner (not yet executed). |
| LINUX | x64 and ARM64: **BUILD VERIFIED**, not run (running a container was not authorised). |
| DESKTOP UI | Redesigned with a design system (spacing, type scale, radius, elevation, status colours, icons, focus states); Home, Projects, Agents, Agent Room, Sessions, Skills, Settings; six-step first run; command palette; shortcuts. Checked on Windows at 1600×1000 and 1900×1300; not tested at 125–200% scaling or on 4K/Retina. |
| DARK MODE | System, Light, Dark and High contrast; switches without restart; persisted. TESTED on Windows. |
| ACCESSIBILITY | Keyboard navigation and visible focus, automation names on controls, text size setting, high-contrast theme, statuses always shown with text and icon, not colour alone. Implemented; not verified with a screen reader or a full keyboard-only walkthrough. |
| SKILLS MANAGER | Browse by category, one-click install, enable/disable/update/remove, permissions (ask / always allow / deny), trust levels, custom skills from GitHub, zip or folder, MCP servers, compatibility checks, re-verification at every start, safe mode. TESTED. |
| CURATED SKILLS | 19 (14 instruction skills, 5 MCP tools); 6 recommended, none installed by default. Research: [skills-research.md](skills-research.md). |
| ONE-CLICK SKILL INSTALL | Working: all 14 instruction skills downloaded from GitHub and verified file by file against pinned SHA-256 hashes. |
| SUPPORTED AGENTS | Codex CLI (stable, real-tested), Antigravity CLI (stable, real-tested), Ollama (beta, real-tested with a local model), Claude Code (beta) and Gemini CLI (beta): both installed on the test machine but not signed in; real runs confirmed detection and a clear "sign in" result, and their full protocols pass with the fake agent, but no successful real request was possible. |
| PLATFORM DISCOVERY | Statuses Ready / Available / Not installed / Detected, not supported / Not available on this system / Sign-in required / Not working / Paused after errors / Unknown, per OS; PATH, login-shell PATH (macOS/Linux) and known install folders; environment overrides. TESTED on Windows. |
| SECURITY REVIEW | Done; see below. No known vulnerable packages. Two trust limitations remain by design until release keys and certificates exist. |
| REAL CODEX | PASS: direct question (Arabic) answered exactly; leader for a two-agent request; leader in the final demo request (plan, implement, verify). |
| REAL ANTIGRAVITY | PASS: direct question with Unicode intact; fallback after a forced Codex start failure (COMPLETE_WITH_FALLBACK); two parallel file tasks with byte-exact results; reviewer in the final demo request. |
| BUGS FOUND | 34 (listed below) plus 1 intermittent test failure that could not be reproduced. |
| BUGS FIXED | 34. |
| KNOWN ISSUES | See "Known issues". |
| COMPETITIVE RESEARCH | Done after implementation: [competitive-analysis.md](competitive-analysis.md). |
| CLOSEST COMPETITOR | iOfficeAI/AionUi (Apache-2.0), whose Team Mode has a leader agent delegating to teammate CLI agents with a mailbox and task board. |
| AGEX UNIQUE VALUE | Independent verification of agent file claims, a Git snapshot with one-click undo for every request, a commit-pinned and hash-verified skill catalog with permissions, a strict no-fabrication Agent Room, and a native non-Electron app. Narrow; competitors are broader and more mature. |
| COMMIT | `feat: mature AGEX cross-platform agent platform and skills system` on `codex-live-agent-dashboard` (hash in the final message). |

## Validation performed

- `dotnet build Agex.slnx`: 0 errors. `dotnet test`: **66 passed, 0 failed**. The online skill test (`AGEX_ONLINE_TESTS=1`) passed.
- `tools/build-release.ps1` for win-x64, win-arm64, osx-arm64, osx-x64, linux-x64 and linux-arm64: all six packages built, `SHA256SUMS.txt` written.
- Windows installer: install from a local package, launch, checksum tamper rejected, uninstall.
- Desktop app with test agents: onboarding, privacy notice, approval dialog, Git snapshot, parallel tasks, dependencies, agent-to-agent question, cancel, restart with session history. Screens checked in light and dark themes.
- Real agents, with the user's accounts and a small number of short requests: see REAL CODEX / REAL ANTIGRAVITY above; Ollama `qwen2.5-coder:7b` answered locally in about one second.
- Adverse cases: missing project folder, read-only data folder, Unicode/emoji/spaces/accented paths, huge output, cancellation, damaged settings, zip slip, symbolic links, zip bombs, tampered skills. Not tested: very long paths, full disk.

## Bugs found and fixed

Agents and engine
1. Codex was located inside the Codex desktop app (an alpha build) instead of the CLI on PATH; PATH now wins.
2. The Codex leader planned without reading project files; the leader prompt now requires reading the relevant files.
3. Text written by agents through Windows PowerShell 5.1 was garbled (UTF-8 without BOM read as the old code page); prompts now tell agents to use `-Encoding UTF8`.
4. The Antigravity leader was not told the project folder and searched the whole drive for 10 minutes; the folder is now in the prompt and passed with `--add-dir`.
5. Leader JSON followed by extra text was rejected as an invalid plan; a balanced-brace extractor accepts it.
6. A second AGEX process marked a session that was still running as interrupted; sessions record their owner process.
7. Small local models could not follow the planning protocol; teams without file-capable agents get a direct answer instead.
8. Parallel tasks could take Git snapshots at the same time; snapshots are serialised.
9. Failure and fallback bookkeeping was not thread-safe under parallel tasks.
10. `agex version` loaded and migrated settings; it no longer touches data.

Desktop app
11. An exception in a page could close the whole app; UI exceptions are now caught, logged and shown.
12. Double-clicking Send could start two requests.
13. A global text style overrode button text colours.
14. Empty sections left large gaps.
15. Long project paths pushed the header controls off screen.
16. Tabs were oversized.
17. Copy buttons broke message card layout.
18. Follow live did not scroll when the page was hidden while messages arrived.
19. Expander headers were black in the light theme.
20. Skill cards showed "Needs ," for skills without required tools.
21. The remote MCP skill showed "vremote" as its version.
22. Session status badges were clipped.
23. Home showed "No tasks" while the leader was still planning.
24. The onboarding skills button had a misleading label.
25. Agent capability chips were noisy.
26. Toggle switches used the default accent instead of the AGEX accent.
27. Tool events in the Agent Room and Team panel showed full home-folder paths and overflowed the window; paths now show `~` and long lines end with an ellipsis.
28. The Agent Room message counter touched the first message.

Build and repository
29. `build-release.ps1` failed on Windows PowerShell 5.1 (`$PSScriptRoot` is empty in parameter defaults).
30. `-Runtime a,b` arrived as one string from some callers.
31. The skill-catalog generator wrote `null` for skills without required tools.
32. Build output (`bin/`, `obj/`) was not ignored by Git and would have been committed.
33. A stray empty `MainWindow.cs` had been created in the repository root.
34. Gemini CLI without credentials failed with the unreadable reason `"error": {` (it reports startup errors as JSON on stderr); AGEX now reads that JSON and reports "Gemini CLI needs you to sign in" (found with the real CLI; regression test added).

Not fixed: one run of the parallel engine test failed once and did not recur in later runs; the assertion now prints the session timeline so a recurrence can be diagnosed.

## Security review

Scope: process execution, agent permissions, file-change control, skills, secrets, updates, local data, UI rendering of agent output, network use, dependencies.

| Area | Finding | Status |
| --- | --- | --- |
| Process execution | Only the five known agents are started, with fixed argument lists, never through a shell; the prompt goes through stdin. Windows `.cmd`/`.bat` shims are refused if an argument contains `cmd` metacharacters (tested). No "run any command" feature exists. | OK |
| Agent permissions | Codex runs in `read-only` or `workspace-write`; Claude Code and Gemini CLI get their permission modes. **Antigravity has no read-only switch**: AGEX runs it with `--sandbox` and its permission prompts skipped, and relies on instructions plus approval before any writing task. | Accepted limitation, documented |
| File changes | Approval before the first write per request (unless the project is trusted); Git snapshot first; every claimed file checked; claims outside the project are rejected. | OK |
| Skills | Catalog files pinned to commits and SHA-256-checked; archives checked for traversal, links, device names and size; custom skills labelled unreviewed; re-verified at start; MCP commands may not contain shell characters; remote MCP must use https. Instruction skills may contain scripts that an agent may choose to run inside its own sandbox; AGEX never runs them. | OK, residual risk documented |
| Secrets | Stored with DPAPI / Keychain / Secret Service; never in settings, exports, logs or diagnostics; passed to MCP servers through environment variables only (tested). Without a Linux Secret Service, AGEX falls back to a user-only file that is not encrypted, and Diagnostics says so (seen on Linux CI). | OK, limitation documented |
| Updates | Package SHA-256 must match `SHA256SUMS.txt`. **Until a release signing key is published, a person able to publish a release on the repository could publish a matching checksum**; Diagnostics shows the mode. Signature verification is implemented and tested. | Open until release key exists |
| Code signing | Not configured; builds are unsigned and say so. | Open until certificates exist |
| Output rendering | Agent output shown as plain text; only `http`/`https` links can be opened. | OK |
| Local data | Plain JSON in the user's profile with default user-only permissions; logs redacted. Session files contain request text and agent answers. | OK, documented |
| Network | No telemetry; update check at most daily and can be turned off; skills declare network use; the Fetch skill can reach local-network addresses (warned on its card). | OK |
| Dependencies | `dotnet list package --vulnerable --include-transitive`: no known vulnerable packages in any project. Avalonia's build-time telemetry is disabled in scripts and CI. | OK |
| Build telemetry during development | Early local builds in this pass ran before `AVALONIA_TELEMETRY_OPTOUT=1` was set and therefore sent Avalonia's anonymous build telemetry from this machine. | Reported; fixed for all later builds |

## Known issues

- macOS and Linux have not been run; ARM64 Windows has not been run. The first CI run on push will provide macOS/Linux test results.
- The repository has no GitHub release yet, so the one-line install commands and in-app updating cannot work until the first tagged release; installing and updating were tested with local packages only.
- Releases are unsigned (no certificates) and the update channel is checksum-only until a release key is configured.
- Claude Code and Gemini CLI are installed on the test machine but not signed in, so no successful real request was run with them. Discovery shows them as Ready after the version check; the sign-in problem appears only on the first request.
- Local models (Ollama) are text-only in AGEX; they cannot change files.
- MCP skills work only with Codex and Claude Code.
- Parallel writers share one working tree (no worktree isolation yet).
- Not tested: display scaling 125–200%, 4K/Retina, screen readers, very long paths, full disk.
- One unreproduced intermittent failure of the parallel engine test.

## System changes made during this pass (outside the repository)

- .NET 10 SDK installed to `%USERPROFILE%\.dotnet` (no PATH change) and Avalonia/xUnit packages in the NuGet cache (approved).
- The real AGEX data folder `%LOCALAPPDATA%\AGEX` was migrated to settings schema 4 by running the new build; a backup is in `backups\20260924-070551-before-migration`.
- Checking `ollama --version` started the Ollama app, which replaced a stale Ollama process; the Ollama server was left running.
- Real Codex and Antigravity requests used a small amount of the user's quota (listed above).
- Temporary test data lives in the session scratch folder and `%TEMP%\agex-demo`; temporary test installs were removed.
- Avalonia build telemetry was sent from early builds (see security review).
