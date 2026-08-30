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

- [ ] source-verify the exact 1.4.5.8 tile/item identities needed by Skyblock progression before adding them to catalogs;
- [ ] provide a Demon/Crimson Altar progression path without guessed raw IDs;
- [ ] provide a Hellforge progression path;
- [ ] provide a Hive/Queen Bee path and enough honey/biome semantics for it to function;
- [ ] provide a Lihzahrd Temple/Altar progression path;
- [ ] provide Mushroom, Marble, Granite, Spider and fishing-oriented resource anchors where vanilla progression depends on them;
- [ ] define richer deterministic loot tiers without turning the starter chest into a dump of otherwise unobtainable progression items.

## S2 - Runtime Skyblock gameplay profile

World generation alone cannot reproduce the behavior of a purpose-built Skyblock mode. Add a runtime-owned profile rather than hiding these rules inside the generator.

- [ ] stable persisted/selected Skyblock gameplay-profile identity;
- [ ] source-backed reduced biome thresholds where empty-world density requires them;
- [ ] source-backed renewable-resource/drop fallbacks for progression-critical enemies;
- [ ] source-backed boss/event allowances required when ordinary world structures are absent;
- [ ] explicit Hardmode conversion policy for a mostly empty world;
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
