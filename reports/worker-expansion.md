# Worker Expansion Status

Date: 2026-09-20

## Developer capabilities

Codex, Git, Context7, Playwright MCP, project initialization, doctor, dashboard, update checks, backup/restore, Caveman skills, and existing `node_repl` remain available. Serena was later removed after MCP process/window multiplication. No duplicate developer stack was installed.

## Browser

Ready for local and approved public web work. In-app browser and approved browser extension can navigate tabs, inspect page state, follow links, fill controls, download files, and capture screenshots. Playwright MCP remains the repeatable QA path. Existing authenticated tabs were not touched.

## Computer Use

Ready for approved foreground Windows apps and GUI-only workflows. Use `@Computer` for local apps/files and `@Browser` or an approved browser extension for web work. Windows Computer Use needs active foreground desktop. Do not use it for terminal commands, credentials, CAPTCHA, or security settings.

## Research

Ready for focused research: search, inspect sources, collect URLs/evidence, structure results, compare records, and produce Markdown/CSV/XLSX/DOCX/PDF where the artifact workflow is available. Rate-limit requests and respect access controls.

## Job applications

Can find and rank roles, inspect requirements, research companies, tailor approved materials, prepare fields, and track applications. User still provides unknown identity, work authorization, visa, salary, education, employment, certification, demographic, clearance, and reference facts. Login, MFA, CAPTCHA, uploads, and final submission need user authorization or handoff as required.

## Documents and PDF

Bundled Python/Node libraries create DOCX, PDF, and structured reports. Synthetic DOCX smoke artifact created at `reports/worker-tests/worker-smoke-test.docx`. Automated `render_docx.py` visual QA is limited because LibreOffice `soffice.exe` is not installed. Existing Word installation was not changed.

## Spreadsheets

Bundled Python spreadsheet libraries create and read XLSX with typed dates, scores, formulas, filters, widths, and freeze panes. Synthetic workbook created and reopened with formula checks at `reports/worker-tests/synthetic-research.xlsx`. Native Excel recalculation/render was not claimed.

## Screenshots

Ready. Browser worker and Playwright capture actual viewport/full-page state. Smoke outputs: `reports/worker-tests/fixture-worker.png` and `reports/worker-tests/fixture-demo-final.png`.

## Video and screen recording

Browser-only Playwright recording succeeded. Output is WebM, VP8, 1280x720, 25 fps, 2.04 seconds: `reports/worker-tests/page@37170fe80d4e7adadaf43df811c4bc30.webm`. `ffmpeg` is present for local conversion, but whole-desktop recording is not enabled or required.

## GitHub

Local Git ready. Official GitHub CLI 2.101.0 installed at user scope. GitHub connector authenticated; read-only repository probe passed. Write actions remain explicit.

## Email

Gmail connector retained. Harmless read-only empty search passed. Draft and send remain explicit, user-authorized actions.

## Cloud files

`AUTH REQUIRED` for remote APIs. OneDrive-synced local workspace files work. Dropbox, Box, Google Drive, and SharePoint connectors remain unconnected. Connect only service needed for a task.

## Connected services

Retained: local Context7 MCP, local Playwright MCP, existing `node_repl`, bundled browser/computer-use, bundled document/PDF/spreadsheet skills, Gmail connector, GitHub connector, and GitHub CLI. Serena: removed / not required.

## Final verification

- `codex --version`: `codex-cli 0.155.0-alpha.9.2`.
- End-to-end fixture smoke passed navigate, fill, save, deliberate failure detection, recovery, and final save. Evidence: `reports/worker-tests/end-to-end.json`.
- Project A/B memory-boundary smoke passed with v2 superseding v1. No external memory service used.
- PowerShell parse, Node syntax, Python compile, doctor, dashboard, secret scan, and `git diff --check` passed.
- GitHub CLI 2.101.0 verified installed. GitHub connector read-only repository probe passed. CLI auth remains separate.

## Reusable worker workflows

Documented in `docs/WORKER.md` and `docs/WORKER-TESTS.md`: research, job search/application preparation, application tracking, QA, user guide, product demo, document/report generation, spreadsheet research, release check, and combined developer plus worker flow.

## Cost

Base expansion uses existing/free local capability. No purchase, paid API activation, or subscription was added. Account plan limits still apply to Codex. Optional GitHub/email/cloud integrations may require account access.

## Security

Added outcome-oriented worker boundaries: inspect before action, use synthetic tests, preserve source evidence, never invent identity facts, never extract credentials/cookies/tokens, avoid CAPTCHA and access-control bypass, and confirm consequential send/submit/upload/delete/permission actions. No secrets were added.

## Doctor

Final counts: 28 READY, 1 LIMITED, 10 WARNING, 2 OPTIONAL, 1 AUTH REQUIRED, 1 DEFERRED, 2 REMOVED, 0 ERROR. `LIMITED` is DOCX render QA because `soffice.exe` is absent. Warnings remain pre-existing Node/NVM, Docker/WSL, and PATH conditions.

## System changes

No Codex MCP configuration, account, browser profile, credential, or security setting changed. GitHub CLI installed through approved user-scope package flow. Gmail was read only. A disposable local fixture server ran for tests, then stopped. No OAuth connector connected.

## Repository changes

Added worker routing and safety docs, project worker instructions, doctor worker checks, Developer/Worker dashboard grouping, synthetic DOCX/XLSX builders, browser screenshot/video smoke capture, and test artifacts under `reports/worker-tests/`.

## Manual actions remaining

Only user-owned actions: connect cloud-file OAuth; provide unknown job-application facts; perform MFA/CAPTCHA; approve consequential upload/send/submit actions; install LibreOffice if automated DOCX render QA is required. GitHub connector writes still need explicit task scope.

## GitHub and open source reuse

Evaluated official OpenAI Codex, Microsoft Playwright, GitHub CLI, LibreOffice, OBS Studio, python-docx, Graphiti, Mem0/OpenMemory, and OmniRoute candidates. Retained existing Codex, Playwright, bundled python-docx, bundled openpyxl, browser/computer-use, Gmail read-only worker, GitHub CLI, and artifact workflows. Rejected duplicate browser tools, OBS, external memory replacement, and unverified OmniRoute routing. No third-party source copied. Licenses recorded in [docs/REUSE-DECISIONS.md](../docs/REUSE-DECISIONS.md).

GitHub discovery found active official projects: Codex releases, Playwright releases, GitHub CLI releases, LibreOffice activity, and OBS releases. OmniRoute search found multiple competing repositories, so routing stayed deferred. Reuse avoided a second browser stack, screen-recording stack, office suite, and memory/router service layer.

## Ten prompts

1. `Inspect this repository, fix the issue, run tests, and create release notes.`
2. `Run this project and test the complete booking flow on desktop and mobile with screenshots.`
3. `Create an illustrated user guide from the real UI and export DOCX and PDF.`
4. `Create a clean browser-scoped product demo using synthetic data and verify the video file.`
5. `Research these companies, preserve source URLs, compare them, and produce an XLSX.`
6. `Find jobs matching this CV and prepare a ranked tracker without submitting applications.`
7. `Open this approved application, fill only known fields, save drafts, and stop before submission.`
8. `Turn these notes into a source-backed report with a polished DOCX and PDF.`
9. `Collect these records into a typed spreadsheet with formulas, filters, and a summary.`
10. `Inspect the fixture source, change one UI behavior, test it visually, capture evidence, and write a mini release guide.`

## Final verdict

GENERAL WORKER READY WITH LIMITATIONS
