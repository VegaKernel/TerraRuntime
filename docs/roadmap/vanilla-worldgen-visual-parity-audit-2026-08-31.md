# Vanilla worldgen visual parity audit - 2026-08-31

These observed defects remain vanilla-specific parity debt and are not papered over with optimized geometry helpers.

- [ ] terrain silhouette: add final post-pass reference-world fixtures for canonical Small/Medium/Large;
- [x] surface shaping: the ordinary canonical `Smooth World` pass now owns both source-ordered scans, exact shared-RNG decision points, typed/versioned tile capabilities, all four slope orientations, half-blocks, erosion/gap-fill, sand normalization and orphan-slope correction; focused fixtures, canonical output checks and a pinned-decompile source contract replace the former coordinate heuristic;
- [x] trees: replace the explicitly source-shaped trunk/branch scaffold with complete 1.4.5.8 framing, crowns and branches; the clean-room `WorldGen.GrowTree` port now owns typed growth capabilities, exact shared-RNG segment ordering, roots and top frames, with focused scripted and canonical generated-world checks;
- [x] dungeon: the vertical shaft + periodic-room approximation is replaced by a typed `LegacyDungeonLayoutProvider`
  graph with starting/end rooms, branches, horizontal and vertical halls, seeded entrance halls and a surface entrance;
  shared RNG owns topology/seeds while each component owns isolated `UnifiedRandom(RandomSeed)` geometry, and canonical
  generated-world/finalizer checks plus a pinned-decompile probe reject a sparse or shaft-shaped regression;
- [x] oceans: remove the destructive late `AlignOcean` correction; the source-backed `Beaches` stage now owns the
  pinned start bounds, shared-RNG coast profile selection, both complete `TuneOceanDepth` tables, water/floor split and
  column order, while canonical finalization proves edge-connected water, continuous sand floors and rising beach
  transitions for every size/seed exercised by the Small/Medium/Large workflow.

The observed unusual dungeon geometry is closed at the structural graph boundary; furniture, chests, traps and exact
reference-seed dungeon equality remain outside this visual-audit item. The under-generated-looking ocean symptom is closed by
the source-backed `Beaches` block and structural basin validator. The coordinate-driven jagged/half-block-heavy surface
writer is closed by the source-backed `Smooth World` block. Segmented ordinary trees without crowns are closed by the
source-backed growth/framing block; palm/vanity-tree placement remains outside that claim.
