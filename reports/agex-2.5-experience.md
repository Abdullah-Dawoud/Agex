# AGEX 2.5 experience pass — early checkpoint (superseded by agex-2.5-local-test.md)

This file records the earlier checkpoint. See `agex-2.5-local-test.md` for the current acceptance status. The periodic screenshot feature described below has since been removed.

Date: 2026-09-27. Branch: `codex/agex-2.5-experience`. This is a development report, not a release declaration. `VERSION` remains 2.4.0 until the acceptance list is complete. No package has been published.

## Status

| Area | Status | Evidence or remaining work |
| --- | --- | --- |
| Home simplification | In progress | Team button wall removed; composer advanced controls collapsed. Project selection is still required before any request. |
| First-run personalization | Partial | First run asks about work interests and selects a Team. Several interests can be checked, but only the first matching Team becomes active. |
| Team quality | In progress | Removed misleading Figma and office desktop requirements. Existing eleven Team definitions and prior profession research remain; current audit of every workflow is incomplete. |
| Team auto-setup | Partial | One action installs available required/recommended free skills, activates Team and pins those skills. Account sign-in, dependency installation and connection tests still require separate actions. |
| Team skill packs | Partial | Required/recommended/optional levels already exist. Setup now excludes optional skills. Pack curation and all-Team task validation remain. |
| Web-first connections | Partial | Figma, Notion and Linear show web access before desktop requirements. Slack and Google services offer web access. Web sign-in does not grant agent access. |
| Account connections | Pending | Existing token and CLI sign-in flows remain. Provider-owned OAuth, verified account readiness and refresh handling are not implemented. |
| MCP auto-connect | Partial | Existing reviewed catalog and setup wizard remain. Hosted provider OAuth is not implemented. Figma's official remote server restricts client access; AGEX uses its existing token-based Framelink option for agents. |
| In-app browser | Partial | User-opened HTTPS pages use the embedded web view. Local preview keeps its file and loopback restrictions. Web providers may reject embedded sign-in; external open remains available. |
| Browser tabs | Partial | Multiple user-opened tabs, new tab and close actions added. Agent browser sessions are not mirrored into these tabs. |
| Terminal tabs | Partial | Users can open multiple project-scoped command tabs with live stdout/stderr, exit state, stop, restart and clear. Agent commands are still shown in Activity; their output is not mirrored into these tabs. |
| Artifact view | Partial | All existing file artifacts appear under Outputs. PDF files use the embedded preview where supported; image/text/HTML preview already existed. DOCX/XLSX and video still need in-app viewers. |
| Live view modes | Superseded | The screenshot implementation described in this checkpoint was removed in the current pass; see the local test report. |
| Agent workspace | Partial | Activity, Changes, Files, Preview, Browser, Terminal, Computer and Connections tabs exist. Agent-driven browser and terminal tabs remain. |
| Performance | Not measured | Terminal output renders in 100 ms batches with a bounded queue. Existing refresh debounce is 300 ms. No execution latency or rendering benchmark had been run at this checkpoint. |

## Real flows tested

- Connection resolution: Figma web route without desktop, Notion web route, office file workflow without desktop.
- Workspace URL policy: HTTPS web tab allowed; HTTP and local files blocked in web tabs; existing local preview policy retained.
- Settings export and import: Detailed live view round trip, secrets excluded.
- Desktop project build: passed with zero warnings and zero errors.
- Terminal diagnostic command hiding: passed. Interactive terminal behavior has not been visually tested.

Fresh onboarding, actual account sign-in, MCP installation, browser navigation, interactive terminal output, takeover, visual sizes/themes, and agent-driven live activity have not been exercised end to end.

## Tests

All 207 tests excluding `RegressionTests.Text_only_team_gets_a_direct_answer_and_hidden_thinking_is_removed` passed. That excluded test fails in this sandbox when `FakeOllama` calls `HttpListener.Start()` (`The handle is invalid.`). Focused connection/workspace/storage tests: 48 passed. Visual and performance acceptance remain open.

## Commit

Development checkpoint: `1b8bba7` on `codex/agex-2.5-experience`. Release commit waits for acceptance.

## Remaining

Complete provider-owned OAuth and test connection checks; unify connection routing for remaining services; implement agent-driven browser and terminal views with bounded, redacted event streams; finish artifacts and takeover; support chat without a project; run visual and performance acceptance; finish all-Team professional audit. Bump `VERSION` to 2.5.0 only after those gates pass.

## Architecture and security decisions

- Browser sign-in and agent access have distinct states. Opening a website never marks its MCP connection ready.
- Embedded web tabs accept HTTPS only. File preview uses the existing local-only policy.
- Team setup installs only skills already in AGEX's catalog and shows their permissions before installation. It does not alter machine-wide tools or credentials.
- Terminal commands run only after a user starts them. Command arguments are omitted from persistent process diagnostics. Output stays in memory and is redacted before display.
- Figma's official remote MCP needs an approved client; current AGEX agent integration remains the existing reviewed Framelink option. See [Figma MCP](https://developers.figma.com/docs/figma-mcp-server/).
- Google Workspace MCP servers are in [developer preview](https://developers.google.com/workspace/preview); Slack, Notion and Linear have official hosted servers, but AGEX does not yet implement their authorization flows. See [Slack](https://docs.slack.dev/ai/slack-mcp-server/), [Notion](https://www.notion.com/help/notion-mcp), and [Linear](https://linear.app/docs/mcp).
