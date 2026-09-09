# Vanilla worldgen visual parity audit — 2026-08-31

These observed defects remain vanilla-specific parity debt and are not papered over with optimized geometry helpers.

2026-09-08 follow-up: [complete109-registration audit](../en/vanilla-worldgen-pass-audit.md) separates actual owners from equality proof. Two further independent source-proven errors corrected: Terrain's liquid-line rolls incorrectly ran in a separately reseeded bridge; Remove Water From Sand incorrectly drained embedded active sand across the whole map instead of the source inland surface scan. Official Terrain grids/lines/next RNG match nine cases and the surface-drain fixture matches all600000 liquid cells. These fixes do not close the reported Dungeon/desert/Pyramid/LivingTree/Crimson/Oasis geometry or final liquid-settle debt.

Rail correction (2026-09-08): all-zero rail frames and insufficient three-cell clearance were independently confirmed. Existing Micro Biomes now publishes source-indexed reciprocal straight/slope/end connections with back-track `-1` and admitted source clearance, preflighted for overhead objects. Complete canonical Small/Medium/Large maps retain connected tracks; official Minecart.FrameTrack independently matches14 frame pairs. Full TrackGenerator route/origin/history/tunnel/smoothing/pressure/wall-finishing and visual riding acceptance remain open. Saved worlds are not retroactively rewritten; see [scope](../en/vanilla-worldgen-micro-biomes.md#parity-boundary).

## Tester follow-up — 2026-09-07

New reports: exposed/missing-wall pyramid without the usual descent; surface containers; missing underground desert walls and malformed oases; a dirt mountain in snow; malformed Living Trees; thin Crimstone cave lining instead of Crimson topology; small Dungeon without descent; sparse/low Hellforts and apparently cut-away Hell terrain/lava. Oceans were reported comparatively close. The message contained no actual attached images. Exact tested world/seed/size and executable are requested; these individual final-world defects are not yet independently reproduced.

Code audit does independently confirm a relevant Underworld root: `MidPass1458.ApplyUnderworld` constructs a continuous ash floor and periodic `x % 19` lava puddles, unlike the pinned pass's roof walk, initial basin, ash/cavity runners and `QuickWater(-2)` order. The earlier two-row lava restoration and source-backed HellFort anchors do not repair that foundation. Do not arbitrarily raise/add forts to compensate, and do not use successful file load or material-count tests as a visual parity claim. Preserve the checked narrow structural slices below; the complete generated-world criteria above remain open.

The 2026-09-07 startup investigation additionally found invalid single-cell Jungle detritus233. The existing vegetation pass now emits complete source-backed3x2 objects only on admitted clear footprints; all3 real-failure-seed integration maps verify every233 before primary liquid preparation. Candidate distribution, plant-replacement cascades and the generation2x2 branch remain open. Post-generation UnsupportedLiquidDeathTile refusals also required corrected TileObjectData style/alternate checks and loading-only coherent destruction; successful worldgen smoke alone is not startup acceptance. See [startup evidence](../en/startup-performance-gate.md#generated-world-startup-failures).

- [x] missing Underworld fort structural slice: ordinary AddHellHouses anchor/palette/spacing and HellFort rooms, wings, doors, platforms and exterior crumbling now execute in the existing Underworld pass. Hellforges uses source house-wall sampling and complete Place3x2 objects. Helper and canonical Small/Medium/Large seed42 regressions cover this boundary; see [scope](../en/vanilla-worldgen-surface-finish.md#underworld-settlements-and-hellforges).
- [x] ordinary settlement lighting: source integer-divided request budget, candidate/spacing/liquid gates, anchor cleanup and CheckTorch attachment frames. Canonical Small/Medium/Large seed42 must retain actual Hell torches; removing the pass fails all three maps.
- [x] ordinary edge ash forests: source grass exposure/scan RNG and GrowTreeWithSettings(Tree_Ash) clearance, branches, independent roots and crown frames. Scripted source tests and canonical Small/Medium/Large regressions cover this slice; removing the pass fails all three maps.
- [x] ordinary cleared-room floor furniture: all13 AddHellHouses arrangements, source budgets/clearance/RNG/styles and existing persisted empty3x2 dresser storage. Canonical Small/Medium/Large regressions and scripted arrangement/metadata tests cover this scoped generation slice, not universal placement side effects.
- [x] ordinary settlement paintings and ceiling decorations: source room recentering, exclusion rectangles, palette/RNG, complete painting/banner/chandelier/lantern frames and support checks. Scripted helper tests and complete retained footprints in canonical Small/Medium/Large seed42 cover this generation slice; disabling the real invocation fails all three maps.
- [x] Underworld's post-carving/pre-Hellstone two-row lava restoration now fills inactive cells at height-145/-144 across the complete width, without RNG draws or changes to active/actuated cells. Eleven helper/real-pass regressions cover this boundary; omitting the real invocation fails Small/Medium/Large. This is not initial basin/QuickWater/runner parity.
- [x] ordinary Underworld base replacement: source roof/basin, Ash/cavity/Hellstone calls and pre-Dungeon QuickWater replace the lower slab/periodic puddles. Official differential21 runner and8 liquid fixtures, canonical phase controls and composed-map regressions cover this slice. Later settling now includes source embedded-solid liquid clearing without weakening validation.
- [ ] complete Underworld reference-seed/final-world geometry equality, full later repeated liquid-settle sequence and special-seed terrain/ash vegetation; the verified ordinary slices are not full Underworld parity.
- [x] ordinary Dungeon furniture/locked and biome chests/traps/full-pass geometry: bounded R16 compares16 identical real-prefix inputs with the unmodified original, plus24 flat fixtures and14 extra seeds. See the current pass audit for exact evidence and local acceptance.
- [ ] independent end-to-end Dungeon prefix, all remaining global side-state consumers, special seeds and final-world/client-playthrough equality; R16 does not prove these.
- [ ] exact Aether, evil/desert/jungle biome helpers and final cross-biome progression acceptance; material presence alone is insufficient.

- [x] ordinary cactus growth now follows source PlantCactus/GrowCactus attempts, gates, population and trunk/raised-arm rules, with scripted and canonical Small seed42 regressions. Source generation skips cosmetic framing; zero cactus frames are not a defect. Mixed-pass placement, oasis and special seeds remain open.

- [x] ordinary palm growth: source `GrowPalmTree` gates, full height, bend/RNG order, base/crown framing and paint/coating replace zero-framed columns; canonical seed `42` also rejects the former surface-window omission. Placement density and the remaining mixed cactus/palm/coral pass are still partial.

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

The deep-ocean finalization regression for Large seed `8675309` is covered by full generation/finalization tests.
The obsolete fixed-height water/sand census was removed in favor of the existing basin geometry gate, without
changing generation or relaxing its water-continuity, sand-floor and beach-rise limits. See the
[ocean validation contract](../en/vanilla-worldgen-oceans.md).

The Small seed `42` chest-corruption regression is fixed at `MountainCaves`: ordinary mountains now fill inactive
cells according to the pinned `WorldGen.Mountinater` rules instead of carving through the existing dungeon.
The per-pass chest-anchor regression fails on the former implementation. This does not close reference-seed terrain
identity or secret-seed mountain parity; see the [mountain behavior boundary](../en/vanilla-worldgen-dungeon-stage.md#mountain-caves-and-existing-world-objects).
