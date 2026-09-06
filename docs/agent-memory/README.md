# Agent memory

This directory is the repository-local working memory for coding agents. It exists to avoid rebuilding the same architecture map and re-verifying the same source-backed facts at the start of every session.

It is **not** a second roadmap and it is **not** a substitute for source-of-truth verification. When this memory disagrees with production code, tests, the official TerrariaServer 1.4.5.8 decompile, Multiplicity 3.0.x, or `docs/roadmap.md`, the higher-quality source wins and this memory must be corrected in the same pass.

## Required reading order before non-trivial work

1. `AGENTS.md`.
2. `docs/agent-memory/work-state.md` for the last checkpoint and in-progress work.
3. `docs/agent-memory/production-graph.md` for dependency/ownership impact.
4. `docs/agent-memory/verified-vanilla.md` when touching vanilla behavior or protocol semantics.
5. Relevant sections of `docs/roadmap.md` and the subsystem docs.

Do not reread the entire repository unless the requested change crosses an unmapped boundary or the memory is visibly stale.

## Update rules

Update this directory whenever a substantial pass changes one of these facts:

- project/reference graph or runtime ownership;
- the authoritative call path of a major subsystem;
- a source-backed vanilla constant, ordering rule, packet semantic or geometry rule;
- the current clean checkpoint or active unfinished pass;
- a discovered regression/risk that the next session must not rediscover;
- a decision that would otherwise tempt a later agent to reintroduce a removed fallback or legacy path.

Keep durable facts in `production-graph.md` and `verified-vanilla.md`. Keep temporary progress, current risks and the exact resume point in `work-state.md`.

Before creating a clean checkpoint ZIP, ensure `work-state.md` names that checkpoint and contains no stale "in progress" claim. If work is interrupted before a checkpoint, record the uncheckpointed state explicitly.

Do not paste decompiled Terraria code here. Record only concise behavioral facts plus the type/method used as evidence.
