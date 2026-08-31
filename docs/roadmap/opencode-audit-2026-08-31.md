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

### Client packet-17 no-item destruction authority

Packet-17 action `4` (`KillTileNoItem`) was still admitted from an untrusted client even though that path did not prove a selected destruction tool and deliberately bypassed the ordinary tile-drop transaction. The wire action remains source-known and decodable, but client runtime admission now fails it closed until TerraRuntime has a legitimate source-backed sender path and the matching destruction authority semantics. Focused ingress and replication tests prove the action cannot mutate a tile, advance section state or enter peer replication while authority is disabled.

Fixes: `3c6cd9cf53bda6dec92af8f661b7b16c4c5ca217`, `0c11a13bc1bf2d5564a55226a8679e3506dc8452`, `0e0276f7775d9cf08ad80c774d770464b23aa2d5`, `d809101c15f69bd1c470dcf07e60587fc6b46ade`.

The pinned packet-17 protocol contract remains green with action `4` represented as a wire identity; runtime admission is intentionally stricter than TerrariaServer's packet handler.

### Packet-17 protocol/runtime ownership split

Packet-17 wire resolution and gameplay admission previously shared `TerrariaTileManipulationState.TryGetKnownAction`, which lived in `TerraRuntime.Protocol.Multiplicity`. That made the protocol adapter conceptually own a gameplay legality decision even though the runtime was intentionally stricter than the wire contract.

The protocol state now exposes only `TryGetWireAction`, resolving all five source-known TerrariaServer 1.4.5.8 packet-17 identities without granting mutation authority. `ClientTileManipulationAdmissionPolicy` now lives in the runtime assembly and independently admits only the authoritative `KillTile` and `PlaceTile` slices. Known-but-unimplemented wall/no-item actions are distinguished from an unknown action byte, and focused tests pin both the complete wire identity set and the fail-closed runtime admission set. Existing authoritative call sites keep their compact call shape through a runtime-owned extension rather than a protocol-owned legality helper.

This closes the audit's protocol/gameplay ownership debt without widening client authority.

### Completed one-shot workflow residue

`security-idle-doc-sync.yml` was a self-removing documentation migration workflow whose target ten-minute idle-timeout documentation had already been applied. The workflow remained in `main` and produced a startup failure with no useful job. It was removed after verifying the runtime and documentation already carried the intended value.

Fix: `a81b7dc917e916d78cd80d5afd59c9c500307586`.

### Vanilla world-generation RNG lifetime

A pre-existing change outside the ten-hour audit window had converted `VanillaSharedRng` into one continuously advancing RNG across registered passes. The new world-generation work in the audited window built the complete ordinary canonical pass pipeline on top of that assumption, making the inherited mismatch material.

Pinned TerrariaServer 1.4.5.8 `WorldGenerator.RunPass` reseeds `Main.rand` from `_seed` before applying every enabled pass. TerraRuntime now creates a fresh source-pinned `VanillaUnifiedRandom1458` for every `VanillaSharedRng` pass. A regression test pins the per-pass reseed, and the official-source CI probe fails if the pinned `RunPass` reseed disappears or moves after pass application.

Fixes: `e1b7aef7b50a0dcd78d8156dfac0326424526cf8`, `a080374ce61fd2cd83e1ae6f2627a2e7c9c74685`, `0ea6e53749ebf90257f5da482df152802889ee82`.

### Worldgen source-contract isolation

The `Terraria Worldgen Pass Catalog` workflow mixed pass/RNG verification with an unrelated seed/CRC discovery step. That coupling prevented the pass/RNG source contract from reaching its actual verification steps when the assumed CRC helper assembly boundary could not be resolved from the pinned deployment. The workflow now owns only pass registration, terrain-pass, RNG and configuration evidence. Seed hashing remains separate evidence debt rather than a prerequisite for proving `WorldGenerator.RunPass`.

Fix: `4f4fbd6a93c72ad0bae0efbaa5a21adb11f16d42`.

Exact source-contract run `33358834457` completed successfully: pinned-source extraction, runtime pass-catalog fingerprint verification, runtime RNG fingerprint verification, `VanillaUnifiedRandom1458` build and evidence upload all passed. This closes the per-pass RNG-lifetime finding with executable evidence from the pinned TerrariaServer 1.4.5.8 binary rather than repository-internal agreement.

### World-generation documentation drift

The dedicated vanilla guide, general world-generation guide and architecture pages disagreed about selectable generators, RNG lifetime and the state of the ordinary canonical pipeline. RU and EN documentation now distinguishes the `terraruntime:flat` infrastructure baseline from `terraruntime:vanilla`, records complete ordinary canonical pass-identity coverage through `Final Cleanup`, and keeps reference-world and special/secret-seed parity explicitly incomplete.

Fixes: `881970debda8d9307566b8567024322bfb8ad947`, `fc6e39a38f27ea9e8388f5a8fc22bb060a33ad8d`, `a7dfe76e3455c24778f0951cca8f96e6a64a9717`, `1ed91205dc6ce85983faceda88494bcf743e8393`, `b0a914258fe8d93cbe00026393ef1e4bb4dfe0f9`, `e6b2649670b8b8d6a8d57981f8e34d047936d329`.

### Worm AI_006 link-lifecycle source contract

The worm-family slot-link evidence debt is now closed for the admitted chain slice. A dedicated official-binary probe extracts `NPC.AI_006_Worms` from TerrariaServer 1.4.5.8 and fail-closes on the source distinction between active-only Eater of Worlds structural death checks and active-plus-`aiStyle` body split checks. Runtime lifecycle predicates now preserve that distinction, and Eater chain construction propagates the source `ai[3]` root slot instead of zeroing it. Focused tests cover slot reuse, isolated segments and root propagation. Complete Eater death/loot/progression and the broader synchronized lifecycle remain explicitly incomplete.

## Positive findings

The audited window is not a blanket architectural regression. Object placement follows the intended transaction boundary: authoritative selected inventory state is resolved, an explicit item-to-object mapping is required, world mutation commits before inventory consumption, failed inventory commit rolls the new object back, and replication occurs only after the transaction succeeds.

The NPC roadmap remains conservative despite the large AI-family expansion: admitted families do not claim full vanilla AI parity. The Skyblock roadmap likewise keeps custom progression gaps explicit. The vanilla world-generation roadmap distinguishes complete pass-identity coverage and official-server load acceptance from still-open reference-world parity.

The post-audit code baseline at `4f4fbd6a93c72ad0bae0efbaa5a21adb11f16d42` passed the full ordinary CI matrix: build/test plus authoritative-loop, protocol, network, world and TUI smoke; NativeAOT Linux/Windows; and CoreCLR extensible Linux/Windows.

## Remaining evidence work

These items remain deliberately incomplete rather than being guessed into production:

- broad client mining parity is still narrow: ordinary `KillTile` authority is currently tied to the imported Copper Pickaxe path and does not yet model the complete vanilla tool-power, tile-specific breakability, special destruction, reach, inventory and drop semantics; wall actions and `KillTileNoItem` must remain runtime-disabled until their own authority contracts exist;
- non-numeric world-seed hashing still needs a dedicated pinned-source contract. `probe_worldgen_seed.py` was removed from the pass/RNG workflow dependency chain because its CRC helper assembly assumption was not proven; the runtime CRC implementation must not be described as exact source-verified seed-hash parity until that separate contract is established;
- the `cc3898be2e5eeb9edab6e93c488a91cebd114c26` encoder change removes `MemoryStream` usage but its commit text overstates `ArrayPool`/pooling: `ArrayBufferWriter<byte>` plus `WrittenSpan.ToArray()` still allocates, so the performance claim needs the repository-required before/after allocation and throughput benchmark rather than prose;

## Audit conclusion

The high-volume change window made substantial real progress, especially in NPC-family coverage, Skyblock, object placement and ordinary canonical world-generation pass coverage. The main architectural defects found were narrow but important: client mutation authority was widened beyond imported gameplay facts, a dead one-shot workflow polluted checks, and the expanded vanilla worldgen relied on an inherited incorrect RNG-lifetime assumption. Unknown tile placement, wall mutation and no-item destruction are now fail-closed; packet-17 wire knowledge is separated from runtime gameplay admission; the worldgen RNG correction is pinned by a successful official-binary source contract; and the ordinary CI matrix is green on the corrected code baseline. Remaining uncertain behavior is explicitly incomplete and isolated as evidence debt rather than being promoted through optimistic compatibility claims.
