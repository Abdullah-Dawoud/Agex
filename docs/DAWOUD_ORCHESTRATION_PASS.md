# Bounded orchestration pass

The selected leader now receives the complete original prompt and returns a JSON plan. There is no task count quota. The legacy regex splitter is a compatibility wrapper that preserves the entire prompt. Explicit leader assignments bypass heuristic rerouting only for those assignments.

The scheduler executes dependency-ready work sequentially. This preserves file conflict safety while parallel execution is deferred. Six reconciliation rounds bound repeated planning; this is not a limit on tasks. Completion requires an explicit leader verification statement and no unresolved tasks. Worker success alone does not complete the goal. Failed or cyclic dependencies remain blockers. Splitting, merging, reassigning existing tasks, superseding failed work, and continuous replanning during a running executor are not implemented.

Operational messages are parsed from executor JSON results. DAWOUD passes recipient messages into subsequent assignments and passes all messages to the leader for follow-up planning. History is bounded to 64 messages, content to 2,000 characters, with duplicate suppression. This is deferred communication, not live bidirectional IPC. Invalid or missing structured output cannot claim completion. Raw results remain in request-local memory; visible message content uses existing sanitization.

The AGY launcher and DAWOUD_AGY_RESULT_V1 contract remain unchanged. Dispatch accepts an optional UTF-8 task file to avoid Windows command-line length limits. Temporary input is removed in the executor's finally block. Authentication, ACLs, installed runtimes, and user configuration are unchanged. Codex retains its read-only sandbox.

The existing bounded filesystem event watcher powers changes visibility. These are observed filesystem events, not a verified net Git diff or attribution to an executor. `:changes` shows observations; `:diff` requests Git statistics only on demand. `:chat` shows operational messages. The outer dashboard border is removed. Paste feedback is implemented for bracketed paste, but native terminal behavior is not validated in this pass.

## Validation

- Graph component tests: complete original prompt, dependency ordering, follow-up creation, messages in both directions, duplicate suppression, rejection of unsupported completion, unknown dependency rejection, no task truncation, and cancellation before dispatch.
- Existing UI component tests, adapted to the borderless layout and explicit goal status.
- PowerShell parser checks and `git diff --check`.
- Real Codex/AGY acceptance, real Ctrl+C, native paste, process orphan inspection, and parallel execution: NOT TESTED.

## Disposable-project acceptance request

Run this in a new disposable directory with no valuable files:

> Build a dependency-free Python utility that normalizes newline-delimited names: trim whitespace, discard empty lines, deduplicate case-insensitively while preserving first spelling and order. Provide a CLI accepting input/output paths containing spaces. Add usage documentation independently of implementation. Add unittest coverage for Unicode, empty input, duplicate case variants, and output directory errors. Execute the tests and CLI against a fixture, inspect actual outputs, and repair any failures. Use Codex and Antigravity for implementation and review according to their capabilities. Exchange an explicit review request and answer through DAWOUD. The selected leader chooses task boundaries and dependencies without a task count target. Finish only when the original requirements are verified, or report the actual blocker.

Acceptance execution status: NOT RUN. Leader-selected task count: NOT OBSERVED. The mocked component task count is not an acceptance result.
