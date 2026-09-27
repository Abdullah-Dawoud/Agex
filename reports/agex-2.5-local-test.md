# AGEX 2.5 — quality and UX recovery report

Date: 2026-09-27. Version: 2.5.0, published as a pre-release after the checks below. Earlier checkpoints of this branch are in `agex-2.5-experience.md`.

## Defects found in the 2.5 preview and fixed at the cause

| Defect | Cause | Fix |
| --- | --- | --- |
| "hi" failed when Antigravity failed | One automatic fallback at most; no fallback after a timeout or after the agent had made progress, even for read-only replies; an Antigravity `error` event did not end the run, so it waited for the timeout | Read-only replies try every other ready agent after any failure; recently failed agents are not asked first; the error event ends the run; the reply is signed by the agent that gave it |
| Simple chat carried skills and tools | Skills without a relevance rule were always sent; Team pins were always candidates | Chat gets nothing; more skills have task rules; skills that only matter when work is done go to Build only |
| Team skills too restrictive | The picker listed only switched-on skills; "choose for this request" replaced the suggestions entirely | Suggestions plus per-request additions and removals; the picker shows suggestions, Team recommendations and every installed skill with search |
| Home stacked and crowded | Setup card, Team card, five composer controls, "More options", timeline and task board in the chat | One reading column; composer with **+** and contextual chips; details folded |
| Oversized side workspace | Eight tabs always visible, open by default at up to 42% of the width | Rail with contextual buttons; compact, wide, overlay and pop-out; collapsed by default (settings schema 6) |
| "Try another tool" for an agent failure | Recovery labels ignored the kind of failure | Retry, Use another agent, Check agents for agent failures; capability fixes keep their own labels |
| Connections shown as Connected but unusable | Connected meant "installed and has keys"; only Codex and Claude Code receive tools per request | Connected only after **Test with agent**; new states Set up (not tested), Not available to your agents, Connection broken |
| Connections needed a project | Requests without a project dropped every need and every skill and server | Only project-bound needs are dropped; system connections stay available |
| Browser task "blocked" | The planner has no browser by design and reported the work as blocked | The work becomes one task for an agent with the browser tool |
| "Do not change any files" treated as a change request; "example.com" treated as a local page | Classifier matched verbs inside a negation and "the page" wording | Negations and bare domains are handled |
| Identical rewrites listed as modified | File dates compared without the text | Same text is not a change |
| Crash on start with the panel left open | Overlay width computed before the first layout (negative) | Guarded |
| Terminal tabs merged across requests | Command ids repeat between runs | Session and task are part of the id; commands are marked finished when the request ends |

## Real flows (Windows 10, 125% scaling, isolated data folder, real agents)

| Flow | Result |
| --- | --- |
| A. "hi" without a project | Antigravity answered in about 10 s; no skills, planning or project scan |
| B. Antigravity failing (stand-in that returns nothing), "hello" | Codex answered in 12 s; the reply is marked "switched agent automatically" |
| C. Software Builder active, "hi" | Plain reply; the skills chip stays hidden |
| D. Skill picker | Suggested for this message (3), Recommended for Software Builder (2 + install link), All skills |
| E, F. Add PDF Documents (not in the Team), remove Test-Driven Development | Chip shows 3 skills, "Your choice"; used by the request |
| G. "fix the bug: the Add one button adds two" | Fixed by Antigravity; Changes 1 file +1 −1; Undo |
| H. "open the counter page in a real browser, click Add one twice" | Codex with Playwright: "The number shown after two clicks is 2"; Browser 1 on the rail, page viewable |
| I. "Run the command: git log --oneline -3" | Terminal 2 on the rail: command, folder, exit 0, output |
| J. Resize and collapse | Rail at 1366 x 768 (overlay panel), docked compact and wide at 1600 and 1920 |
| Test with agent: Playwright with Codex | "Codex called browser_tabs successfully"; card changes from Set up, not tested to Connected |
| First run | Four steps; interests in plain words; suggested Teams at the end |

Performance: 4 identical fake-agent requests took 5.7–6.2 s with the workspace hidden and 5.7–5.8 s with it open on Terminal. Watching does not slow execution.

Visual checks: 1366 x 768, 1600 x 900 and 1920 x 1080 at 125% scaling; dark, light and high contrast; empty Home, Team active, conversation, running request, failed request, 12 changed files, browser and terminal sessions, collapsed, compact and wide panel.

## Tests

247 tests pass (37 new: fallback, Team + hi, task-aware and user-chosen skills, projectless chat and connections, recovery labels, contextual surfaces, command display, the planner browser case, and connection readiness: configured vs connected, agent without tools, adapter failure, read-only call failure, server crash, missing key, per-agent availability, incompatible Team agents, restart and reconnect).

## Not done

- Provider OAuth for hosted services (Linear, Slack, Notion, Figma) is not implemented; keys and CLI sign-ins remain.
- Browser tabs mirror the pages agents open; they are a separate browser, so Take control pauses AGEX rather than taking over the agent's own browser.
- AutoCAD and Revit were not seen connected live (AutoCAD 2027 fails to start on the test machine; Revit is not installed).
