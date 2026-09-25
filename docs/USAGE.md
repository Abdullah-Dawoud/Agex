# Using AGEX

## First start

AGEX opens right away and scans in the background. Setup walks you through:

1. **Welcome.**
2. **Scan** — agents, editors, tools and integrations, found only in known install locations. Nothing is sent anywhere.
3. **Agents found** — each with a status: Ready, Sign-in required, Not working, Not installed, or "Detected — not integrated" for tools AGEX cannot drive.
4. **Choose your agents** — Codex and Antigravity are pre-selected when they are ready.
5. **Recommended skills** — optional; nothing is installed unless you tick it.
6. **Choose a project** — the folder the agents work in.
7. **Ready.**

Every step can be skipped and changed later (**Settings → General → Run setup again**).

## Pages

| Page | What it is for |
| --- | --- |
| **Home** | The conversation: "What do you want to do?" with team quick starts, then your request, progress and the answer with a change summary. The composer at the bottom has Attach, Team, Skills, Efficiency and Agents. |
| **Agent Room** | The conversation: assignments, questions, answers, results, reviews, revisions, status and tool events. Filter by agent, task or type; search; copy; expand long messages; follow live. The **Task graph** tab shows the plan as steps. |
| **Teams** | Job teams (Software Builder, Research Lab, Architecture & BIM, Marketing & Growth, Computer Operator, Job Search & Applications, Document Office, Data Analyst, Security Review, DevOps & Release, Local Private AI): what each does, what it needs, how ready this computer is (x/y tools ready), and a setup checklist (Ready, To set up, Optional, Programs and services) with Install, Connect, Official download, Use free alternative, Skip and Set up recommended. Each team lists the files it works with and the actions it always asks about. |
| **Connections** | Everything agents can use: programs on this computer, web services, MCP servers and existing MCP connections found in your other AI tools, each with its state and next step. Add connection searches what AGEX knows and, on request, the public MCP Registry. |
| **Projects** | Your project folders and each one's own team, sharing preset, file-change permission, trust, ignored folders, instructions and skills. |
| **Agents** | Which agents AGEX may use, their status, where their data goes, the model picker (models the agent itself reports, with facts such as local, reasoning, vision and context), effort, whether they may change files; how work is shared; saved teams; Routing & Providers (optional endpoints for Codex); other tools found, each with an action (Open project, Use as preferred editor, Learn more, Request integration). |
| **Skills** | Browse and install skills, manage permissions, update, add your own. |
| **Sessions** | Every past request. Search across all sessions, see the timeline, artifacts and what ran; continue, retry, clone, export, undo, delete. |
| **Settings** | General, appearance (System/Light/Dark, text size), approvals and safety, privacy, sessions, notifications, updates, backup/import, advanced, diagnostics. |

The **Workspace** panel on the right of Home and the Agent Room has five tabs: **Activity** (what each agent is doing), **Files** (files you attached, files the team changed, reports), **Preview** (web pages and local dev servers inside AGEX, images, text and code; other files open in their own app, or in your preferred editor), **Diff** (changes against the snapshot taken before the request, or the last commit) and **Computer** (run state with Pause, Resume, Take control and Stop, a live screenshot of the main screen every 2 seconds while a request runs, the window in front, the latest action agents reported, and the list of actions).

The web preview uses the system web engine: Microsoft Edge WebView2 on Windows (built into Windows 11 and current Windows 10), WKWebView on macOS, WebKitGTK on Linux (install `libwebkit2gtk-4.1` if it is missing). It loads only files from the previewed page's folder and servers on this computer (`localhost`, `127.0.0.1`); any other link is blocked and offered as **Open in your browser**. Live screenshots stay in memory, are never saved or sent, can be switched off, and are available on Windows and macOS (macOS asks for the Screen Recording permission); Linux shows the action list only. Drag its left edge to resize it, hide it with the arrow or Ctrl/Cmd+J, or open it in its own window; AGEX remembers the width and whether it is open. It never shows an agent's hidden reasoning.

## Skills for a request

The Skills button in the composer shows Auto or your choice. Auto uses the skills you switched on plus the ones pinned to the team in use. Choose for this request picks exactly the skills for the next request (even ones switched off for Auto); chips show them and "Back to Auto" undoes it. Profiles save a selection; built-in ones are Quick coding, Deep research, Save tokens, Architecture review, Marketing research and Local only. Pin to team keeps a skill on whenever that team is in use. Under Advanced, tick a skill for some agents to give it only to them; a skill ticked for no agent goes to every agent that can use it.

## Changes and files

Each result shows "Changes: N files +A -R". Click it for the Changes tab: added (A), modified (M), deleted (D) and renamed (R) files with their line counts; click one for a unified or side-by-side diff. AGEX keeps the text of the project's files in memory when a request starts (text files up to 512 KB, 40 MB in total), so line counts and diffs work without Git; older sessions use Git when available. The Files tab shows the project tree with changed files highlighted.

## Attachments

Use **Attach**, drop files on the request box, or paste a screenshot (Ctrl/Cmd+V). Each file appears as a chip with its type and size; click it to preview, or remove it. Supported: images (PNG, JPEG, WebP, GIF, BMP), PDF, Word/Excel/PowerPoint (text is extracted on this computer), text and code files, zip archives (the list of files only) and videos (details, and with your permission 6 still frames made with ffmpeg if it is installed). Other files, such as programs, are refused. At most 20 files of up to 100 MB each.

Before sending, AGEX shows what each agent receives and where it goes ("These files may be sent to OpenAI cloud (Codex)" or "stay on this computer"), and you confirm. Files are copied into AGEX's own data folder for that request; nothing else is uploaded.

## Efficiency

Pick an efficiency mode on Home or in Settings:

| Mode | What changes |
| --- | --- |
| Maximum quality | Full context; Codex and Antigravity use high reasoning effort when you left effort at the default. |
| Balanced | The default. |
| Save tokens | The leader sees a shorter file list (120 files, no dates), earlier results and messages are shortened, agents are asked for brief answers, and Codex/Antigravity use low effort when left at the default. Measured: the leader's first prompt for a 250-file project was 69% shorter (24,571 to 7,589 characters). |
| Local-first | The leader gives every task a local model can do to the local agent. |

The mode in use is written into each request's timeline, so quality is never reduced silently.

## Making a request

Type what you want in plain language and press **Send** (or Ctrl+Enter / Cmd+Enter). Examples:

- "Add a dark-mode switch to the settings page and test it."
- "Why does `npm test` fail? Fix it."
- "Summarise the documents in this folder into README.md."

What happens:

1. **Planning.** The leader agent reads your project and either answers directly (questions) or makes a plan: tasks, which agent does each, which files each will change and what must happen first.
2. **Approval.** Before the first file change, AGEX asks once: *Allow*, *Allow and trust this project*, or *Don't allow*. Trusted projects are not asked again. For Git projects AGEX first saves a snapshot so you can undo.
3. **Work.** Tasks without dependencies run in parallel. Two tasks that would change the same file never run at the same time.
4. **Checking.** AGEX checks every file an agent claims to have created, changed or deleted. A claim that does not match the folder is sent back for repair.
5. **Review.** The leader reviews the results, asks for revisions if needed, and confirms when the goal is met (up to six review rounds).

The first time a project is used with cloud agents, AGEX tells you which services will receive your request and the files the agents read.

### When an agent needs you

If the leader cannot continue without a decision ("Which database should I use?"), Home shows the question with an answer box, and AGEX notifies you if the window is in the background. Answer once and the request continues. **Skip** stops the request.

### Pause, cancel, retry

- **Pause** stops new tasks from starting; running agents finish their current step. **Resume** continues.
- **Cancel** stops all running agents. Files already changed stay changed — use **Undo changes** in Sessions if a snapshot exists.
- On the result: **Retry**, **Retry with…** (other agents), **Continue** (a follow-up that knows the earlier result), **Copy result**, **Undo changes**.

### Results and statuses

| Status | Meaning |
| --- | --- |
| Complete | The leader checked the result against your goal. |
| Complete (recovered) | Complete, after AGEX switched to another agent because one failed. |
| Partly complete | Some tasks finished, others failed or were skipped. |
| Not verified | Tasks finished but the leader did not confirm the goal. |
| Failed / Could not start | Nothing useful finished, or no agent could plan. The reason is shown. |
| Cancelled / Interrupted | You stopped it, or AGEX was closed while it ran (nothing restarts automatically). |

Usage (tokens, cost) is shown only when an agent reports it; otherwise AGEX says it is not reported.

## Teams and sharing presets

A **team** is a saved set of agents plus a leader (for example "Coding team: Codex + Antigravity", "Local team: Ollama"). Pick it next to the Send button.

**How work is shared** (Agents page):

| Preset | Behaviour |
| --- | --- |
| Automatic | The leader assigns each task to the best-suited agent. |
| Balanced | Spread evenly. |
| Fast | Prefer the agents that finished fastest in your past requests. |
| Best quality | Prefer your quality order (you set it; AGEX does not rank agents). |
| Low cost | Prefer local agents; cloud only when needed. |
| Local only | Only agents that run on this computer; nothing goes to cloud services. |
| Custom | Exact percentage shares (Advanced). |

## Keyboard

Shortcuts are optional; every action is also a button. Ctrl on Windows/Linux, Cmd on macOS.

| Keys | Action |
| --- | --- |
| Ctrl/Cmd+K | Search commands, projects and sessions |
| Ctrl/Cmd+Enter | Send the request |
| Ctrl/Cmd+N | New conversation |
| Ctrl/Cmd+O | Open a project |
| Ctrl/Cmd+F | Search the Agent Room |
| Ctrl/Cmd+1 … 9 | Go to a page |
| Ctrl/Cmd+J | Show or hide the workspace panel |
| Ctrl/Cmd+, | Settings |
| Ctrl/Cmd+Shift+L | Light or dark theme |
| Esc | Close a dialog |

## The agex command

```text
agex                         open the desktop app
agex --safe-mode             open without skills
agex run "<request>" [--project <folder>] [--team <id>] [--agents codex,antigravity] [--yes]
agex --cli                   type requests one after another in the terminal
agex doctor | agents | project [<folder>] | sessions ... | skills ... | update | repair
agex settings export <file> | import <file> | backup | uninstall | version
```

`agex run` prints the timeline as it happens and exits with 0 (complete), 3 (partly complete or not verified), 130 (cancelled) or 1 (failed). Without `--yes` it asks before file changes; in a script with no terminal it does not allow them.

## Safe mode and recovery

- **Safe mode** (`agex --safe-mode`, or Settings → Advanced) starts without any skills. Use it if a skill causes trouble.
- If AGEX closes while a request runs, the next start marks it **Interrupted**; it is never restarted on its own. Your unsent request text, project and page are restored.
- **Settings → Diagnostics → Repair AGEX** fixes AGEX's own files (folders, damaged settings, session index, broken skills, caches). It never installs or changes agents.

## Backup and moving to another computer

**Settings → Backup and import**: export settings (optionally with projects; never tokens), import them elsewhere, or back up now. Imports save a backup of your current settings first. The export lists your skills so AGEX can offer to install them on the other computer.
