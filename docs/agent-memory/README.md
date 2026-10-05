# Agent memory

2026-10-05 tenth gameplay/NPC checkpoint is locally accepted on baseline `e4e5ab5e`: Zombie/Meteor Head/Spiked Slime individual loot, Slimer's actual death-child construction/reset, source-born remote base health and derived heart eligibility, represented town debuff/DoT/topics, human/bot raw-slot lifetimes, ordinary no-active AI001 CheckActive and next-spawn suppression, and effective NPC difficulty in packet23. Full **1474709/1474710** tests pass in **491.735 s**, with zero failures/errors and 1 pre-existing canonical world-generation liquid skip. Release rebuild has zero warnings/errors; Windows NativeAOT/five smokes and documentation/domain/graph/diff gates pass. Immutable production/test SHA256 `6ddd077d6ac501b75c7e0fd2cb47deebd860e01904f4dcde6bc5304290ac1593`. Evidence `.cache/ordinary-status-inactive-v3-final-status.json` and `.cache/ordinary-status-inactive-v3-accepted-tests-20261005.xml`. Actual official source captures and copied regression controls accompany the new families. Graph remains15 projects/29 edges; no production dependency or world-file format changed. FullVanillaAiParity remains false; broad N1-N5, unimported individual loot/encounters, dynamic Skyblock lowTiles, incomplete open Void Bag storage, selected no-living Hive/Bee AI005, custom raw-slot aggro/history, town lethal/wet/special debuff contexts and complete NPC wire cadence remain open. The Linux reference assembly ran on Windows CoreCLR; genuine Windows x86 Framework arithmetic was captured separately for Slimer. Neither is Linux NativeAOT acceptance. Commit/push follows local acceptance without waiting for GitHub CI, as explicitly requested. Preserve all earlier accepted artifacts. Next cache-only plans: `.cache/npc-specific-eleventh-plan.md` (43 Zombie/Skeleton tables, 32 currently admitted actors, 11 pure-table-only) and `.cache/npc-eleventh-raw-flight-plan.md` (existing AI002 raw targeting plus a distinct Bee AI005 whole outer/ascending Hive-child phase). They are planning evidence, not integrated acceptance.

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
