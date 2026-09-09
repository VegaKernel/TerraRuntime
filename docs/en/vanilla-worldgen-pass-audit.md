# Complete vanilla worldgen pass audit — 2026-09-08

Ordinary Dungeon full-pass verification (2026-09-09): stage36 moves to bounded `R16`, not `E9`. Sixteen retained comparisons use identical real production-prefix inputs in the unmodified official Dungeon pass and the runtime pass: all normalized cells, ordered chest inventories/prefixes, Old Man anchor and next RNG match. They cover all canonical sizes, all three entrance forms and both evils. Another24 independent flat-input comparisons and14 extra Small seeds also match. `R16` does not claim the preceding prefix, every GenVars field, special seeds or final-world equality. The ledger is now **40P + 32C = 72 unfinished rows**; independent multi-stage prefix evidence remains **31 E9 / 279 checkpoints**. Final acceptance is recorded in agent memory; older paragraphs below are historical.

Dungeon spikes/wall variants (2026-09-09): ordinary spike attempts, asymmetric supports, run ordering and preservation of liquid/frames now follow source rules. Wall seeds spread through connected base-wall regions, recoloring solid borders without crossing them; slopes/actuation retain source behavior. Early sampling no longer includes the later area expansion. Independent144 whole-cell/RNG fixtures pass;151 new tests, Dungeon1467/1467; reverting liquid preservation/propagation fails124 tests, then restored. Release6480/6480 passes: zero build warnings/errors, Windows NativeAOT/six smokes, fresh-world loading in both servers and unchanged comparison budgets. Linux NativeAOT/client playthrough remain unverified. The retained graph envelope still needs comparison with the complete crawler bounds lifecycle; remaining decoration and full-prefix equality are open. No whole-world or E9 claim;40P+33C=73unfinished,31E9/279 unchanged.

Dungeon discovery/doors batch (2026-09-09): source room interiors and retained hall axis now drive ordered, non-deduplicated candidates. Ordinary room edges are interleaved and do not require matching background walls. Door placement uses source column selection, clearance, masonry, frame/random order and protected supports. Independent official comparisons:234 door fixtures and8 candidate suites;250 new regressions, focused1316/1316. Negative controls fail30 door ties/2 old room-inset tests, then restored. Release6329/6329 passes with zero build warnings/errors, Windows NativeAOT/six smokes, fresh-world loading in both servers and unchanged comparison budgets. Linux NativeAOT and client playthrough remain unverified. Unsupported frame-important object destruction remains fail-closed in this pre-furniture slice. Whole Dungeon and world equality are **not** verified; ledger unchanged:40P+33C=73 unfinished,31E9/279. Earlier paragraphs describe preceding batches.

Locally verified cohesive Dungeon batch (2026-09-09): the production entrance renderer uses source-derived Dome/Tower buildings instead of rectangular shells. Independent original-executable comparisons cover 216 whole buildings and 216 building-plus-platform/shelf continuations, including normalized cell fields, shared RNG and ordered metadata. Thirty ordinary platform fixtures and twenty-four pillar/wedge/mosaic fixtures supplement this evidence. The shared tree grower retains final RangeFrame cleanup and an explicit ignore-walls growth gate. Legacy entrance, layout and live authorities are retained. Final Release6079/6079, zero build warnings/errors, WindowsNativeAOT/six smokes, fresh-world loading in both servers and unchanged comparison budgets pass; LinuxNativeAOT/client playthrough remain unverified. Complete Dungeon candidate discovery, doors/remaining decoration and real-prefix equality remain open; no E9 promotion or whole-world parity claim.

[Русский](../ru/vanilla-worldgen-pass-audit.md)

Layout-origin final gates: Release5586/5586, zero warnings/errors, WindowsNativeAOT+sixsmokes and fresh dual-server loading pass. The ordinary seed1458 Dungeon anchor matches the reference; full-world cells still differ. No status promotion; next Dome/Tower buildings/full-prefix work remains open.

2026-09-09 layout-origin update:18 official whole-layout fixtures and63 room-interior metadata comparisons match; new31 retained checks are part of574 passing Dungeon tests. Restoring the old origins/shell anchor fails20/22. Precalculated initial cursor/RNG and interior-top connection are corrected, not the complete Crawler/buildings/features. Dungeon36 remains `C`, **40P+33C=73 unfinished;31E9/279 unchanged**. Next: Dome/Tower buildings and complete Dungeon prefixes. Final gates are in agent memory; paragraphs below are historical.

2026-09-09 precalculated entrance connection: 54 actual-official whole-route fixtures match cells, per-step state, platform records and RNG; retained59/affected631 pass, incorrect-length negative54 failures. Dungeon36 remains `C`: Dome/Tower buildings, layout start/top-room metadata and decoration still diverge. **40P + 33C = 73 unfinished rows; 31 E9 / 279 checkpoints** remain unchanged. No complete-prefix promotion from this component proof.

2026-09-09 Legacy entrance component update: 108 independent official building fixtures match complete cells, next shared RNG and entrance metadata; the production rectangle negative control fails 111/112 retained tests. Dungeon row36 stays `C` (some older narratives called it `P`): Dome/Tower, layout, decoration and full-prefix proof remain open. Counts remain **40P + 33C = 73 unfinished rows**, **31 E9 / 279 checkpoints**.

This inventories all **109 registrations** in TerrariaServer 1.4.5.8 `WorldGen.AddPasses` against the current production plan. **The complete generator is not yet 1:1.** The table does not mark approximate implementations complete.

The ordinary canonical plan contains 107 source positions plus 7 internal stages, totaling 114. The first Jungle registration and Skyblock occur only inside `skyblockWorldGen`; their absence from the ordinary plan is not missing coverage. Internal Reset, TerrainLayers, Biomes/Caves/Ores barriers, SecretSeeds barrier and Metadata are not additional vanilla content.

`SourceBackedFinal1458Tests.Complete_ordinary_plan_matches_every_applicable_source_registration_in_order` checks the entire sequence, not just its tail. This proves coverage/order, not geometry. Scope: Small/Medium/Large; other profiles do not inherit these claims. Pure Remix has a separate bounded Reset/Terrain/Dunes slice; combined special seeds and noncanonical dimensions still use the existing compatibility path. No new fallback was added.

## Evidence levels

2026-09-09 surface-material batch: stages39/40/43 move from `P` to `C` after 54 official complete-delegate synthetic-input comparisons (all normalized cells and next RNG), 58 retained checks and 473 affected tests. There are now **40P + 33C = 73 unfinished rows**; **31 E9 / 279 checkpoints** is unchanged. These fixtures do not bypass the still-divergent preceding Dungeon graph or prove nine real prefixes. Historical counts below describe earlier checkpoints.

Current Lakes/Slush acceptance: all nine ordinary size/seed prefixes match full cell data, next RNG and retained tunnel/lake/snow state. The retained test covers **31 stages / 279 checkpoints**; stages33/34 are bounded `E9`, leaving **43P + 30C** unfinished rows. Release `4908/4908`, restored focused `41/41`, Windows NativeAOT, six smokes and fresh Small/Classic/Corruption1458 loading in both servers pass. The allocation harness now warms the same non-inlined loop it measures; workload/byte limit are unchanged and injected allocation fails. Earlier failures remain documented, with the exact small allocation object unproven. Same-reference tile L1 `0.196460` is slightly worse than the previous batch; wall L1 `0.458101` improves. Both pass unchanged budgets, not whole-world equality. Linux NativeAOT, special seeds and client playthrough remain unverified.

Current Corruption acceptance: all nine ordinary Corruption prefixes match full cell data and next RNG. The retained prefix now covers **29 stages / 261 checkpoints**; stage32 is bounded `E9`, leaving **45P + 30C** rows. Crimson separately has six matching complete-pass synthetic-input fixtures, not nine real-prefix fixtures. Cave kernels match36 official cases; omitting wall-frame RNG fails36/36. Release4870/4870, build0/0, WindowsNativeAOT+six smokes, freshSmallCorruption dual-server loading, and the extended prefix/grass/cave recheck85/85 pass. Same-reference budgets pass at tileL1 `0.195909`, wallL1 `0.461801`; complete world equality, special seeds, LinuxAOT and client playthrough remain unverified. Earlier batch counts below are historical.

Latest Underworld batch: all9 complete ordinary prefixes match full16-byte cells, next RNG and ordered dresser slots/coordinates/empty contents. The prefix now covers28 stages/252 checkpoints;46P and30C rows remain. Shared QuickWater fixes include source origin-flag contacts, the full-solid guard, sub24 kind reset and generation-only LavaCheck desert-wall conversion over7x7 cells; loading never applies that conversion.24 official contact kernels pass; negative controls fail6/6 and12/24. Full default-parallel4728/4728 and sequential4728/4728, restored Release0 warnings/errors, final prefix/contact44/44, WindowsNativeAOT+sixsmokes and freshSmall1458 loading in both servers pass. Earlier parallel allocation failures are recorded in work-state; the final standard run is green without threshold changes. Same-reference budgets remain unchanged and pass (tileL1 .201211,wallL1 .530400,liquidRatio1.043167); whole-world equality is not achieved. Earlier batch narratives below are historical; the table and E9 definition reflect current proof.

Current sky/deposit/web batch passes Release **4638/4638**, zero build warnings/errors, all six Windows NativeAOT smokes and fresh Small/Classic/Corruption1458 loading in both TerraRuntime and official1.4.5.8. Full-map acceptance exposed incomplete antlion-larva fragments after source DirtToMud; the existing final-framing slice now removes those fragments as official CheckSuper does, without weakening loading validation. Twenty independent style/fragment regressions supplement the deposit tests; disabling the fix fails16/28 framing tests.

The untouched new candidate fingerprint is `00d191513552b72b52278249a0953bfa00b596f9d913979b7e5cd0630d4a5500`, against the unchanged official reference `a73ec6c799c5e6ce3f9684377e97b07770caf19f4369c63d237d20c4fb21fa26`. Existing comparison budgets pass unchanged: tileL1 **0.201211** (previous0.275338), wallL1 **0.530400**, active ratio0.966632, liquid ratio1.043254, silhouette NMAE0.007981/p95 0.044167/correlation0.922428; spawn delta(1,0), dungeon delta(85,-6), chests181→144, townNPC2→2. This improvement is not whole-world equality or client playthrough acceptance. Linux NativeAOT remains unexercised locally. The complete Underworld prefix is the next differential target.

The next related batch extends independent ordinary-prefix proof through **Webs: 27 stages × 9 seed/size cases, 243 checkpoints**. Floating Islands retains its proven geometry and now exports ordered X/Y/style/lake state. Dirt To Mud, Silt and Shinies use source populations, depth bands and the existing small TileRunner instead of approximate blobs. Webs uses the source ceiling/side search and retained mountain-cave coordinates; those coordinates are independently checked at Mount Caves. All five new stages match complete normalized cell data and next RNG. Twenty-six isolated mud/silt/ore/web fixtures cover metadata and replacement rules. Final shared Release/NativeAOT/fresh-world gates are tracked in agent memory, not implied by prefix proof.

Marble and Granite now match all nine complete22-stage ordinary prefixes, including full normalized tile bytes, next RNG and ordered padded structure rectangles. Nine Marble and fifteen Granite component fixtures independently match the official executable. The shared local Windows acceptance gates pass (4515 tests, six NativeAOT smokes and fresh-world loading in both servers); bounded E9 is not whole-world completion. DesertHive's field RNG was moved unchanged for Granite reuse, without adding another generator.

The existing MidPass invokes the source-shaped slab and pressure algorithms directly. Placement reads retained `GenVars.rockLayer` (`TerrainState.CurrentRockLayer`), not the published gameplay layer. Marble count scales by area, Granite by width. Shared framing preserves ordinary ore, slope, speleothem, paint and liquid semantics; unknown object framing aborts instead of inventing a replacement.

Mushroom Patches now matches the independent official ordinary prefix on all nine canonical cases, including complete normalized cell data, next RNG and retained biome centres. The existing MidPass uses source-derived placement, ShroomPatch and whole-map grass/cleanup. Six isolated ShroomPatch fixtures also match full cell data/RNG. Small root runners reuse the existing Underworld runner, now named `SmallTerrainRunner1458`, with no retained alias. This supports bounded E9, not complete generator parity; final acceptance is tracked below and in agent memory.

Full Desert now uses source-derived surface, all four entrance builders, cluster fields and decoration in the existing MidPass. All nine canonical prefixes match the official server, including the full normalized 16-byte cell representation at Full Desert, its retained desert/hive/density/structure bounds and next RNG. Isolated official fixtures additionally cover five surfaces, five cluster fields, twenty entrances and five decoration cases. This is bounded E9 evidence, not complete generation or final acceptance; current acceptance is tracked in agent memory. `WorldGenerationRequest.ResolveVanillaSeed1458()` owns seed conversion shared by the executor and desert material stream: numeric negatives follow the source absolute-value/minimum-int rule, while text CRC32 uses the low byte of each UTF-16 code unit, not UTF-8. Fourteen official seed values include Cyrillic, emoji and numeric boundaries. Custom-provider seed identity is unchanged.

- **E9**: ordinary prefix from Dunes through Underworld, 28 stages × 9 seed/size cases, 252 checkpoints. Independent official type/wall/frame/active/liquid hashes, next RNG and pyramid candidates match at each stage. Full Desert and every subsequent prefix stage additionally check all normalized16-byte cell fields; retained desert, mushroom, stone-biome, sky-island and mountain-cave metadata are checked at their respective stages. This does not prove all GenVars/StructureMap consumers, unexercised branches or special seeds.
- **R9**: independent official Terrain probe, 3 seeds × 3 sizes: all type/frame/active cells, waterLine/lavaLine and next RNG match. Reset beach inputs were supplied identically; this is not an independent full Reset or whole-world proof.
- **R1**: independent official Remove Water From Sand probe on an isolated grid; amount/kind match for every cell. This does not prove the preceding Settle Liquids or the whole post-settle block.
- **C**: source-backed contracts/verified components exist; the complete pass is not proven 1:1.
- **R16**: complete ordinary Dungeon pass on16 identical real-prefix inputs; independent original cells/ordered chests/anchor/RNG. This is not independent generation of the supplied prefix or proof of every global field.24 flat fixtures and14 extra seeds supplement it; special seeds remain open.
- **P**: implemented, with exact helpers, retained state and RNG still open.
- **N**: no ordinary-profile mutation; special-seed behavior is not closed.
- **S**: conditional Skyblock registration, not part of the ordinary plan.

For every C/P row, the next required gate is an identical input snapshot for candidate and official pass, comparing changed cells, side tables, GenVars and next RNG. The check column identifies scope, not a claim that every listed helper is missing.

## Production owners

- Base: [SourceBackedProvider1458.cs](../../src/TerraRuntime.WorldGeneration/Generation/Vanilla/SourceBackedProvider1458.cs).
- Early: [EarlyPipeline1458.cs](../../src/TerraRuntime.WorldGeneration/Generation/Vanilla/EarlyPipeline1458.cs).
- Mid: [MidPipeline1458.cs](../../src/TerraRuntime.WorldGeneration/Generation/Vanilla/MidPipeline1458.cs).
- Dungeon: [DungeonPipeline1458.cs](../../src/TerraRuntime.WorldGeneration/Generation/Vanilla/DungeonPipeline1458.cs).
- JungleStructure: [JungleStructurePipeline1458.cs](../../src/TerraRuntime.WorldGeneration/Generation/Vanilla/JungleStructurePipeline1458.cs).
- PostSettle: [PostSettlePipeline1458.cs](../../src/TerraRuntime.WorldGeneration/Generation/Vanilla/PostSettlePipeline1458.cs).
- Chest: [ChestPlacementPipeline1458.cs](../../src/TerraRuntime.WorldGeneration/Generation/Vanilla/ChestPlacementPipeline1458.cs).
- LateStructure: [LateStructurePipeline1458.cs](../../src/TerraRuntime.WorldGeneration/Generation/Vanilla/LateStructurePipeline1458.cs).
- SurfaceFinish: [SurfaceFinishPipeline1458.cs](../../src/TerraRuntime.WorldGeneration/Generation/Vanilla/SurfaceFinishPipeline1458.cs).
- StartingNpc: [StartingNpcPipeline1458.cs](../../src/TerraRuntime.WorldGeneration/Generation/Vanilla/StartingNpcPipeline1458.cs).
- Vegetation: [VegetationPipeline1458.cs](../../src/TerraRuntime.WorldGeneration/Generation/Vanilla/VegetationPipeline1458.cs).
- UndergroundFinish: [UndergroundFinishPipeline1458.cs](../../src/TerraRuntime.WorldGeneration/Generation/Vanilla/UndergroundFinishPipeline1458.cs).
- MicroBiomes: [MicroBiomesPipeline1458.cs](../../src/TerraRuntime.WorldGeneration/Generation/Vanilla/MicroBiomesPipeline1458.cs).
- Final: [FinalPipeline1458.cs](../../src/TerraRuntime.WorldGeneration/Generation/Vanilla/FinalPipeline1458.cs).

Special: existing compatibility profile, not an ordinary overlay.

## Early geometry corrections in this continuation

The existing early owner now retains integer dune curve midpoints and source clear/reset metadata, double rock/dirt population bounds, the correct current-surface Small Holes exclusion and the Clay-versus-Sand gate. Rock Layer Caves draws brush parameters before position. Surface cave searches stop without a runner when no surface is found; wet Caverer branches restore water and out-of-world tunnel brush cells retain their RNG draws. Mount Caves uses the verified three-type exclusion. Grass seeds its center from four active dirt neighbours, preserving center metadata; it is not exposed-surface grass spreading.

`EarlyTerrainReference1458Tests` retains all 252 official stage checkpoints for seeds 1458, 42 and 8675309 at Small/Medium/Large dimensions, plus isolated Grass and absent-surface regressions. E9 is a bounded differential result, not whole-world completion. Dunes regular StructureMap rectangles and Tunnels side tables are not fully retained; Corruption and later passes remain outside this prefix proof. Earlier whole-world measurements below are explicitly historical.

Jungle now preserves the source empty-wall placement gate and framing RNG, wall-hole sample range, final tunnel iteration and gem draw order. Its generation-only natural-tile destruction clears type/frames/shape/block coating like KillTile, including the grass dust-selection draws, without adding gameplay drops or live tile authority. Unknown objects and exhausted search budgets fail closed; no invented mud origin is substituted.

The existing Mid pass now invokes [JungleMudSurface1458](../../src/TerraRuntime.WorldGeneration/Generation/Vanilla/JungleMudSurface1458.cs) for whole-map mud exposure and four-connected clumps below20. All admitted prefix materials are solid/clearable; unknown active semantics abort before mutation. Since59→60 preserves activity/solidity and this slice has no RNG, the source full-map scan admits an equivalent nonrecursive exposure scan. Source lava-column ordering, ten-tile border, paint/coating cleanup and five-tile component bounds are preserved. Nine complete official pass fixtures plus isolated tests verify this bounded ordinary result, not later DesertBiome or all special seeds.

Final Cleanup now invokes the bounded generation-only `TileFrame / CheckDoorClosed` slice for Obsidian doors. Overlapping HellForts can overwrite one door cell; the remaining fragments are removed if the three-cell footprint or solid anchors are broken, preserving the replacement brick. Six independent official fixtures and production integration tests cover this correction. This does not close all object framing, other door styles or the complete Final Cleanup pass.

## All registrations in source order

Full Desert integration also exposed later-stage boundary defects. Ordinary Settle Liquids leaves `RollingCactus` (tile484) non-solid; Smooth World now preserves that source override instead of eroding its cells or copying its identity into terrain gaps. Final Cleanup removes incomplete unstyled rolling-cactus footprints through the bounded `Check2x2` slice, preserving foreign replacement tiles; full anchor checks and all object framing remain open. Loading-only liquid death admits coherent rolling cactus and the four generated `AntlionLarva` (tile485) styles, with malformed/foreign footprints still rejected. Their named identities are available in `VanillaTileIds`; no live drop, NPC or projectile authority is added. Generated-world liquid validation now reuses the simulator's existing exact settling overrides instead of rejecting liquid inside boulders. Ordinary full-solid terrain remains rejected. Independent official QuickWater fixtures and generation/loading TileFrame/KillTile fixtures support these changes; current acceptance gates are recorded in agent memory.

| # | Source name | Owner | Evidence | Next check / scope |
| ---: | --- | --- | --- | --- |
| 1 | Terrain | Base | R9 | TerrainPass / layers / RNG |
| 2 | Jungle | Special | S | skyblockWorldGen |
| 3 | Skyblock | Special | S | skyblockWorldGen |
| 4 | Dunes | Early | E9 | Dunes / pyramid candidates / RNG |
| 5 | Ocean Sand | Early | E9 | OceanSand / TileRunner / RNG |
| 6 | Sand Patches | Early | E9 | TileRunner / selection / RNG |
| 7 | Tunnels | Early | E9 | digTunnel / RNG |
| 8 | Mount Caves | Early | E9 | Caverer / selection / RNG |
| 9 | Dirt Wall Backgrounds | Early | E9 | background scan / walls / RNG |
| 10 | Rocks In Dirt | Early | E9 | TileRunner / RNG |
| 11 | Dirt In Rocks | Early | E9 | TileRunner / RNG |
| 12 | Clay | Early | E9 | TileRunner / RNG |
| 13 | Small Holes | Early | E9 | TileRunner / liquid lines / RNG |
| 14 | Dirt Layer Caves | Early | E9 | TileRunner / liquid lines / RNG |
| 15 | Rock Layer Caves | Early | E9 | TileRunner / liquid lines / RNG |
| 16 | Surface Caves | Early | E9 | Caverer / selection / RNG |
| 17 | Wavy Caves | Early | E9 | special-seed branches |
| 18 | Generate Ice Biome | Early | E9 | snow bounds / lavaLine / full-pass snapshot |
| 19 | Grass | Early | E9 | cardinal dirt seeds / metadata / RNG |
| 20 | Jungle | Early | E9 | JunglePass / runners / tunnels / RNG |
| 21 | Mud Caves To Grass | Mid | E9 | jungle scan / SpreadGrass / RNG |
| 22 | Full Desert | Mid | E9 | full cell data / retained bounds / RNG; general StructureMap consumers remain open |
| 23 | Mushroom Patches | Mid | E9 | ShroomPatch / roots / grass / cleanup / centres / full cells / RNG |
| 24 | Marble | Mid | E9 | slabs / slopes / speleothems / full cells / structures / RNG |
| 25 | Granite | Mid | E9 | pressure / lava / cleanup / decoration / full cells / structures / RNG |
| 26 | Floating Islands | Mid | E9 | full cells / RNG / retained XY, style, lake; later houses remain open |
| 27 | Dirt To Mud | Mid | E9 | whole-world runner population / ignored sand / mud velocity / RNG |
| 28 | Silt | Mid | E9 | two depth/population bands / wall rejection / runner / RNG |
| 29 | Shinies | Mid | E9 | twelve ordered ore bands / runner / RNG; special seeds unproven |
| 30 | Webs | Mid | E9 | retained mountain caves / ceiling-side search / runner / full cells / RNG |
| 31 | Underworld | Mid | E9 | All9 complete prefixes match full cells, next RNG and ordered dresser slots/anchors/empty contents;24 contact kernels; ordinary profile only |
| 32 | Corruption | Mid | E9 | ordinary Corruption full prefix cells / RNG; Crimson complete-pass synthetic fixtures; special seeds remain open |
| 33 | Lakes | Mid | E9 | Full cells / RNG / ordered lake columns; ordinary prefixes |
| 34 | Slush | Mid | E9 | Full cells / retained snow bounds / no RNG; ordinary prefixes |
| 35 | Dual Dungeons Dither Snake | Dungeon | N | dualDungeons |
| 36 | Dungeon | Dungeon | R16 | complete ordinary pass:16 identical real-prefix inputs; cells / ordered chests+prefixes / Old Man / RNG;24 flat cases +14 extra seeds; independent end-to-end prefix/special seeds remain open |
| 37 | Mountain Caves | Dungeon | C | Mountinater / complete-pass snapshot |
| 38 | Beaches | Dungeon | C | ocean depth / slopes / pass snapshot |
| 39 | Gems | Dungeon | C | 18 official delegate fixtures / remaining prefix proof |
| 40 | Gravitating Sand | Dungeon | C | 18 official gap-fill fixtures / remaining prefix proof |
| 41 | Create Ocean Caves | Dungeon | P | OceanCave / entrance / RNG |
| 42 | Shimmer | Dungeon | P | ShimmerBiome / Aether geometry / RNG |
| 43 | Clean Up Dirt | Dungeon | C | 18 official wall-cleanup fixtures / remaining prefix proof |
| 44 | Pyramids | Dungeon | P | Pyramid / walls / descent / RNG |
| 45 | Dirt Rock Wall Runner | JungleStructure | P | wall runners / RNG |
| 46 | Living Trees | JungleStructure | P | GrowLivingTree / roots / room / RNG |
| 47 | Wood Tree Walls | JungleStructure | P | wall spread / roots / RNG |
| 48 | Altars | JungleStructure | P | placement / exclusions / RNG |
| 49 | Wet Jungle | JungleStructure | P | water placement / scan order |
| 50 | Jungle Temple | JungleStructure | P | temple graph / rooms / door / RNG |
| 51 | Hives | JungleStructure | P | HiveBiome / honey / RNG |
| 52 | Jungle Chests | JungleStructure | P | candidate rooms / frames / RNG |
| 53 | Settle Liquids | JungleStructure | P | QuickWater(3) / WaterCheck / quickSettle |
| 54 | Remove Water From Sand | PostSettle | R1 | surface scan / six tile types / boundaries |
| 55 | Oasis | PostSettle | P | PlaceOasis / banks / placement / RNG |
| 56 | Shell Piles | PostSettle | P | all source branches / decoration / RNG |
| 57 | Smooth World | PostSettle | C | WorldSmoother / whole-pass differential |
| 58 | Waterfalls | PostSettle | P | source placement / RNG |
| 59 | Ice | PostSettle | P | snow bounds / thin ice / RNG |
| 60 | Wall Variety | PostSettle | P | wall helpers / RNG |
| 61 | Life Crystals | PostSettle | C | placement attempts / framing / RNG |
| 62 | Statues | PostSettle | C | styles / placement attempts / RNG |
| 63 | Buried Chests | Chest | C | AddBuriedChest / styles / loot / RNG |
| 64 | Surface Chests | Chest | C | AddBuriedChest / surface eligibility / RNG |
| 65 | Jungle Chests Placement | Chest | C | candidate order / loot / RNG |
| 66 | Water Chests | Chest | C | AddBuriedChest / liquid eligibility / RNG |
| 67 | Spider Caves | LateStructure | P | SpiderBiome / webs / walls / RNG |
| 68 | Gem Caves | LateStructure | P | GemCave / RNG |
| 69 | Moss | LateStructure | P | moss selection / growth / RNG |
| 70 | Temple | LateStructure | P | temple finishing / traps / RNG |
| 71 | Cave Walls | LateStructure | C | CaveWallVariety / regions / RNG |
| 72 | Jungle Trees | LateStructure | C | tree grower / placement / RNG |
| 73 | Floating Island Houses | LateStructure | C | IslandHouse / furniture / loot / RNG |
| 74 | Quick Cleanup | SurfaceFinish | P | scan order / cleanup rules |
| 75 | Pots | SurfaceFinish | C | styles / locations / RNG |
| 76 | Hellforge | SurfaceFinish | C | fort geometry / placement / RNG |
| 77 | Spreading Grass | SurfaceFinish | P | SpreadGrass / recursion |
| 78 | Surface Ore and Stone | SurfaceFinish | P | surface scan / TileRunner / RNG |
| 79 | Place Fallen Log | SurfaceFinish | C | PlaceFallenLog / placement / RNG |
| 80 | Traps | SurfaceFinish | C | trap families / wiring / RNG |
| 81 | Piles | SurfaceFinish | C | styles / placement / RNG |
| 82 | Spawn Point | SurfaceFinish | C | source spawn search / safety |
| 83 | Grass Wall | SurfaceFinish | P | wall spread / RNG |
| 84 | Guide | StartingNpc | C | Guide / seed profiles / NPC metadata |
| 85 | Sunflowers | Vegetation | C | placement / RNG |
| 86 | Planting Trees | Vegetation | C | tree grower / planting attempts / RNG |
| 87 | Herbs | Vegetation | C | biome selection / styles / RNG |
| 88 | Dye Plants | Vegetation | C | styles / substrate / placement / RNG |
| 89 | Webs And Honey | Vegetation | P | web / honey scans / RNG |
| 90 | Weeds | Vegetation | P | plant selection / RNG |
| 91 | Glowing Mushrooms and Jungle Plants | Vegetation | P | growth helpers / RNG |
| 92 | Jungle Plants | Vegetation | P | growth helpers / RNG |
| 93 | Vines | Vegetation | P | biome selection / lengths / RNG |
| 94 | Flowers | Vegetation | P | styles / growth / RNG |
| 95 | Mushrooms | Vegetation | P | growth helpers / RNG |
| 96 | Gems In Ice Biome | UndergroundFinish | P | snow bounds / gems / RNG |
| 97 | Random Gems | UndergroundFinish | P | placement / RNG |
| 98 | Moss Grass | UndergroundFinish | P | moss growth / RNG |
| 99 | Muds Walls In Jungle | UndergroundFinish | P | wall scans / RNG |
| 100 | Larva | UndergroundFinish | C | hive anchors / framing / RNG |
| 101 | Micro Biomes | MicroBiomes | C | TrackGenerator / houses / all biome helpers |
| 102 | Settle Liquids Again | Final | P | QuickWater / WaterCheck / quickSettle |
| 103 | Cactus, Palm Trees, & Coral | Final | C | growers / planting distribution / RNG |
| 104 | Tile Cleanup | Final | P | scan order / framing rules |
| 105 | Lihzahrd Altars | Final | C | temple anchors / placement / RNG |
| 106 | Water Plants | Final | P | liquid/substrate selection / RNG |
| 107 | Stalac | Final | C | styles / placement / RNG |
| 108 | Remove Broken Traps | Final | C | trap validation / source scan |
| 109 | Final Cleanup | Final | P | all cleanup branches / persisted state |

## Priorities from actual differences

- [x] Terrain: move both liquid rolls from reseeded TerrainLayers to the end of Terrain. Small seed1458 changes water778→745 and lava846→801. TerrainLayers transfers retained values only; missing state throws rather than rerolling.
- [x] Remove Water From Sand: replace whole-map embedded active-sand draining with the source surface scan: x400..width-401, y100..<worldSurface-1, first active tile, six types53/396/397/404/407/151, upward clearing through100. Underground/ocean liquid is untouched; liquid kind is preserved.
- [x] Ordinary Dunes-through-Mud-Caves-To-Grass prefix: 162 independent official cell/RNG/pyramid checkpoints retained. This closes the documented nine-case differential, not all branches or retained state.
- [ ] Early side tables: retain and verify remaining GenVars/StructureMap consumers. Full Desert now matches its documented nine-case complete-cell/RNG differential; that does not close missing early side tables or downstream consumers.
- [x] Ordinary Dungeon: source entrance/stairs, descent, rooms, doors/platforms, locked/biome chests, furniture, paintings, lights, banners and traps; complete-pass cells/chests/anchor/RNG match the `R16` differential above.
- [ ] Dungeon beyond that boundary: independent end-to-end prefix equality, all remaining GenVars/StructureMap consumers and special seeds; final-map/client-playthrough equality is not inferred from isolated pass equality.
- [ ] Pyramids, Living Trees, Crimson, Oasis, Aether, Jungle Temple/Hives: exact builders, walls, entrances, rooms and downstream state; do not paint a corrective shell over a wrong shape.
- [ ] Floating Islands: preserve proven CloudIsland/CloudLake components; separately verify source placement/IslandHouse/furniture/loot and the result after later passes.
- [ ] Underworld: preserve proven roof/basin/runner/QuickWater components; verify the full sequence with HellFort, Hellforge and decoration. Final visible lava is not equivalent to the early basin probe.
- [ ] Settle Liquids/Again: the first pass still uses approximate sweeps and the second vertical column compaction. These are not vanilla QuickWater + WaterCheck + bounded quickSettle orchestration; only embedded-liquid clearing was previously closed. Implement exact orchestration without an alternative live authority path.
- [ ] Micro Biomes: preserve corrected rail frames/clearance; port TrackGenerator origin/route/history/tunnel/smoothing and other biome builders. House budgets/frames do not establish source topology.
- [ ] Every remaining C/P row: prove placement attempts, RNG, frames, side tables, loot and cleanup order. Do not widen differential budgets to accommodate a changed world.
- [ ] Whole-world matrix: corruption/crimson × Small/Medium/Large × multiple seeds; separate special-seed branches. Official loading, structural budgets, playthrough and exact equality are distinct gates.

## Reproducible evidence

`TerrainReference1458Tests` retains nine official hashes for seeds1458/42/8675309 and exercises production Terrain and its bridge. `SurfaceSandDrain1458Tests` retains the official600000-cell hash with boundary and negative cases. Official Linux executable SHA-256: `4B87890AC53D40F61DB5F928693A379ACF4CCBD8ED3B47EB32FB096F145DF034`. Local reflection probes/decompile stay ignored in .cache; production does not load a Terraria assembly.

Whole-world fingerprints are not yet required to match by the [reference differential gate](vanilla-worldgen-reference-differential.md). A green gate must not be relabeled 1:1. See also [visual debt](../roadmap/vanilla-worldgen-visual-parity-audit-2026-08-31.md).

### Current and historical whole-world measurements

Current Marble+Granite batch: component86/86, affected221/221 and restored full4515/4515 pass with zero errors/skips; Release has zero warnings/errors. Windows NativeAOT and all six smokes pass. Fresh Small/Classic/Corruption1458 loads in both servers; official exits without saving, runtime saves only its fixture checkpoint, and both owned servers are stopped. Same-reference, unchanged-budget comparison: tileL1=0.275338 (previous0.289948), wallL1=0.532758 (previous0.591555), active ratio=0.964576, liquid ratio=1.039093, silhouette NMAE=0.007985, p95=0.044167, correlation=0.922362. Spawn delta(+1,0), dungeon delta(+85,-6), chests181→142 and town NPCs2→2 remain different. Candidate fingerprint `7aec2ba77dd20f939008743fb06cb052ace6ac20565e629f3ceff8f03113cbd3`; ignored evidence `stone-world-compare.json/log`. Linux NativeAOT, remote CI and official-client playthrough remain unverified. All measurements below are historical; no budget was widened and no whole-world equality is claimed.

Latest Mushroom Patches candidate: restored focused50/50 and affected147/147 pass; Windows NativeAOT publish and all six smokes pass. A fresh Small/Classic/Corruption1458 loads in TerraRuntime and official1.4.5.8; the official server exits without saving. The same-reference comparison passes unchanged budgets: tileL1=0.289948 (previous0.308584), wallL1=0.591555 (previous0.589932), active ratio=0.966202, liquid ratio=1.036840, silhouette NMAE=0.007992, p95=0.044167 and correlation=0.922356. Spawn delta(+1,0), dungeon delta(+85,-6), chests181→142 and town NPCs2→2 remain different. Candidate fingerprint `8c28f8a9c64587660d1d4a6f92167a713be23822d95fa22f9ffdafd9294a7d1d`; ignored evidence `mushroom-world-compare.json/log`. The wall histogram has slightly worsened; this is recorded rather than hidden by changed budgets. Full Release4475/4475 passes with zero errors/skips; the build has zero warnings/errors and documentation/diff gates pass. Linux NativeAOT and official-client playthrough remain unverified. Measurements below describe earlier candidates.

Latest Full Desert candidate: Release build has zero warnings/errors; full suite4457/4457 and the final eight framing-fixture rechecks pass. All six Windows NativeAOT smokes pass. Fresh Small/Classic/Corruption1458 loads in TerraRuntime and official1.4.5.8; the official server exits without saving. Compared with the same untouched official reference, tileL1=0.308584 (previous0.355396), wallL1=0.589932 (previous0.719732), active ratio=0.964312, liquid ratio=1.040876, silhouette NMAE=0.007998, p95=0.044167 and correlation=0.922285. Spawn delta(+1,0), dungeon delta(+85,-6), chest count181→142 and town NPC count2→2 remain distinct from full parity. Structural budgets pass unchanged. Candidate fingerprint `43ae2acd6b88b05195354560eea3fe458503cf4d9c5119ba51e08d0111bc0786`; ignored evidence `full-desert-world-compare.json/log`. Linux NativeAOT and official-client playthrough remain unverified. All paragraphs below describe earlier candidates.

After the Obsidian-door correction, the final native Small1458 was generated again and loaded by both TerraRuntime and official1.4.5.8. Its fingerprint and the following Jungle/surface metrics are unchanged (`worldgen-prefix-world-compare.json/log`); the separate Medium42 regression covers the damaged door. Final Release suite4354/4354, affected133/133 and six Windows NativeAOT smokes pass. Linux NativeAOT and GUI playthrough are not claimed.

The newer Jungle/surface continuation also generated a fresh native Small1458 and loaded its untouched pre-checkpoint copy in official1.4.5.8. Reusing the same independent official reference, current tileL1=0.355396, wallL1=0.719732, liquidratio=1.081689, silhouettecorr=0.926584, chests181→142 and dungeon delta(+85,-6). Existing budgets pass unchanged; candidate fingerprint `337af1242e5fbf1cc79412b2000a227b4e3ef2892dff8646a722408410762e3a` remains different. Evidence: ignored `jungle-surface-world-compare.json/log`. The older measurements below are retained for provenance, not current-map claims.

Fresh Small/Classic/Corruption seed1458 was generated by the final Windows NativeAOT candidate and, independently, official Windows1.4.5.8 using copied seed `1.1.1.1458`. Candidate cold startup reached listening; its pre-checkpoint copy also reached official `Server started` and exited via `exit-nosave`. A newly generated official reference was compared against that unchanged candidate copy, not a runtime-checkpointed world.

The existing `WorldCompare --enforce` structural budgets passed without modification. Nevertheless, tile histogram L1=0.390148, wall L1=0.759073, liquid ratio=1.139229, silhouette correlation=0.926982; chest count181→145 and dungeon anchor delta(+69,+6). Both worldSurface337 and rockLayer457 match. Source fingerprint `a73ec6c799c5e6ce3f9684377e97b07770caf19f4369c63d237d20c4fb21fa26` differs from candidate `39be088b0f443925ecab82880e378c27d7c0a1e2415896e0fc265a5c475c2085`. This is measured remaining whole-world debt, not equality or GUI playthrough acceptance. Logs/worlds/report are ignored `.cache/terrain-parity-*`; this additional local seed is not a claim that remote CI's8675309 case ran.

Selected active-tile totals localize the next investigations; they are not target quotas to paint into the map:

| Material (source IDs) | Official | Candidate |
| --- | ---: | ---: |
| Dungeon bricks (41/43/44) | 84043 | 120549 |
| Sandstone/hardened sand (396/397) | 56692 | 41297 |
| Snow/ice (147/161) | 135503 | 111249 |
| Living wood/leaves (191/192) | 4603 | 783 |
| Lihzahrd brick (226) | 25684 | 1121 |
| Ebonstone (25) | 51746 | 30363 |
| Ash (57) | 386135 | 382736 |
| Cloud/rain cloud (189/196) | 8053 | 8049 |

The Temple and Living Tree differences make their builders high-priority follow-ups. Similar Ash/Cloud totals do not prove their final geometry; more Dungeon bricks do not establish a larger or correctly connected interior.
