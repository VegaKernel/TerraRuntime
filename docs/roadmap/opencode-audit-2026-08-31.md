# OpenCode change audit — 2026-08-31

This audit covers the high-volume `main` change window from `73e49550331bbac3dfa4468b732e5f946ec4e958` through `f6fffb4ae59e4c8de68adc1dcac8fed84cc10e04` and checks the resulting runtime against the repository ownership, fail-closed, evidence and roadmap rules.

The review treats compilation and self-round-trip tests as insufficient evidence for behavior-sensitive authority or vanilla-parity claims. The relevant architecture remains single-writer authoritative gameplay, untrusted client input, protocol/gameplay separation, source-pinned TerrariaServer 1.4.5.8 behavior and explicit incomplete-parity ledgers.

## Confirmed corrections

### Client packet-17 tile placement authority

The sparse item placement catalog had been relaxed so an unknown held item could authorize an arbitrary valid simple tile. That made the packet claim stronger than the imported item-to-tile facts. The runtime now fails unknown mappings closed; only explicit source-backed placement mappings can authorize a client tile placement. Regression tests pin Dirt, Stone and Sand mappings plus the unknown-item rejection.

Fixes: `f4e68f6b486339c4397a194ca3ca885c12c35582`, `ccd33b70dd3e00f213fd168d781dd84ade215e0e`.

### Client packet-17 wall authority

Packet-17 wall actions were admitted into the authoritative path before the runtime could prove the selected wall item, consume it, or prove hammer/tool authority for wall destruction. Wire decoding remains supported, but runtime admission now fails these actions closed until those gameplay semantics are source-backed. This preserves protocol knowledge without granting mutation authority from packet shape alone.

Fixes: `d4aac5a3d782c45f4d04929dca644726acc52038`, `f4aecb9ebf1de4172ddc7d6f5c561fd325c0491d`.

### Completed one-shot workflow residue

`security-idle-doc-sync.yml` was a self-removing documentation migration workflow whose target ten-minute idle-timeout documentation had already been applied. The workflow remained in `main` and produced a startup failure with no useful job. It was removed after verifying the runtime and documentation already carried the intended value.

Fix: `a81b7dc917e916d78cd80d5afd59c9c500307586`.

### Vanilla world-generation RNG lifetime

A pre-existing change outside the ten-hour audit window had converted `VanillaSharedRng` into one continuously advancing RNG across registered passes. The new world-generation work in the audited window built the complete ordinary canonical pass pipeline on top of that assumption, making the inherited mismatch material.

Pinned TerrariaServer 1.4.5.8 `WorldGenerator.RunPass` reseeds `Main.rand` from `_seed` before applying every enabled pass. TerraRuntime now creates a fresh source-pinned `VanillaUnifiedRandom1458` for every `VanillaSharedRng` pass. A regression test pins the per-pass reseed, and the official-source CI probe now fails if the pinned `RunPass` reseed disappears or moves after pass application.

Fixes: `e1b7aef7b50a0dcd78d8156dfac0326424526cf8`, `a080374ce61fd2cd83e1ae6f2627a2e7c9c74685`, `0ea6e53749ebf90257f5da482df152802889ee82`.

### World-generation documentation drift

The dedicated vanilla guide, general world-generation guide and architecture pages disagreed about selectable generators, RNG lifetime and the state of the ordinary canonical pipeline. RU and EN documentation now distinguishes the `terraruntime:flat` infrastructure baseline from `terraruntime:vanilla`, records complete ordinary canonical pass-identity coverage through `Final Cleanup`, and keeps reference-world and special/secret-seed parity explicitly incomplete.

Fixes: `881970debda8d9307566b8567024322bfb8ad947`, `fc6e39a38f27ea9e8388f5a8fc22bb060a33ad8d`, `a7dfe76e3455c24778f0951cca8f96e6a64a9717`, `1ed91205dc6ce85983faceda88494bcf743e8393`, `b0a914258fe8d93cbe00026393ef1e4bb4dfe0f9`, `e6b2649670b8b8d6a8d57981f8e34d047936d329`.

## Positive findings

The audited window is not a blanket architectural regression. Object placement follows the intended transaction boundary: authoritative selected inventory state is resolved, an explicit item-to-object mapping is required, world mutation commits before inventory consumption, failed inventory commit rolls the new object back, and replication occurs only after the transaction succeeds.

The NPC roadmap remains conservative despite the large AI-family expansion: admitted families do not claim full vanilla AI parity. The Skyblock roadmap likewise keeps custom progression gaps explicit. The vanilla world-generation roadmap distinguishes complete pass-identity coverage and official-server load acceptance from still-open reference-world parity.

## Remaining evidence work

These items were not changed speculatively during this audit:

- packet-17 `KillTileNoItem` and broad simple-tile destruction still need official-client/source evidence plus tile-specific mining/tool/drop authority before the runtime can claim broad server-authoritative mining parity;
- the `cc3898be2e5eeb9edab6e93c488a91cebd114c26` encoder change removes `MemoryStream` usage but its commit text overstates `ArrayPool`/pooling: `ArrayBufferWriter<byte>` plus `WrittenSpan.ToArray()` still allocates, so the performance claim needs the repository-required before/after allocation and throughput benchmark rather than prose;
- new worm-family slot-link behavior should receive focused official-source/differential coverage before stronger stale-link or full-lifecycle claims are made. Vanilla AI may intentionally use raw slot references, so this is an evidence task rather than a guessed rewrite.

## Audit conclusion

The high-volume change window made substantial real progress, especially in NPC-family coverage, Skyblock, object placement and ordinary canonical world-generation pass coverage. The main architectural defects found were narrow but important: client mutation authority was widened beyond imported gameplay facts, a dead one-shot workflow polluted checks, and the expanded vanilla worldgen relied on an inherited incorrect RNG-lifetime assumption. Those confirmed defects are corrected; remaining uncertain behavior stays fail-closed or explicitly incomplete rather than being rewritten from guesswork.
