# Cavemem Phase 5 Benchmark

Date: 2026-09-19

Version: `0.2.1`

Storage: disposable local SQLite under the visualization workspace. Embedding provider: `none`. Automatic worker start: `false`. No IDE hooks installed. Synthetic data only.

## Results

| Test | Result | Evidence |
|---|---|---|
| Basic recall | PARTIAL | Exact `AlphaPay` query returned decision; natural `payment provider` query returned no hit. |
| Cross-session persistence | PASS | Data remained searchable from a new CLI process. 13 observations / 6 sessions persisted. |
| Irrelevant suppression | PASS, limited | `settlement` returned only two payment records; small keyword-only corpus. |
| Cross-project isolation | FAIL | `payments` returned both AlphaPay and BetaPay. Search API accepts query/limit only; no project/cwd filter. |
| Update behavior | PARTIAL | `API version` returned v1 and v2 together. No automatic supersession or current-fact preference. |
| Historical recall | PASS, manual | Separate history observation returned v1 and migration reason. No native temporal relation. |
| Source-vs-memory conflict | PARTIAL, policy only | Project policy says current source wins. Cavemem itself does not inspect or prioritize source files. |
| Decision-reason recall | PARTIAL | `callback` query returned decision; longer `implementation X duplicate callback` query returned no hit. |
| Retrieval quality/latency | PARTIAL | Keyword searches returned compact snippets in roughly 144–157 ms. Semantic retrieval not tested because embeddings were disabled. |

## Isolation conclusion

FAIL. Project isolation is a hard requirement. Session `cwd` is stored, but exposed search tool has no namespace/cwd argument. Memory from unrelated projects can be returned together. Do not use Cavemem as shared developer memory.

## Security test posture

- No production data.
- No API key.
- No remote embedding.
- No automatic IDE capture.
- No background worker.
- Local SQLite only.
- Additive Codex MCP smoke test passed: `search`, `timeline`, `get_observations`, and `list_sessions` exposed.

## Cleanup

Cavemem MCP entry and global package were removed after evaluation. Disposable database was removed. Existing `node_repl` and Serena config stayed intact.

## Decision

`REMOVE`

Reason: hard project-isolation failure plus weak update precedence. Recall quality alone does not justify cross-project leakage risk.
