# AGEX 2.1 user workspace — report

Date: 2026-09-24. Version: 2.1.0 (prepared, not published; the v2.0.0 tag is unchanged).
Everything below was checked on the new build (Windows 10 19045, display at 125% scaling), not on the installed 2.0.0.

**MODEL PICKER**
Every integrated agent has a picker with **Auto (the agent's own default)** first, then the models the agent itself reports; a custom model ID exists only under Advanced. Seen in the new build: Codex 7 models from `codex debug models` (vision, reasoning, 272k context), Antigravity 14 from `agy models`, Ollama 10 from `/api/tags` plus `/api/show` (local/Ollama cloud, tools, reasoning, vision, context), OpenCode 16 from `opencode models` (local and free models first, "Free (OpenCode Zen)"). Only reported facts are shown. Lists refresh after sign-in, after an agent version change (the cache is marked stale), from the refresh buttons, and when the 12-hour cache expires; a failed refresh says "Could not refresh models" and keeps the last list. Codex no longer shows a free-text model field.

**OPENCODE INTEGRATION**
Implemented (beta). `opencode run --format json`, prompt on stdin; `--auto` when writing is allowed, the built-in read-only `plan` agent otherwise; attachments with `--file`; JSON events (text, tool use, step finish with tokens, error) parsed, reasoning ignored. Sign-in check with `opencode auth list`, sign-in with `opencode auth login`. A real run with the free model `opencode/big-pickle` succeeded earlier in this pass. The new build found a real defect: OpenCode installed under an nvm folder with a space ("Author Software") failed to start through `cmd.exe`. Fixed: cmd gets its own quoting, and npm shims that only start a native `.exe` are run directly. After the fix the card shows version 2.0.10 and 16 models. Copilot CLI and Aider were evaluated and not integrated (plain-text output, no reliable completion or result signal); Cursor Agent CLI and Qwen Code were not evaluated in depth. They offer **Learn more** and **Request integration**.

**AGENT INSTALL**
Before installing, AGEX shows the official source, the package, the location, the size, whether admin rights are needed, the account needed and where data goes. One-click npm install covers Codex, Claude Code, Gemini CLI and OpenCode (`opencode-ai`). Antigravity and Ollama get the official instructions. Nothing is installed silently.

**AGENT SIGN-IN**
Each agent's own login flow runs in a terminal (Codex, Claude Code, Gemini CLI, OpenCode `auth login`). AGEX never asks for passwords. Checks shown in the new build: Codex "Signed in · ChatGPT", Antigravity "Signed in" after refresh, Claude Code "Sign-in required" with a Sign in button.

**CAVEMAN**
Verified in the new build:
- Skills search "caveman" finds 3 entries.
- Details show AGEX Curated, author, licence ("MIT (skills folder; engine and proxy are BSL-1.1 and not included)"), cost LOCAL, the source pinned to commit 2fd153c, "only change how agents write", no account, and the risk note.
- Install into an isolated data folder: "Every file matched its pinned checksum."

The install ran through `agex skills install caveman` against the same data folder the app uses. Driving the install dialog through UI automation was unreliable, so that single click was not scripted. Caveman is also in the Local/Private and Token Saver packs and in the Local Private AI team.

**OMNIROUTE**
Added as an integration, not a skill: Agents > **Routing & Providers** lists OmniRoute (MIT, local gateway at `http://localhost:20128/v1`, Responses API), Ollama and LM Studio on this computer, OpenRouter, and custom endpoints. Each entry shows its cost label, privacy ("Stays on this computer" or "Requests leave this computer"), whether an API key is required, and **List models** (`GET /models`). A provider is used only after you select it for Codex; keys stay in the system key store and are passed as an environment variable. The Skills page has a Routing & Providers category that points there.

**FREE-FIRST OPTIONS**
Skill labels: LOCAL, FREE, FREE TIER, PAID and API KEY REQUIRED, with a Cost filter (All, Free, Free tier, Local, Paid). Free-tier claims come from the official pricing pages fetched on 2026-09-24 (reports/skills-research.md). Other free-first features:
- Provider presets never call a cloud service free.
- Team checklists show "Free option" alternatives.
- OpenCode's free Zen models and local Ollama models are listed first.

**TOKEN-SAVING**
Built into AGEX, via Efficiency mode on Home and in Settings: Maximum quality, Balanced, Save tokens, Local-first. The mode is written into every request's timeline, so quality is never reduced silently.

Measured by an automated test with a 250-file project: the leader's first prompt was 24,571 characters in Balanced and 7,589 in Save tokens, 69% shorter. Save tokens:
- sends 120 files without dates;
- shortens earlier results and messages;
- asks agents for brief answers;
- uses low effort for Codex and Antigravity when effort is left at the default.

The catalog adds Repomix (the project claims about 70% savings in compress mode; not measured by AGEX) and Serena (symbol-level retrieval), plus a Token Saver pack. Snake-oil compressors were rejected.

**ATTACHMENTS**
Attach button, drag and drop, and pasting a screenshot or copied files (Ctrl/Cmd+V). Each file becomes a chip with its type and size, which you can preview or remove. Supported files:
- images;
- PDF;
- DOCX, XLSX and PPTX (text extracted locally, with no external program);
- text and code;
- zip (file list only);
- video (details, and 6 frames with ffmpeg if you allow it).

Programs and unknown binaries are refused. Limits: 20 files, 100 MB each. Before sending, AGEX shows a delivery plan for each agent, for example "These files may be sent to OpenAI cloud (Codex)" or "stay on this computer". Delivery by agent:
- Codex: images with `-i`.
- Antigravity and Claude Code: `--add-dir`.
- Gemini CLI: `--include-directories`.
- OpenCode: `--file`.
- Ollama: the text inline, and images only for vision models.

Verified in the new build: two files attached through the real file dialog, chips shown, and the image previewed in the panel.

**TEAMS**
The Teams page has 11 job teams: Software Builder, Research Lab, Architecture & BIM, Marketing & Growth, Computer Operator, Job Search & Applications, Document Office, Data Analyst, Security Review, DevOps & Release and Local Private AI. Each team has:
- a goal, typical tasks and outputs;
- an approval level;
- required, recommended and optional tools, with x/y readiness;
- a setup checklist with Install, Connect, Official download, Skip, Open Agents, and Set up everything (installs only free no-account skills after one confirmation).

A team's brief and approval rules go into every prompt. Read-only teams never let agents write. Local Private AI pins agents on Auto to a local model they reported.

**ARCHITECTURE TEAM**
Architecture & BIM covers PDF drawings, research, schedules, reports and AutoCAD scripts. Revit and AutoCAD are detected in Program Files\Autodesk. The Autodesk AI Bridge is detected only by its installed Host. **Connect Revit/AutoCAD** appears only when the bridge is installed, and adds it as a local MCP tool that asks before each use. Otherwise **Learn how to connect** explains the steps and that the bridge has no public release yet. Direct control is never claimed. On this computer the bridge is not installed, so the item shows "Not installed".

**MARKETING TEAM**
Marketing & Growth works at the Browser actions level. It uses Web Fetch, Exa (free, rate-limited), Playwright, Firecrawl (free tier) and Frontend Design, with Notion optional. Its outputs are campaign plans, content calendars (CSV), ad and social copy, and SEO audits.

**COMPUTER OPERATOR TEAM**
Computer Operator works at the Sensitive level. Its brief makes agents stop and ask before submitting, sending, paying, uploading, deleting or changing accounts. It uses Playwright (required), Chrome DevTools, and optionally Windows-MCP (Windows only, Community, high risk, asks each time). Job Search & Applications never submits without approval.

**SIDE WORKSPACE**
The panel on Home and the Agent Room has five tabs: Activity, Files, Preview, Diff and Computer. You can:
- resize it with its left edge;
- hide it with the arrow, the top-bar button or Ctrl/Cmd+J;
- pop it out into its own window, which docks again when closed.

Width and open state are saved. On a 1366×768 screen at 125% scaling (about 1093 DIPs wide) it docks at 30% of the width, and it is hidden below 1040 DIPs.

**LIVE PREVIEW**
The Preview tab shows images (with pixel size) and text or code (first 600 lines). Other files get Open, Show in folder and, once a preferred editor is set, Open in <editor>. HTML shows its source with a hint to open it in the browser. There is no embedded web view. Diff compares against the snapshot taken before the request, or the last commit.

**COMPUTER VIEW**
The Computer tab shows the run state and Pause, Resume, Take control (pauses the team so you can work) and Stop, plus the last 40 actions agents reported. It states that AGEX does not move the mouse or type into other apps. Hidden reasoning is never shown.

**TESTS**
111 of 111 pass, run by `tools/build-release.ps1` in Release. There are 31 new tests:
- 28 in WorkspaceTests: OpenCode parsing and arguments, Ollama `/api/show`, provider URLs and presets, attachment classification, Office text, copying and refusal, the delivery plan and inline text, teams against the catalog, team briefs, the Autodesk bridge honesty rule, skill costs, Codex provider arguments without the key, and the measured Save tokens reduction;
- 2 in RuntimeTests: batch files in folders with spaces, and npm shims for native programs;
- the updated onboarding install list.

The known intermittent Windows test (fake-agent log contention, recorded earlier) failed once in the Debug run and passed on the next two full runs. `git diff --check` is clean.

Visual checks were screenshots of the new build at 1366, 1600 and 1920 px windows. They cover:
- the Codex, Antigravity, Ollama and OpenCode dropdowns;
- the Home composer with attachment chips and the panel;
- the Teams page and team setup;
- Skills with the Cost filter and Caveman;
- the Agents providers and editor actions;
- the panel's Preview and Computer tabs;
- the dark, light and high-contrast themes (`AGEX_HIGH_CONTRAST=1`, without changing the system setting).

No controls were clipped. Issues found by this check and fixed:
- the panel was hidden at 125% scaling;
- the Teams page re-parented a panel;
- long OpenCode model labels were too wide;
- mixed local/cloud agents were labelled "Cloud";
- the Local Private team excluded agents on Auto;
- the panel toggle icon was unclear;
- a duplicate "Free option: Free" badge;
- the CLI printed "It may: ." for skills with no permissions.

The screenshots are in `%USERPROFILE%\AGEX-2.1-evidence\screenshots` (not committed).

**NEW LOCAL BUILD**
Package: `%USERPROFILE%\AGEX-2.1-evidence\package\agex-2.1.0-win-x64.zip` (SHA-256 in `SHA256SUMS.txt` next to it). It was installed with the release installer (`-NoPath -NoShortcut -NoLaunch`) to `%LOCALAPPDATA%\AGEX-2.1-test`. `agex --version` prints "AGEX 2.1.0 (win-x64)". The desktop app from that install was started and showed the new Agents page with the model picker.

**INSTALLED v2.0.0: UNCHANGED**
`%LOCALAPPDATA%\Programs\AGEX\install.json` still says 2.0.0, and the newest file timestamp there is the same before and after. All tests used separate data folders (`AGEX_HOME`).

**COMMIT**
The commit on branch `codex-live-agent-dashboard` that contains this report. It is not pushed and not released.

**REMAINING**
- The UI-automation click on the skill install dialog; the install itself was verified through the CLI.
- An embedded browser or web view in Preview (HTML opens in the browser today).
- Live screen view of the desktop in the Computer tab (only reported actions are shown).
- Adapters for Copilot CLI and Aider, once they have structured output.
- Real-account runs for the beta adapters (Claude Code, Gemini CLI).
- A real Revit or AutoCAD session through the bridge (the bridge is not installed here).
- Providers for agents other than Codex.
- Screenshots at true 100% scaling (this display is 125%).
- Publishing v2.1.0 after acceptance.
