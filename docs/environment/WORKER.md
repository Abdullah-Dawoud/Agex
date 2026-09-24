# General Worker

## Purpose

This setup keeps developer mode and general worker mode in one environment. Codex uses existing local files, shell tools, bundled browser/computer control, Playwright MCP, document skills, and task-specific connected services when explicitly enabled.

## Capability routing

| Need | First choice | Fallback | Boundary |
|---|---|---|---|
| Code, tests, Git | local files, `rg`, Git | shell tools | project scope |
| Exploratory web work | Codex in-app browser or approved browser extension | Playwright MCP | site approval and login boundaries |
| Repeatable browser QA | Playwright MCP | bundled browser | synthetic/local data first |
| Desktop GUI | Computer Use | manual handoff | approved app, foreground Windows session |
| Documents and PDF | bundled document/PDF skills | Markdown or plain text | render and inspect generated DOCX/PDF |
| Spreadsheet | bundled spreadsheet skill | CSV | keep sources and formulas reviewable |
| Screenshots | browser or Playwright screenshot | Computer Use capture | explicit task output folder |
| Browser demo video | Playwright video when supported | local `ffmpeg` post-processing | browser-scoped capture only |
| GitHub | plain Git locally | one approved GitHub integration | authentication remains user-authorized |
| Email | Gmail connector for read-only search and explicit drafts | browser only when explicitly requested | no send, cookie, or credential extraction |
| Cloud files | OneDrive-synced local workspace; official connector per task | browser only when explicitly requested | remote auth and uploads remain user-owned |

Do not install overlapping browser controllers, office suites, email clients, cloud clients, scrapers, or remote-control software without a concrete gap and review.

## Worker execution loop

1. Define requested outcome and external side effects.
2. Inspect current source, files, app state, and available tools.
3. Choose smallest existing capability.
4. Execute read, draft, and reversible prepare steps.
5. Verify visible state, files, formulas, screenshots, or document rendering.
6. Recover ordinary failures once, then use a safe alternative.
7. Pause for login, MFA, CAPTCHA, missing identity facts, payment, legal consent, destructive action, or consequential submission.
8. Report completed work, evidence, limits, and manual actions.

## Research workflow

Search with focused queries. Visit relevant sources. Capture URL, source date, evidence, and uncertainty. Rate-limit requests. Do not scrape around access controls. Save structured results as Markdown, CSV, or workbook when useful.

## Job workflow

Find and rank relevant roles, inspect requirements, research companies, tailor approved CV and cover-letter material, prepare application fields, and record status. Never invent qualifications, dates, salary, authorization, visa status, demographic data, certifications, clearance, references, or identity. Leave unknown fields for the user. Never solve CAPTCHA or submit a consequential application without required confirmation.

## User guide workflow

Run the local or approved application. Inspect real UI state. Exercise major flows. Capture clean viewport, full-page, element, desktop, or mobile screenshots. Write steps tied to observed controls. Render final DOCX/PDF and inspect pages before delivery. Store outputs under an explicit task folder.

## Product demo workflow

Use synthetic data. Select one strong flow. Capture browser-scoped visuals. Prefer Playwright video for browser-only demos. Use `ffmpeg` only for local conversion or trimming when needed. Verify output file, duration, and playback metadata. Never capture unrelated desktop content.

## External action boundary

Read is usually safe. Draft and prepare are reversible. Send, submit, publish, upload, delete, permission changes, and account changes have external effect. Prepare everything possible first, then ask for the smallest required confirmation.

## Daily prompt patterns

```text
Research these companies and produce a source-backed spreadsheet.
Run this project and create an illustrated user guide with screenshots.
Test the complete booking flow and produce a QA report with screenshots.
Create a clean browser-scoped product demo using synthetic data.
Find jobs matching this CV and prepare applications without submitting them.
Inspect this repository, fix the issue, test it visually, and create release notes.
```

## Retained integrations

Gmail connector is retained for read-only worker tasks. GitHub connector is authenticated for repository reads; write actions remain explicit. Cloud-drive connector is not connected; OneDrive-synced local files remain available. Add one official connector only when task value, permissions, authentication step, and removal path are clear. Authentication is user-owned and never stored in this repository.
