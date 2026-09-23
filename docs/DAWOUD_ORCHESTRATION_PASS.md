# DAWOUD real acceptance: bounded follow-up pass

Real leader: Codex. Initial decomposition: 5 tasks chosen without a count target: behavior review, independent documentation, implementation, dependent verification, final review. Dependencies and intended file ownership came from the leader.

## Execution evidence (local time, UTC+03:00)

| Task | Executor | Actual PID | Agent start | Agent end |
|---|---|---:|---|---|
| docs-01 | Antigravity | 31336 | 04:47:34.9518829 | 04:48:17.7668102 |
| implement-01 | Antigravity | 34700 | 04:47:34.9521507 | 04:48:11.1464691 |

Agent-lifetime overlap: min(end)-max(start) = 36.1943184 seconds. These are actual worker PIDs; the timestamps include launcher startup. Both actual AGY processes were observed alive together. File ownership was README.md versus Convert-Names.ps1. The verification task started at 04:48:17.8532698, after both prerequisites completed.

## Important failed acceptance condition

AGY returned successful final results claiming README.md and Convert-Names.ps1 creation. Neither file existed in the disposable project. Verification had not completed when the 210-second acceptance deadline expired. The request was cancelled, queued final review did not start, and recorded task-owned processes were absent afterward. Root acceptance is FAIL, not PASS. The fixture contains only the harness evidence file, which does not count as agent implementation.

Messages were produced by real Codex and AGY, but dedicated mailbox delivery did not run before cancellation. Real message delivery remains unproven. A separate real Codex reconciliation checkpoint was launched against the missing-file observation; its output is recorded locally when completed.

Native terminal cancellation was exercised separately: real Codex PID 34552 was RUNNING; sending Ctrl+C entered cancellation, stopped the owned process, produced GOAL: CANCELLED, accepted :status afterward, and exited through :quit. No recorded task-owned process remained. This covers a real leader cancellation and a deadline cancellation during AGY execution, not a matrix of simultaneous-worker Ctrl+C cases.

## Implementation

The scheduler now launches bounded isolated runspaces using the existing executor functions. Unknown ownership, overlapping files, directory overlaps and wildcard ownership serialize. Global AGY slots still use the existing worker lock. One Codex worker and at most the configured total worker limit may run. Cancellation unwinds owned workers in finally blocks.

Malformed leader plans receive one repair attempt. Mailbox entries have identities and delivery status; pending messages enter recipient assignments or dedicated reply turns. Dedicated communication is bounded. Answers do not trigger recursive chats. New component checks cover path conflicts, message delivery/statuses and schema safety; real mailbox round trips remain unverified.

Git snapshots are refreshed on completion and explicit commands; non-Git projects retain filesystem observations. Diff excerpts are bounded. Bracketed paste reports line and character counts. Native large-paste and updated changes rendering are not validated in this pass.

The canonical AGY stream launcher, result protocol, authentication and system configuration are unchanged. The process-wide PATH rewrite was removed from concurrent callers to avoid a shared environment mutation. No dependencies were installed.

Focused parser checks and git diff --check pass. No original component matrix was rerun. The real acceptance harness is scripts/dawoud-real-acceptance.ps1. Its observed failure must be resolved before claiming complete orchestration.

Real reconciliation completed: Codex inspected the fixture, confirmed all deliverables missing, returned CONTINUE, and proposed four repair tasks without a count target. The plan is preserved in docs/dawoud-real-reconciliation.json. It was not executed within this pass.
