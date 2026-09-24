# Persistent Memory Decision

Review date: 2026-09-20

Decision: retain project documents, AGENTS.md, and Git history. Retain no external memory engine or Serena project state.

Synthetic isolation smoke passed:

- Project A returned only its Alpha decision.
- Project B returned only its Beta decision.
- Project A recorded `v2 supersedes v1`.
- Test used disposable files and no external memory service.

Candidate review:

- Cavemem: removed. Earlier live test failed project isolation and version supersession retrieval.
- Graphiti: strong temporal/provenance model, but adds graph database, model or embedding credentials, service lifecycle, and Docker-style operations. Defer.
- Mem0/OpenMemory: active Apache-2.0 ecosystem, but adds vector, service, provider, and namespace controls not needed by current setup. Defer.
- `codex-mem` candidates: insufficient maintenance/adoption evidence for a high-trust default. Do not install.

Rule: memory may explain why/history. Current source, tests, schema, configuration, official documentation, and Git decide truth.
