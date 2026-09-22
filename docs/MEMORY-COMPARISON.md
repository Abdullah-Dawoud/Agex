# Persistent Memory Comparison

Date: 2026-09-19

## Decision

No external memory service is retained. Cavemem 0.2.1 completed a disposable benchmark, then failed the hard project-isolation gate. Keep Codex local memory as an optional host capability, but do not add another always-on memory system.

## Evidence summary

| Candidate | Fit | Main reason not installed now |
|---|---|---|
| Codex local memory | Existing, low setup | Scope/retrieval/storage are product-managed and not a portable project contract |
| Cavemem | Tested, then rejected | Search API has no project filter; update retrieval returns conflicting versions |
| Graphiti | Strongest temporal/provenance model | Docker plus graph database, LLM, embeddings, and service maintenance |
| OpenMemory/Mem0 | Broad memory API/MCP ecosystem | Vector/service/API-key choices and more operational surface than current need |

Sources: [Cavemem](https://github.com/JuliusBrussee/cavemem), [Graphiti](https://github.com/getzep/graphiti), [Graphiti MCP](https://help.getzep.com/graphiti/core-concepts/mcp-server), [Mem0](https://github.com/mem0ai/mem0), [OpenMemory](https://github.com/mem0ai/openmemory), and [OpenAI memory documentation](https://learn.chatgpt.com/docs/customization/memories).

## Weighted matrix

Ratings are relative to this setup, not feature-count scores: source-first behavior, Windows, project isolation, low context cost, local privacy, reversible operations, and low maintenance matter most.

| Criterion | Codex local memory | Cavemem | Graphiti | OpenMemory/Mem0 |
|---|---|---|---|---|
| Codex compatibility | native | advertised Codex hooks/MCP | MCP; integration setup | MCP/API; integration setup |
| Windows compatibility | native host | likely Node/SQLite path; verify | Docker/DB burden | Docker/Python/vector burden |
| Cross-session persistence | yes, product-managed | local SQLite/index | graph store | vector/store |
| Project isolation | behavior must be treated as opaque | namespace must be tested | group/namespace design | user/project filters must be tested |
| Global memory | product-managed | possible global + project scopes | possible, explicit model needed | possible, explicit filters needed |
| Semantic retrieval | product-managed | local vector + hybrid search | semantic + graph + keyword | semantic/vector; optional graph features |
| Keyword retrieval | undocumented here | SQLite FTS5 | hybrid search | implementation/config dependent |
| Entity relationships | undocumented here | limited/unknown | strong | available through memory model; verify |
| Temporal knowledge | undocumented here | update/history must be tested | first-class validity windows | update/history supported; verify conflict behavior |
| Provenance | undocumented here | record manually/test | episode provenance | metadata/API support; verify |
| Stale handling | opaque | test updates/deletes | superseding/validity model | update/delete APIs; conflict behavior needs test |
| Automatic capture | product-managed | hooks/worker | application-controlled episodes | add/search API; capture policy is ours |
| Explicit save/search | product-managed | MCP tools | MCP/API | MCP/API |
| Context efficiency | unknown | designed for compressed retrieval | potentially high graph context | depends on filters/ranking |
| Local-first | host-local | local by default | self-host possible, heavy | self-host possible, heavier |
| Privacy | Codex-managed host state | local unless remote embeddings selected | model/embedding data may leave host | hosted/API key or self-host boundary |
| Setup complexity | none | low after healthy Node | high | medium-high |
| Infrastructure | Codex host | Node, SQLite, optional local worker | Docker, graph DB, model/embedding | service/store, model/embedding |
| LLM requirements | product-managed | optional/implementation dependent | LLM + embeddings | model + embeddings depending mode |
| Cost | account/product policy | local software; optional provider cost | provider + infrastructure cost | hosted/provider or self-host cost |
| Backup/export | product-managed | local data; verify CLI/export | database backup | API/store backup |
| Removal | product settings/host state | uninstall + remove local data after backup | stop/remove stack + backup | stop/remove service/store + backup |
| Maturity for this use | available but opaque | promising, needs benchmark | mature concept, overbuilt here | broad, overbuilt here |

## Taxonomy and record shape

Use only durable, reusable memories:

`type`, `statement`, `scope`, `source`, `last_verified`, `confidence`, `status`, and optional `supersedes`/`expires`.

Types: architecture, decision, business rule, integration, bug/lesson, terminology, developer preference, temporary context. Temporary context must expire or be easy to delete. Stable high-value entries should graduate to `ARCHITECTURE.md`, decision records, security docs, or other project documentation.

## Retrieval contract

1. Identify task domain.
2. Retrieve a small top-ranked set.
3. Verify critical or stale claims against current source/tests/schema/configuration.
4. Use memory to explain why/history, Codex and `rg` to locate current code, and Git to explain exact changes.
5. Record corrections as superseding or historical; do not return contradictory facts as equivalent current truth.

## Candidate notes

### Cavemem

Official project describes cross-agent persistent memory, local SQLite, compressed observations, hooks, explicit MCP search/timeline tools, and hybrid FTS5/vector retrieval. Test results: basic recall, cross-session persistence, compact keyword retrieval, history, and decision reasoning worked. Project isolation failed because search accepts only query/limit and returned both projects. API v1/v2 also remained jointly retrievable. Candidate rejected for this environment.

### Graphiti

Graphiti models temporal knowledge graphs with provenance, validity windows, hybrid search, and graph traversal. Its MCP server is useful for entity/history-heavy work, but the normal deployment brings Docker plus Neo4j/FalkorDB-style storage, model calls, embeddings, and service operations. That is not justified by a measured gap yet.

### OpenMemory/Mem0

Mem0 provides an open-source memory engine with library/self-hosted options and MCP operations for add/search/list/update/delete. OpenMemory focuses on portable memory across coding harnesses. It may be a middle ground, but the current setup has no need for its service, vector, credential, or hosted-data boundary. Test only if Cavemem fails the practical requirements.

### Other alternatives

No additional candidate qualified for this phase. Adding another catalog would not improve the decision: all surviving options still require the same proof of project isolation, source-conflict behavior, and context efficiency.

## Code inspection, Git, and docs

Codex and `rg` answer “where/how is it implemented now?” Memory answers “why/what was learned?” Git answers “what changed?” Documentation answers “what is intended?” None is a substitute for current source. Context7 remains `DEFERRED` and is not part of memory work.
