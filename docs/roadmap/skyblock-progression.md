# Skyblock progression roadmap

This roadmap tracks the gap between a valid deterministic Skyblock `.wld` and a complete Terraria progression mode. It is deliberately separate from vanilla source-parity world generation: Skyblock may be custom, but every vanilla-visible content identity and gameplay fallback still needs a source-backed contract.

Checkbox policy: `[x]` means implemented on `main` with focused executable verification. `[ ]` means the slice is not yet proven complete.

## S0 - Deterministic progression foundation

- [x] separated starter, biome and lower dungeon island layout;
- [x] deterministic compact layout for the documented `$256\times160$` minimum workspace;
- [x] reserved Snow/Water, Jungle/Honey, Cavern/Lava and Aether/Shimmer islands;
- [x] Water, Lava, Honey and Shimmer basins through the normalized generation tile ABI;
- [x] fixed-seed liquid-footprint determinism tests;
- [x] `.wld` round-trip verification for progression liquids and generated chests.

## S1 - Source-backed structure and resource anchors

- [x] source-contract coverage for the Skyblock tile/wall identities currently used by structure/resource generation;
- [x] Demon/Crimson Altar generation anchor with source-shaped `3×2` frame layout and world-evil style offset;
- [x] Hellforge generation anchor with source-shaped `3×2` frame layout;
- [x] Hive shell plus `HiveUnsafe` wall around the guaranteed Honey reservoir;
- [x] compact Lihzahrd chamber using `LihzahrdBrick`, `LihzahrdBrickUnsafe` and a `3×2` Lihzahrd Altar anchor;
- [x] deterministic Mushroom, Marble, Granite and Spider/Cobweb resource anchors;
- [x] fishing-oriented Water reservoir remains guaranteed independently from random island placement;
- [ ] source-back the Larva/Queen Bee interaction path rather than treating Hive geometry alone as boss progression;
- [ ] source-back Lihzahrd Altar activation, Power Cell consumption and Golem summon behavior in authoritative gameplay;
- [ ] define richer deterministic loot tiers without turning the starter chest into a dump of otherwise unobtainable progression items.

## S2 - Runtime Skyblock semantics

World generation alone cannot reproduce the behavior of Terraria Skyblock. Runtime rules are derived from persisted vanilla world state and current world contents; they must never depend on `WorldGeneratorId` or on which implementation originally generated the file.

- [x] persist and consume Terraria 1.4.5.8 `SkyblockWorld` world semantics; built-in Skyblock creation sets the vanilla flag while loaded vanilla Skyblock worlds are recognized from the `.wld` itself;
- [x] source-backed `lowTiles` classifier gated by `SkyblockWorld` and the strict `<10%` active-tile density rule;
- [x] source-backed Snow and Desert thresholds (`300` under `lowTiles`, otherwise `1500`);
- [x] explicit source-backed Hardmode conversion policy (`GERunner` conversion is skipped under `lowTiles`);
- [ ] wire the threshold policy into the future tile-count/SceneMetrics biome producer;
- [ ] source-backed renewable-resource/drop fallbacks for progression-critical enemies;
- [ ] source-backed boss/event allowances required when ordinary world structures are absent;
- [ ] fishing and spawn-rule validation against the lowered layer/biome model;
- [ ] protocol-safe replication with no client-unknown content IDs.

## S3 - Progression verifier

A world that loads is not necessarily a world that can be completed.

- [ ] define machine-checkable milestones from fresh spawn through Wall of Flesh, mechanical bosses, Plantera, Golem and Moon Lord;
- [ ] verify that every milestone has at least one generated or renewable prerequisite path;
- [ ] run the verifier across a seed corpus and every supported world size/evil/game mode combination;
- [ ] retain the current official TerrariaServer `.wld` acceptance as a separate compatibility gate;
- [ ] record a compact progression manifest in CI artifacts so a failed seed shows the missing prerequisite instead of only reporting "generation failed".

## S4 - Optional richer Skyblock content

These items improve variety but are not allowed to weaken deterministic progression guarantees.

- [ ] additional thematic planetoids and micro-structures;
- [ ] richer source-pinned chest pools;
- [ ] optional challenge presets with fewer initial resources;
- [ ] host/plugin extension examples that add a deterministic island/pass without taking ownership of the live world;
- [ ] evaluate subworld-like concepts only if they can be expressed without creating a second authoritative world owner.

## Definition of done

Skyblock is progression-complete only when a generated world is deterministic, `.wld` compatible, accepted by the official pinned server, contains no guessed client-visible IDs, and has an executable proof that all required vanilla progression milestones remain reachable without manual world editing or administrator item injection.
