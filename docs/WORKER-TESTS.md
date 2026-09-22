# Worker Smoke Tests

Use synthetic data and the disposable fixture. These checks prove capability paths without touching accounts.

## Browser

Start `fixtures/ui-test/server.js`. Open `http://127.0.0.1:4173/` with the Codex in-app browser. Inspect accessibility content, fill `Name`, save, verify `Saved <name>`, inspect console, and capture a screenshot. Playwright MCP remains the repeatable QA path.

## Document

Create a short synthetic report with a title, source note, result table, and screenshot. Save DOCX and optionally PDF under a task output folder. Render DOCX pages to PNG and inspect every page before delivery.

## Spreadsheet

Create synthetic company rows with `company`, `role`, `fit score`, `status`, and `source URL`. Keep dates and scores typed. Recalculate, scan formulas, render the affected sheet, and export XLSX.

## User guide

Exercise fixture save flow. Capture the actual page. Write numbered steps that match the observed label and button. Include screenshot and observed result. Render guide DOCX/PDF and inspect it.

## Product demo

Use fixture save flow with synthetic name. Prefer Playwright browser video for browser-only capture. If video is unavailable, report screenshot-only limitation. Verify file metadata before claiming video success.

## Combined developer and worker flow

Inspect fixture source, make one small intentional UI change, run the fixture, exercise the changed flow, capture evidence, and produce release notes. Keep change reversible and validate with `git diff --check`.

Current end-to-end smoke also exercises navigate, fill, save, deliberate failure detection, recovery, and final save. Evidence: `reports/worker-tests/end-to-end.json`.

## Memory boundary

Run `scripts/memory-isolation-smoke.ps1` for disposable Project A/Project B scope and v1/v2 supersession checks. This proves file-boundary discipline only; it does not enable an external memory engine.
