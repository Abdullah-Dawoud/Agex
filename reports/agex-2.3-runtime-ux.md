# AGEX 2.3 — runtime and UX report

Date: 2026-09-26. Version: 2.3.0, published as a pre-release after acceptance. The v2.2.0 tag and release are unchanged.

Tested on Windows 10 at 125% scaling:
- a Debug build with an isolated data folder (`%USERPROFILE%\AGEX-2.3-evidence\home`);
- real Codex 0.155.1 runs on a copy of the user's game project (`hoollaaa`).

The installed AGEX in `%LOCALAPPDATA%\Programs\AGEX` and the original project were not touched.

## What went wrong before (audit)

The failed session "open the game as a normal user and try to play it..." was read from the local session history. It failed for these reasons:

1. **The work happened inside planning.**
   - The leader (Codex) tried to run the server and open the browser during its planning turns. These run in Codex's `read-only` sandbox, with no network and no tools from AGEX.
   - It used 3 planning turns (170k to 240k input tokens each) and never created a task.
   - Nothing told the leader which agent could use a browser.
2. **MCP tools were refused inside `codex exec`.**
   - Codex logged: "MCP tool call requires approval, but approval policy is never".
   - This affected every MCP server handed to Codex, not only the browser.
3. **The page was opened from `file://`.**
   - The game loads JavaScript modules. Browsers block those from `file://`: "Failed to fetch dynamically imported module".
4. **`npm start` failed inside Codex's sandbox.** The error was "No Node.js version is configured"; the Node version manager's shim does not work there.
5. **No capability routing.** Antigravity has a built-in browser, but nothing chose it. Permissions set in AGEX did not turn into tools for the agents.
6. **The "path mismatch" (`h0011aaa`) was not an AGEX path bug.**
   - It came from error text pasted into the chat, which had been copied from the screen with `hoollaaa` misread.
   - AGEX passed the right folder every time: the session's project and Codex's `-C` argument both point to `hoollaaa`.
   - Regression tests now cover this.
7. **Why "hi" was expensive.** A greeting went through full planning on OpenCode (plus a plan repair), with 11 skills and a 300-file list: 18.4k input tokens and two runs.

All seven are fixed at the cause. Details below.

**FAST CHAT**
Done.
- Rule-based classification, with no model call.
- Greetings and small talk get one reply:
  - one read-only run;
  - no project scan, no plan, no skills and no tools;
  - lowest reasoning effort where the agent has it;
  - a 3-minute limit.
- Real Codex result for "hi":
  - before: 34.1k input tokens, 21 s, and it read README and package.json;
  - after: 16.8k input tokens and 15 s through the CLI (10.2 s in the app);
  - it read no files.

  Most of the remaining 16.8k tokens is Codex's own system prompt and tools.

**ASK/PLAN/BUILD**
Done. The Mode button in the composer offers:
- **Auto** (default): AGEX classifies each message.
- **Ask**: one read-only answer.
- **Plan**: one read-only run that writes a plan (goal, findings, steps with files, risks, how to check). It has a **Build this plan** button.
- **Build**: the team pipeline.

The chosen mode is shown on the button and in the placeholder text. `agex run --mode auto|ask|plan|build` does the same in the terminal.

**CHAT HISTORY**
Done. **History** opens the conversation list: New chat, recent conversations, and "This project" or "All projects".
- A follow-up joins its conversation, and the list shows the number of turns.
- Click a conversation to reopen it. Typing then continues it: the next request carries the last four turns.
- Tested live: after "hi", the follow-up "what did I ask you just now?" got "You said 'hi.'", with both turns on screen.
- The list is docked beside the chat when there is room, and floats over it on narrow windows, with a close button.
- Answers show headings, lists, bold and code. Links show as text only.

**PROJECT HISTORY**
Done. The conversation list has a project switcher (recent projects and "Open folder..."). Opening a conversation from another project switches to that project. Sessions shows usage for Today, This week, This project or all sessions.

**CAPABILITY ROUTING**
Done.
- Before dispatch, AGEX works out what the request needs:
  - read or edit the project;
  - run commands;
  - browser;
  - local web;
  - computer control;
  - files outside the project;
  - the internet;
  - other programs;
  - MCP tools;
  - external communication;
  - destructive actions.
- AGEX then decides which agents and tools really have those capabilities:
  - Antigravity has a built-in browser.
  - Codex and Claude Code get the Browser (Playwright) or Windows-MCP tool.
  - An agent with only file access never gets browser work.
- The leader prompt lists each agent's real abilities and the local address. It tells the leader not to do interactive work while planning and not to ask the user to enable access.
- Browser and computer tools go only to task runs, never to planning.
- A missing capability is named exactly, with a one-click fix: Enable browser, Enable computer control, Allow commands, Allow file changes, Allow network, Allow connected tools, Connect required tool, Turn it on or Open Agents. You can also continue without it or cancel.
- **Failure recovery:**
  - A task that fails, or that says it could not use a browser, is re-run once by another agent that has one.
  - Failed results show three parts: What failed, Why, and What AGEX can try next.
  - The next-step buttons are: Try another tool, Enable browser, Enable computer control, Connect required tool, and Retry.
- Tested on the real game flow; the results are below.

**LOCAL BROWSER ACCESS**
Done.
- A request that needs a page on this computer gets the project served at `http://127.0.0.1:<port>`. The server:
  - listens on loopback only and answers GET and HEAD;
  - serves files inside the project only;
  - never serves hidden files such as `.env` or `.git`;
  - never lists folders.
- The web preview uses the same server for project HTML files, so JavaScript modules load there too.
- `localhost`, `127.0.0.1` and `::1` count as the user's own computer. 10.x, 172.16-31.x, 192.168.x, 169.254.x and single-label names count as network targets.
- Codex gets network access inside its writing sandbox only for requests that need it. Antigravity's terminal sandbox is dropped only when commands and network are allowed for such a request.
- The browser tool's screenshots now go to AGEX's temporary folder, not into the project.

**COMPUTER CONTROL**
Partly verified.
- Routing and approvals are done:
  - Computer-control work goes to agents with the Windows-MCP tool.
  - It needs the "Computer control" permission, which is off by default. The "Computer control is turned off" dialog was checked live.
  - Smart approvals ask once per request before the mouse and keyboard are taken over.
  - The 2.1 Computer view (pause, resume, take control, stop) is unchanged.
- A web game uses the real browser tool: Playwright's clicks, keys and screenshots. Computer control is optional there.
- **Not verified live:**
  - Windows-MCP installed through the wizard, but its test failed on this computer. uv could not finish installing `pywin32` because another process held the file (os error 32; same with Python 3.12).
  - The wizard shows that exact error. No live mouse-control run was done in this pass.

**APPROVAL MODES**
Done. Ask every time, Smart approvals (default) and Trust this session. Trust this session resets when AGEX restarts.

The three modes differ as follows:
- **Ask every time** asks before each task, listing what the agent will be able to do.
- **Smart approvals** asks for:
  - sensitive actions;
  - changes that cannot be undone (no Git snapshot, project not trusted);
  - taking over the mouse and keyboard.
- **Trust this session** asks nothing for what you turned on.

In every mode, and in every prompt, these still need a yes:
- payments;
- sending messages or emails;
- deleting significant data;
- account and security changes;
- publishing or submitting;
- changing secrets.

A request that mentions any of these asks once before it starts. Approval dialogs offer "Trust this session".

Per-capability switches are in Settings > Approvals and permissions and under the Approvals button in Home: read files, write project files, run commands, browser, computer control, network, MCP tools, external communication and destructive actions. A setting that is off is never handed to agents.

**AGENT-SPECIFIC MODEL SETTINGS**
Done. Each adapter declares `ModelSettingsSupport`: model selection, reasoning effort levels, temperature, vision, tools, context window and custom endpoint. The Agents page renders only what the adapter declares. A saved setting the agent cannot use is never sent.
- Codex: model, reasoning effort (only the levels the chosen model reports, when Codex reports them), and model provider.
- Antigravity: model and reasoning effort. `agy --help` documents `--effort low|medium|high|max`, so the control is correct here. "max" was added.
- Claude Code, Gemini CLI and OpenCode: model only.
- Ollama: model, temperature (`options.temperature`) and context window (`options.num_ctx`).

**MODEL DISCOVERY**
- Existing:
  - automatic model lists;
  - a 12-hour cache that also expires when the agent version changes;
  - the last good list kept when a refresh fails;
  - a model that disappears falls back to Auto, with a notice.
- New: a new sign-in refreshes the list right away.
- A manual model ID is still only under Advanced.

**MCP AUTO-CONNECT**
Done. Connect on a reviewed MCP tool first shows the source, what runs, permissions, network use, account needs and the agents that use it. Then it runs these steps:
1. Check what it needs.
2. Install and configure.
3. Store settings securely.
4. Connect to AGEX and your agents.
5. Test the connection with the MCP handshake (initialize and tools/list; no tool is called).

A missing prerequisite shows "Get Node.js"; a missing key shows "Add key". Nothing asks you to edit JSON.

Tested for real:
- Context7 ("Developer documentation"): installed and reported "Connected to Context7: 2 tools available. Ready."
- Windows-MCP: installed; its test failed with the pywin32 error above, which the wizard showed as it is.

**AUTODESK BRIDGE SETUP**
Done, with one step that stays manual. The same step list shows:
- Find Revit and AutoCAD: on this computer, AutoCAD found, Revit not found.
- Find the Autodesk AI Bridge: not installed. It has no public installer, so AGEX says it cannot install it and offers "Check again".
- Connect it to AGEX.
- Test the connection (MCP handshake).

It installs for the user only, so no administrator rights are needed. Connections and Teams now open this wizard ("Connect step by step").

**ACCOUNT USAGE**
Only what the agent reports:

| Agent | Usage / quota |
| --- | --- |
| Codex | Reported: plan, 5-hour and weekly percent used, reset times, source and time checked. Read with `codex app-server` (`account/rateLimits/read`); no model quota is used. The account e-mail in the reply is not read or stored. |
| Antigravity | "Usage not reported by this agent" (no usage command in `agy --help`). |
| Claude Code | "Usage not reported by this agent" (no non-interactive usage command). |
| Gemini CLI | "Usage not reported by this agent". |
| OpenCode | "Usage not reported by this agent" for quota. Tokens and cost appear per request when OpenCode reports them. |
| Ollama | "No quota: local models run on this computer." |

**PER-REQUEST USAGE**
Done. Each answer has "This request: N in · M out". It opens to show, per agent:
- input tokens, with the cached part;
- output tokens;
- reasoning tokens (Codex, Gemini and OpenCode, when reported);
- cost only when the agent reports it (Claude Code, OpenCode).

It also shows the total and the efficiency mode used. AGEX keeps no price list, so cost is never estimated.

Session history stores usage per agent. Sessions totals it for Today, This week, This project or all sessions. On the test day it showed "Codex: 2.9M input · 2.5M of it cached · 25k output · 7.8k reasoning, 10 requests".

**TOKEN REDUCTION**

Measured with real Codex runs, on the same project and agent (input tokens as Codex reported them):

| Request | Before (2.2 pipeline) | After (2.3) |
| --- | --- | --- |
| "hi" | 34.1k, 21 s, read 2 files | 16.8k, 15 s, read nothing |
| "what is this project?" | 88.2k, 29.7 s | 49.7k, 23 s |

AGEX's own prompts (PromptSizeTests, 250-file project):

| Prompt | Before | After |
| --- | --- | --- |
| Leader prompt | 25,286 chars | 6,646 chars (74% smaller) |
| Direct answer | 25,286 chars (went through the leader) | 725 chars |
| "hi" | 25,286 chars (went through the leader) | 397 chars |

The leader now gets a short file map instead of 300 entries with dates. Maximum quality and text-only leaders still get the full list.

Skills on Auto are filtered by relevance. For the eleven skills in the real game session, 6 are still sent: Caveman ×2, Code Review, Systematic Debugging, Test-Driven Development and Writing Plans. PDF, notebook, threat-model and two security skills are dropped.

Browser and computer tools reach only task runs that need them.

The successful game run used 679.5k input tokens, of which 634k were cached. Most of that is Codex's own tool loop while playing, which AGEX does not control.

**REAL USER TESTS**
Real Codex, on a copy of the game project:

| # | Scenario | Result |
| --- | --- | --- |
| 1 | "hi" | Pass. Chat mode, one run, instant-style reply ("Hi! What would you like to work on in hoollaaa?"), no plan. |
| 2 | "what is this project?" | Pass. Ask mode, read-only, read README.md and package.json, 23 s, 0 changes. |
| 3 | "plan how to add authentication" | Pass. Plan mode, a written plan with 5 steps and risks, 46 s, 0 changes, "Build this plan" shown. |
| 4 | "implement authentication" | Pass. Build mode (see below). |
| 5 | "open the local game and play it" | Pass after the fixes (see below). |
| 6 | localhost web app | Pass. The agent used `http://127.0.0.1:54780/index.html` served by AGEX (in #5); the local server tests cover GET, module types, `.env`, `..` and POST. |
| 7 | Ask every time | Pass (engine test): asked for file changes and before each task. |
| 8 | Trust this session | Pass (engine test): no questions; a sensitive request ("send an email to my clients") still asked and stopped when denied. |
| 9 | Antigravity settings | Pass. Effort shown with the values agy documents; Claude Code, Gemini and OpenCode show no effort; Ollama shows temperature and context. |
| 10 | MCP one-click | Pass for Context7 (installed and 2 tools tested). Windows-MCP failed its test on this computer (pywin32 file lock) and the wizard showed why. |
| 11 | Autodesk bridge | Pass as a guided flow; the bridge itself is not installable automatically. |
| 12 | Per-agent usage | Pass. Codex quota shown live; others honest "not reported". |
| 13 | Per-request usage | Pass. "This request: 17k in · 18 out" for "hi"; Sessions totals. |

Details:
- **#4:** The first run asked a real design question (email/password or identity provider), which the terminal does not answer when input is piped. With the choice written into the request:
  - 2 tasks ran;
  - 5 files changed (+230 −2), each verified;
  - 7 min 19 s, 1.16M input tokens (1.06M cached).

  That run ended "Not verified". The classifier had wrongly required a browser because the request mentioned a "sign-in screen" and the "game". This was fixed afterwards (a browser is now needed only when the request asks to open or use a page) and covered by tests. The run was not repeated.
- **#5, first try:** the browser tool was refused ("approval policy is never").
- **#5, after the fix:**
  - Codex opened the page served by AGEX in a real browser (Playwright: navigate, click, press keys, screenshots, console);
  - it played two rounds;
  - it reported a pointer-lock failure and a `WaveManager` TypeError;
  - 2 min 41 s.

Visual acceptance was run at 1366 × 768, 1600 × 900 and 1920 × 1080 (125% scaling, dark theme). It covered:
- Home (empty, chat reply, answer, plan, build result, failure with recovery);
- history, docked and floating;
- the Mode and Approvals menus;
- Settings > Approvals and permissions;
- the missing-capability dialog;
- the cloud notice;
- Agents (usage and per-agent settings);
- Sessions usage;
- the MCP wizard (review, progress, done, failed test);
- the Autodesk wizard.

Screenshots are in the session scratchpad (`s23`) and are not committed. Light and high-contrast themes were not re-checked this pass. The new controls use the existing theme resources.

**TESTS**
191 of 191 pass. 61 are new since 2.2 (RuntimeUxTests and PromptSizeTests):
- classification of the listed user phrases;
- local and private targets;
- sensitive actions;
- approval rules;
- capability routing and missing-capability fixes;
- quick chat, ask, plan and build routes;
- browser tools only in tasks, with local URL, network and Codex tool approval;
- recovery options;
- Ask every time and Trust this session;
- project folders with spaces, Unicode, similar names and a renamed folder;
- the local web server;
- model settings per agent;
- Ollama options;
- sandbox and network switches;
- Codex quota parsing;
- usage history;
- skill relevance;
- the settings migration;
- the browser tool's output folder;
- prompt sizes.

The test fake agent now retries its shared log. Parallel fakes had made two tests fail intermittently; after the fix the suite passed 8 runs in a row.

`git diff --check` is clean. Debug and Release builds: 0 warnings.

**COMMIT**
The commit on `main` that contains this report, tagged `v2.3.0`.

**REMAINING**
- Windows-MCP could not be started on this computer: uv could not install `pywin32` because of a file lock (os error 32). Computer control through Windows-MCP was therefore not run live this pass.
- Agents cannot yet be stopped mid-run before a sensitive action. AGEX confirms such actions:
  - before a request that mentions them;
  - through prompts that tell agents to ask;
  - through permissions that remove the tools.
- Dev-server projects (for example Vite) still need an agent to start the server. AGEX serves static projects itself. Starting a server inside Codex's sandbox can fail where the Node version manager's shim needs files outside the sandbox.
- Codex is the only agent that reports account quota. Claude Code, Gemini CLI, Antigravity and OpenCode have no quota command AGEX can call without a model request.
- Claude Code is not signed in on this computer, so its route was covered by the engine tests only. Antigravity, Gemini CLI and OpenCode were not used for real runs this pass.
- Checked on Windows only. The new code has no Windows-only parts except Windows-MCP.
