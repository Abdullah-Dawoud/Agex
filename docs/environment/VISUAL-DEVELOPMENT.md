# Visual Development

## Current capability

Codex bundled browser/computer-use provides interactive navigation, clicks, form input, DOM inspection, screenshots, and browser console workflows. Use it first for exploratory work.

Playwright MCP is configured locally and supports Node.js 18+, accessibility snapshots, navigation, clicks, form input, screenshots, console inspection, and repeatable MCP workflows. The reusable fixture is `fixtures/ui-test/`.

`fixtures/ui-test/` contains a synthetic static fixture and server source for future local browser verification. It is retained as a reusable fixture; it contains no user data.

## Workflow

1. Start disposable/local app.
2. Open URL with bundled browser control.
3. Inspect visible content and accessibility tree.
4. Exercise click and form paths.
5. Inspect console and network errors.
6. Capture screenshot at useful viewport sizes: 390x844, 768x1024, 1440x900.
7. Fix one issue.
8. Repeat same interaction.
9. Save screenshots outside source unless they are intentional visual fixtures.

Never use production data for first browser verification. Do not paste secrets into forms or URLs.
