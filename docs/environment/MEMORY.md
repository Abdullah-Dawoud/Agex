# Memory Policy

## Authority order

1. Current source and tests.
2. Scoped `AGENTS.md` instructions.
3. Current project documentation.
4. Verified search and code-intelligence results.
5. Verified persistent memory.

Memory never silently overrides current code, schema, tests, or official documentation.

## Operating model

```text
task -> retrieve small relevant set -> verify critical claims against source -> act
```

Codex and `rg` answer `where/how is this implemented now?`; Git answers `what changed?`; project docs answer `what is intended?`; memory answers `why/what was learned?`. Memory is advisory, never an automatic prompt-wide dump.

Global capability may be shared. Knowledge is project-scoped: repository docs, scoped `AGENTS.md`, and any future memory namespace must be keyed to the project identity. No cross-project recall without an explicit global scope.

## Memory classes

- Architecture: stable component relationships.
- Decision: chosen/rejected approaches and reasons.
- Business rule: domain invariants.
- Integration: provider/API behavior and verification steps.
- Bug/lesson: repeatable failure and prevention.
- Terminology: project vocabulary.
- Developer preference: durable workflow preference.
- Temporary context: active investigation; expires or is deleted.

## Good memory

Store durable, reusable context such as architecture decisions, project conventions, recurring bugs, important relationships, developer preferences, and stable workflows. Include date, scope, confidence, and source when practical.

## Bad memory

Do not store secrets, credentials, transient task state, unverified guesses, copied source files, or instructions that conflict with current repository evidence.

Include scope, source/provenance, confidence, last verified date, and status where practical. Status values: `CURRENT`, `NEEDS VERIFICATION`, `SUPERSEDED`, `HISTORICAL`, `TEMPORARY`.

## Bad memory

Do not store secrets, credentials, transient task chatter, copied source files, unverified guesses, or facts that current source contradicts.

## Current status

Caveman skills are installed. Codex exposes optional local memories, but its storage and retrieval behavior are implementation details; do not edit its databases. Cavemem is not installed or callable on this host. No second memory system was added.

Phase 5 tested Cavemem 0.2.1. Recall and compact retrieval worked, but its MCP search API had no project filter: Project A and Project B payment memories returned together. Update queries also returned v1 and v2 together. Cavemem is therefore `REMOVE` for this environment. Graphiti and OpenMemory/Mem0 remain research-only.

## Benchmark gate

1. Save one harmless architectural decision with project scope and provenance.
2. Start a fresh agent/session if practical.
3. Ask a related question without repeating the decision.
4. Measure recall, precision/noise, latency, context size, update/history behavior, isolation, and source conflict behavior.
5. Remove or isolate the test record.

Critical domains (auth, authorization/RLS, payments, financial logic, migrations, production config, secrets, destructive work) require source/schema/config verification even when memory retrieval is correct.

Graphiti requires a measured gap that justifies database, model, embedding, and operational complexity. Important durable memories should graduate into project docs or decision records; Git remains the exact change history.
