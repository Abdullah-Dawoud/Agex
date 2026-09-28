# Using AGEX

## First start

AGEX opens right away and scans in the background. Setup has four short steps:

1. **Welcome.**
2. **What would you like AGEX to help you with?** Software, Architecture / BIM, Marketing, Research, Documents, Data, Automation, Job search, Private / local AI or Other. Pick any; nothing is installed.
3. **Choose your agents** from the ones found on this computer.
4. **Ready**, with the Teams that fit your answers. **Set up** on a Team prepares its recommended skills; it is optional.

A project folder is not needed to start: chat, questions and web or browser tasks work without one. Every step can be skipped and changed later (**Settings > General > Run setup again**).

## Pages

| Page | What it is for |
| --- | --- |
| **Home** | A chat. Type and press Enter (Shift+Enter for a new line). **+** holds attachments, skills, Team, agents, efficiency, approvals and the project. Small chips appear only when they matter: the mode (Auto, Ask, Plan, Build), the Team in use, and the skills this message will use. Conversations and New chat are at the top left. |
| **Agent Room** | The conversation: assignments, questions, answers, results, reviews, revisions, status and tool events. Filter by agent, task or type; search; copy; expand long messages; follow live. The **Task graph** tab shows the plan as steps. |
| **Teams** | Job teams (Software Builder, Research Lab, Architecture & BIM, Marketing & Growth, Computer Operator, Job Search & Applications, Document Office, Data Analyst, Security Review, DevOps & Release, Local Private AI): what each does, what it needs, how ready this computer is (x/y tools ready), and a setup checklist (Ready, To set up, Optional, Programs and services) with Install, Connect, Official download, Use free alternative, Skip and Set up recommended. Each team lists the files it works with and the actions it always asks about. |
| **Connections** | Everything agents can use: programs on this computer, web services, MCP servers and existing MCP connections found in your other AI tools, each with its state and next step. Add connection searches what AGEX knows and, on request, the public MCP Registry. **Install & Connect** installs a reviewed tool (and Node.js or uv when missing), stores its key, connects it and tests it; **Update** installs a newer pinned version. Revit and AutoCAD install the prebuilt Autodesk AI Bridge the same way. |
| **Projects** | Your project folders and each one's own agents, models, providers, team, sharing preset, file-change permission, trust, ignored folders, instructions and skills. Open several projects in separate windows without interrupting running work. |
| **Agents** | Which agents AGEX may use, their status, where their data goes, the model picker (models the agent itself reports, with facts such as local, reasoning, vision and context), effort, whether they may change files; how work is shared; saved teams; Routing & Providers (OmniRoute and other model endpoints for Codex or AGEX Models); other tools found, each with an action (Open project, Use as preferred editor, Learn more, Request integration). |
| **Skills** | Browse and install skills, manage permissions, update, add your own. |
| **Sessions** | Every past request. Search across all sessions, see the timeline, artifacts and what ran; continue, retry, clone, export, undo, delete. |
| **Settings** | General, appearance (System/Light/Dark, text size), approvals and permissions, privacy, sessions, notifications, updates, backup/import, advanced, diagnostics. |

The **workspace** on every page starts as a slim rail. It shows a button for each thing the current work produced, with a count: **Changes**, **Browser** (pages an agent opened), **Terminal** (commands an agent ran, with the folder, output and exit code) and **Preview**, plus **Activity** (who is working, the recent steps, Pause, Take control and Stop). Press one to open the panel on it; **More** has Files, Connections, a new browser tab and a new terminal. The panel opens compact, can be made wide or opened in its own window, and slides over the chat on small windows instead of squeezing it. AGEX remembers your choice. Watching the workspace never slows the agents, and it never shows an agent's hidden reasoning.

The web preview uses the system web engine: Microsoft Edge WebView2 on Windows, WKWebView on macOS and WebKitGTK on Linux (install `libwebkit2gtk-4.1` if it is missing). Project pages load through AGEX's local server; other links open in your browser.

## Skills for a request

Skills follow the task. For "hi" AGEX sends none; for "fix this bug" it suggests debugging and testing skills; for a PDF, the PDF skill. The skills chip shows how many the message will use. Open it to see **Suggested for this message**, **Recommended for** the Team in use, and **All skills** with search. Tick any installed skill to add it, or untick a suggested one to leave it out; your choice persists across messages in this project or in projectless chat until you change it. A Team only recommends: it never limits which skills you can use. Under **Advanced**: skill profiles, skills pinned to the Team (considered for every request of that Team and used when they fit), and which agent gets which skill.

## Agents that fail

If the agent answering a message fails (it stops, times out, needs sign-in or returns nothing), AGEX asks another ready agent and marks the reply "switched agent automatically"; details are in Activity. An agent that just failed is not asked first next time. You see an error only when every suitable agent failed; its buttons are about agents: **Retry**, **Use another agent**, **Check agents**. Missing capabilities offer their own fix, such as **Enable browser** or **Connect required tool**.

## Connection states

**Connected** means an agent used the connection: it received the tool and a harmless read-only call worked (**Test with agent**). Before that a connection is **Set up, not tested**. AGEX provides tools to enabled agents through native MCP support or its capability gateway. **Connection broken** shows why the last test failed. Results survive restarts and expire after 24 hours; disconnecting clears them.

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

## Modes

The Mode button in the composer decides how AGEX handles a message:

| Mode | What happens |
| --- | --- |
| **Auto** (default) | AGEX decides. "hi" gets a quick reply; "what is this project?" gets a read-only answer; "plan how to add login" gets a plan; "add login" or "open the game and play it" goes to the team. |
| **Ask** | One agent answers and explains. It may read files; nothing is changed. |
| **Plan** | One agent inspects the project and writes a plan (goal, findings, steps, risks, how to check). Nothing is changed. **Build this plan** hands it to the team. |
| **Build** | The team plans, carries out and checks the work (below). |

A message sent while a conversation is open continues it: the agents see the last few turns. **New chat** starts over. Each answer shows how many tokens each agent reported for it ("This request").

## Approvals and permissions

The Approvals button in the composer (and Settings > Approvals and permissions) sets how often AGEX asks:

- **Ask every time**: before each task that changes files, runs commands, uses a browser or controls the computer.
- **Smart approvals** (default): only for sensitive actions, for changes AGEX cannot undo (projects without Git snapshots, unless trusted), and before an agent takes over the mouse and keyboard.
- **Trust this session**: no questions for what you turned on, until AGEX restarts.

In every mode AGEX and the agents ask before payments, sending emails or messages, deleting significant data, account or security changes, publishing or submitting, and changing passwords, keys or tokens.

What agents may do at all: read files, write project files, run commands, browser, computer control, network, MCP tools, external communication and destructive actions. When a request needs something that is off or not connected, AGEX says exactly what is missing and offers the fix (for example **Enable browser** or **Connect required tool**), or you can continue without it.

Pages on this computer: when a request needs a browser and the project has an `index.html`, AGEX serves the project at `http://127.0.0.1:<port>` for the agents (pages that use JavaScript modules do not work from `file://`). Browser work uses an agent's built-in browser or the **Browser (Playwright MCP)** connection through AGEX.

## Making a request

Type what you want in plain language and press **Send** (or Ctrl+Enter / Cmd+Enter). Examples:

- "Add a dark-mode switch to the settings page and test it."
- "Why does `npm test` fail? Fix it."
- "Summarise the documents in this folder into README.md."

What happens:

1. **Planning.** The leader agent reads your project and either answers directly (questions) or makes a plan: tasks, which agent does each, which files each will change and what must happen first.
2. **Approval.** Depending on the approval mode, AGEX asks before file changes: *Allow*, *Trust this session*, *Allow and trust this project*, or *Don't allow*. With Smart approvals, Git projects are not asked because AGEX first saves a snapshot so you can undo.
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
