# Phase 3 Benchmark Plan

No Serena or Context7 installation is performed in Phase 2.

## Serena benchmark

Use one representative repository with known symbols, references, and a safe targeted edit.

For each task, compare:

1. `rg` plus focused file reads plus Codex.
2. Serena semantic/symbol retrieval plus Codex.

Record:

- elapsed time;
- files opened;
- relevant and irrelevant context;
- estimated input tokens;
- symbol/reference relationship accuracy;
- edit scope and test result;
- failure recovery and permission surface.

Use at least one navigation task, one relationship task, and one targeted refactor. Prefer repeated runs over one anecdote.

## Context7 benchmark

Use libraries with known version drift and a locked project version. Compare:

1. Codex existing knowledge.
2. Official documentation search.
3. Context7 retrieval.

Record:

- version correctness;
- answer quality and citation quality;
- context size;
- latency;
- hallucination or obsolete-API rate;
- usefulness for an actual implementation task;
- credentials and network permissions required.

## Decision gate

Adopt a tool only when it improves relevant context or correctness enough to justify latency, maintenance, permissions, and tool-selection overhead. Record rejected tools and evidence. Do not enable both overlapping retrieval systems by default.
