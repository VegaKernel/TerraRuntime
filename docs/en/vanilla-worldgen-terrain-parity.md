# Terraria 1.4.5.8 vanilla world-generation parity

Lakes/Slush acceptance is complete for the bounded ordinary matrix: **31 E9 stages / 279 checkpoints**, with **43P + 30C** audit rows unfinished. Release `4908/4908`, restored focused `41/41`, Windows NativeAOT, six smokes and fresh Small/Classic/Corruption1458 loading in both servers pass. Full cell/RNG and tunnel/lake/snow comparisons match all nine cases. Same-reference tile L1 `0.196460` and wall L1 `0.458101` pass unchanged budgets, but do not prove whole-world equality. The empty-tick test now warms its measured loop without changing workload or byte limit; injected allocation still fails, and earlier failures remain in agent memory. Next is Dungeon room/hall parity within the existing graph. Linux NativeAOT, special seeds and client playthrough remain unverified. Earlier WIP paragraphs below are historical.

Slush joins the Lakes batch: the existing Ice Biome pass now retains exact row bounds; Slush scans those bounds without RNG, converting stored Silt/Stone and Mud only when no active jungle material lies within the source radius. Random slush blobs are removed. Three official whole-pass fixtures and focused boundary/field-preservation tests pass. Negative controls detect six lake-water-retention and four invalid Slush activity-gate regressions. Three real Small prefixes through both stages match cells/RNG and side tables; larger-prefix/full/native acceptance is pending. Current accepted audit counts remain29E9,45P+30C until those gates finish.

New Lakes WIP: source surface placement and SonOfLakinater replace the old underground ellipses. The existing Tunnels pass retains its original middle-column anchors for lake spacing; lake choices preserve mountain-cave, desert, sand and evil-biome exclusions. Twelve official basin kernels and six full Lakes delegate fixtures match normalized cells, next RNG and ordered lake columns. Existing grass recursion is shared with lake carving, not duplicated; unknown material/tree/framing semantics fail closed. Current integrated/full/native gates are in agent memory. No Lakes stage promotion yet; the Corruption acceptance below predates this batch.

Current accepted ordinary Corruption prefix: nine official Small/Medium/Large cases match full cells and next RNG; retained tests cover29 stages/261 checkpoints. Stage32 is now bounded E9, with45P+30C rows still unfinished. Full Release4870/4870, extended prefix/grass/caves85/85, build0/0, WindowsNativeAOT+six smokes and freshSmallCorruption dual-server load pass. Same-reference tileL1 `0.195909`, wallL1 `0.461801` pass unchanged budgets but are not zero. Crimson real-prefix, special-seed, full-world and client parity remain separate obligations. Prior WIP notes below describe the path to this acceptance; next work is Lakes.

Corruption component WIP: the former generic chasms and deep replacement strip are removed. Ordinary main/sideways geometry, repeated terminal altar searches, shared orb placement, column cooldowns, shallow conversion and per-orb-cell cleanup now follow source ordering. Thirty-six official cave fixtures and six complete Corruption pass fixtures match all normalized cells and next RNG on identical synthetic inputs. Existing ore, altar, surface and framing owners are reused; no alternate runtime authority is introduced. Integrated world generation and new full/native acceptance are tracked in agent memory. No full-world prefix equality or audit promotion is claimed.

Surface component acceptance: `4827/4827` Release tests, zero build warnings/errors, Windows NativeAOT and six smokes passed. A newly generated Small/Classic/Crimson1458 world loads in both TerraRuntime and official 1.4.5.8. This does not establish Linux NativeAOT, client playthrough, full-world equality or stage completion. The next coherent slice is ordinary Corruption chasm/side-tunnel/orb ordering, verified against the official executable before replacing the existing approximation.

Crimson branch evidence expanded: six complete official-pass fixtures on identical input/state match all normalized cells and next RNG, including two regions, mixed materials/desert walls, surface conversion, altar search and deferred hearts. This is not full-world prefix equality. Grass evidence is now sixteen cases, including pre-existing cobwebs; their generation framing is a verified no-op in the existing framing owner. Canonical Crimson generation passes for all three sizes; affected tests `94/94`, omitted-surface negative control `6/6` failures. Current full/native gates are still tracked in agent memory.

New surface WIP: Crimson now uses ordered jungle-column conversion, the shallow source random-walk band and depth-limited Dirt/Mud evil grass instead of the broad deep replacement strip. Twelve independent initialized official grass fixtures match all cell fields and next RNG; this does not yet prove the complete surface/pass sequence. Existing generation framing is reused; unsupported tree/object effects abort rather than approximate. Corruption geometry and its old surface orchestration remain open. Current new-batch gates are in agent memory; altar acceptance below predates this change.

Altar component acceptance: **4804/4804** Release tests passed with allocation tracing attached; build has no warnings/errors. Windows NativeAOT, six smokes, fresh Small/Classic/Corruption1458 and Crimson1458 loading in both servers pass. Fixed-count negative control fails all nine late-pass regressions. Same-reference budgets pass: tile L1 `0.196990` (previous `0.196269`, slightly worse), wall L1 `0.520040` (previous `0.523785`). No monotonic whole-world improvement, stage completion, Linux NativeAOT or client playthrough is claimed. Allocation-test isolation alone did not eliminate instability: a subsequent empty-tick test failed before the traced full run passed; the exact failing allocation stack remains unverified.

Measurement isolation: the NPC-dispatch and server-player steady-state allocation test classes run in a nonparallel collection. Their workloads, warmup and byte limits are unchanged; concurrent canonical world generation must not invalidate a warmed shared-pool measurement. Before isolation, two full runs failed different allocation gates, while the isolated NPC/altar set passed. This does not establish a production allocation regression or its exact allocation stack. Current full revalidation remains recorded below/in agent memory.

Altar batch WIP (2026-09-08): the existing late Altars pass now uses the ordinary area-based count, original coordinate rejection/attempt order, ocean and retained Shimmer-position exclusion, and shared generation-only `Place3x2(26)` rules. Crimson regions now search for altars before deferred hearts. Direct official placement comparisons cover all $18096$ material/shape/actuation/style combinations; nine independent late-pass fixtures match complete cells and next RNG. The Shimmer geometry itself and complete evil surface/Corruption geometry remain open. This is component evidence, not a stage promotion or whole-world parity claim; current acceptance is tracked in agent memory.

`terraruntime:flat` remains a separate minimal deterministic generator. Vanilla parity work happens only under the existing `terraruntime:vanilla` generator identity.

## Current migration model

Crimson cave component acceptance: **4783/4783** Release tests, zero build warnings/errors, Windows NativeAOT plus six smokes and a NativeAOT-created Small/Classic/Crimson1458 file loaded by both servers. All three canonical Crimson generation cases retain real hearts after finalization. A failing CPU-budget test fixture was corrected to measure thread CPU instead of wall time; runtime budgets/assertions were not weakened. Linux NativeAOT, actual-client playthrough and whole-pass equality remain unverified; stage32 stays open.

Crimson cave geometry WIP (2026-09-08): ordinary Crimson now has its own source-backed inclined trunk, central chamber, branching veins, entrance excavation and deferred hearts, rather than the generic Corruption-like vertical tunnel. Nine direct official 1.4.5.8 comparisons match all normalized cell fields, next RNG and ordered heart coordinates, including protected columns and two caves sharing one heart-placement phase. This establishes the geometry component, not whole-pass parity: surface grass conversion, altars and Corruption geometry remain open. Current batch gates are recorded in agent memory; preceding acceptance below belongs to the earlier placement slice.

Local acceptance for the bounded evil-placement/excavation slice: Release **4770/4770**, zero build warnings/errors, Windows NativeAOT plus six smokes and fresh Small/Classic/Corruption1458 loading in both servers pass. Same-reference budgets remain unchanged and pass: tile L1 `0.196269` (previous `0.201211`), wall L1 `0.523785` (previous `0.530400`). Chests still differ `181→144`; no whole-world equality or stage completion is claimed. An earlier stalled test run was stopped and recorded; its cause is not claimed diagnosed. Linux NativeAOT and real-client playthrough remain unverified.

Evil-biome placement/excavation WIP (2026-09-08): the existing Corruption pass now scans actual surface jungle/snow bounds and uses the retained desert rectangle, asymmetric source edge draws and per-selection exclusion relaxation. The fixed-coordinate fallback is removed; a bounded exhausted search aborts generation. Chasm excavation respects dungeon/cracked-brick and dungeon-wall protection and clears only activity, preserving other tile fields; ore/orb exclusions apply only to Corruption, not Crimson. Direct official `CanEvilReplace` calls match all $553436$ known material/wall/activity combinations, retained as a golden regression. This does **not** close the still-shared approximate Corruption/Crimson geometry, surface grass conversion, hearts/orbs/altars or whole-world parity. Full batch acceptance remains tracked in agent memory; the stage stays open.

Full registration audit (2026-09-08): [all109 entries and open parity targets](vanilla-worldgen-pass-audit.md). Terrain now owns its final water/lava draws; the separately reseeded TerrainLayers bridge must only transfer the result. Nine independent official ordinary Terrain fixtures (three seeds × Small/Medium/Large) match every type/frame/active cell, both liquid lines and next RNG. Surface-sand drainage now follows the source surface scan rather than whole-map embedded-liquid removal; a separate official fixture matches all600000 liquid amount/kind pairs. Neither result establishes full-world 1:1 or exact downstream settling.

Ordinary sky geometry correction (2026-09-08): the existing FloatingIslands pass now calls SkyIsland1458 for CloudIsland/CloudLake. Ten direct official Linux1.4.5.8 binary comparisons across five seeds match block types/activation, walls, liquid amount/kind and next RNG. These are geometry fixtures, not whole-world seed parity or cosmetic frame equality. Source cloud underside, distinct lake carving/depth/drift, held-water checks, rain pockets and wall-framing RNG replace the old approximate helpers and artificial shallow fill. Anchor selection uses the source width-minus-one budget, strict spawn/spacing exclusions and200..worldSurface ground scan, without fabricated fallback anchors. Special seeds, later house placement/contents, final-world visual parity and other biomes remain open. Dungeon entrance still uses a simplified shape; connected graph/load acceptance does not close that geometry debt.

The built-in vanilla generator is intentionally migrated pass by pass. `SourceBackedProvider1458` keeps the previous compatibility passes, inserts source-backed prerequisites where required, and replaces individual pass implementations under the same generator identity.

This prevents a half-ported pass set from being presented as a second generator and keeps existing special-seed compatibility behavior available while source-backed implementations are introduced.

## Shared random stream

`WorldGenerationRngMode.VanillaSharedRng` means the exact Terraria world-generation RNG API is shared by all work **inside one pass**. Pinned TerrariaServer 1.4.5.8 `WorldGenerator.RunPass` creates `Main.rand = new UnifiedRandom(_seed)` before each enabled pass, so TerraRuntime starts every vanilla-shared pass from that pass-local seed and preserves call order only within its pass. Carrying state between registered passes would be a compatibility bug, as would parallelizing RNG-sensitive work inside a pass.

## Source-backed Reset bootstrap

For ordinary seeds, and for the pure `Don't Dig Up`/Remix profile, on Terraria's three canonical world sizes, the plan starts with `terraria:1.4.5.8/Reset`. The bootstrap consumes the pre-Terrain RNG sequence and retains the generated state required by later passes, including:

- dungeon side and location;
- jungle and snow origins;
- randomized left and right beach boundaries;
- ore-tier choices;
- tree styles and transition positions;
- cave and surface background styles;
- moon style and selected fresh-world state;
- world-size-dependent generation counts.

The key beach configuration is source-pinned to `BeachBordersWidth = 275`, `BeachSandRandomCenter = 320`, `BeachSandRandomWidthRange = 20`, `BeachSandDungeonExtraWidth = 40`, and `BeachSandJungleExtraWidth = 20`.

A fixed seed checkpoint locks RNG consumption: for seed `1458` in a small $4200 \times 1200$ ordinary world, Reset produces left/right beach bounds `322 / 3830`, dungeon side `-1`, dungeon location `484`, and the next shared RNG value is `289143048`.

`tools/ci/probe_worldgen_reset.py` decompiles the pinned official TerrariaServer 1.4.5.8 `WorldGen.Reset` and rejects changes to the verified Reset constants, randomized beach construction, or tree/cave ordering. The dedicated `Terraria Worldgen Reset Contract` workflow runs that source contract together with the focused implementation tests.

The pure Remix branch is deliberately small and source-pinned: `Reset` substitutes hell-chest item `112` with `683`, and chooses the jungle origin from the `$20\%$`–`$35\%$` band rather than the ordinary `$15\%$`–`$30\%$` band. Zenith is not admitted merely because it includes Remix: it also turns on other special branches that have not been ported.

## Terrain parity slice

`terraria:1.4.5.8/Terrain` is source-backed for ordinary seeds and the pure `Don't Dig Up`/Remix profile on Terraria's canonical world dimensions:

| Size | Tiles |
| --- | ---: |
| Small | $4200 \times 1200$ |
| Medium | $6400 \times 1800$ |
| Large | $8400 \times 2400$ |

The implementation ports the pinned TerrainPass surface-feature state machine, dirt/stone column fill, surface-history retargeting, six-tile rock-layer quantization and `FlatBeachPadding = 5` semantics. Terrain now consumes the randomized beach boundaries produced by the preceding Reset bootstrap instead of a fixed compatibility value. For pure Remix it also uses Terraria's alternate surface-offset distribution and deep rock-layer initialization/ceiling.

Non-canonical dimensions, Zenith, combined special switches and secret switches still use the previous compatibility Terrain path. Their Reset branches are not yet claimed source-exact, and the source-backed Reset pass intentionally consumes no additional RNG for those compatibility cases. After pure Remix Terrain, all later source-shaped overlays still remain on the compatibility path; this is a bounded `Reset + Terrain` slice, not complete Remix world parity.

## Metadata ownership

The Terrain replacement publishes its source-shaped world-surface and rock-layer values to the candidate metadata workspace. The compatibility metadata pass still computes spawn, dungeon anchor and seed-profile persistence, after which the source-backed layer values are restored.

For source-backed ordinary and pure Remix terrain worlds, the Reset bootstrap is also transferred into `RuntimeWorldGenerationMetadataSnapshot`. Fresh `.wld` persistence now emits Reset-derived moon type, tree/cave transition positions and styles, primary and secondary background styles, cloud timer/count, wind, slime-rain countdown and pre-hardmode ore choices. Flat and custom generators leave this bootstrap absent and retain the conservative fresh-world defaults.

This persistence bridge matters to later pass work: Jungle, desert, ocean, structures and decoration can consume one Reset result during generation while the saved world retains the same initial choices after restart instead of silently reverting to compatibility defaults.

## Smooth World surface shaping

The ordinary canonical path now runs a clean-room implementation of TerrariaServer 1.4.5.8 `Smooth World` instead of the previous coordinate-based shaping heuristic. The pass preserves the two source-ordered scans inside the $20\,\text{tile}$ border, their shared-RNG decision points, exposed-edge erosion and gap filling, all four slope orientations, half-bricks, sand-family `SmoothSlope` normalization and orphan-slope correction.

Shape representation is explicit: `TileShape1458` owns the runtime mapping from full block and half-brick to the four vanilla slope values. `WorldSmoothingCatalog1458` separately owns the version-pinned tile capabilities for generation clearing, slope prevention, pounding exclusions, support-above guards, sand conversion and temporary cracked-brick solidity. Topology code therefore does not contain anonymous tile-identity chains or atlas/storage numbers.

Focused fixtures exercise the exact ordered RNG calls and every mutation family, including both top and bottom slope orientations. The canonical Small integration check requires all five non-full shape forms in the composed world. `tools/ci/probe_worldgen_smooth_world.py` independently compares the runtime capability sets and decision routes with the pinned decompile; `.github/workflows/terraria-worldgen-smooth-world.yml` recreates that evidence from the SHA-256-pinned official binary.

This closes the ordinary `Smooth World` shape-writer audit. It does not claim a byte-identical vanilla world: terrain silhouette, pass-exact upstream geometry, dungeon, ocean alignment and special-seed shaping remain separate parity boundaries.

## Ordinary Underworld terrain

The existing ordinary canonical `Underworld` pass replaces its solid lower Ash slab and periodic puddles with the source-ordered roof carve, lava-basin walk, vertical/horizontal Ash runners, liquid cavities and area-scaled Hellstone deposits. It retains the existing vegetation, forts, lighting, furniture and decoration owners. Small runners preserve protected/frame-important tiles, liquid-line gates, slope metadata and exact RNG draws, including overwritten velocity samples. Unsupported runner types/strengths are rejected.

The pass reuses the world's liquid flow owner for pre-Dungeon `QuickWater`, with the retained Terrain water line rather than the loading-only line. Its generation surroundings use Reset's pre-Aether shimmer origin and the source liquid-interaction cleanup, including the asymmetric above-shimmer rule. Loading remains a separate lifecycle state, not a second fluid algorithm.

Independent official-server comparison covers 21 small-runner fixtures with zero tile or next-RNG differences; seven results are retained as golden regressions. Canonical phase regressions also reject the former solid bottom row. This is not a claim of whole-world or special-seed equality.

Both later settling stages now include the solid-cell clearing slice of `WorldGen.WaterCheck`, including generation-only temporary solidity exceptions. That omission became observable after the Underworld flow started relocating liquids across the map. The validator remains strict. Full late-stage settling parity remains open: existing downward sweeps/column compaction do not implement the complete repeated `QuickWater / WaterCheck / quickSettle` sequence, post-Dungeon/Aether rules or all object side effects.

## Acceptance

`.github/workflows/terraria-vanilla-generated-world-acceptance.yml` performs four checks for the canonical small world:

1. builds TerraRuntime and runs the focused worldgen tests;
2. creates a real `.wld` using `terraruntime:vanilla`;
3. loads it through TerraRuntime's world verifier;
4. boots pinned TerrariaServer 1.4.5.8 with that world and requires the server listener to open.

The existing flat-world acceptance remains unchanged.

## Remaining parity work

Reset and Terrain now have source-backed ordinary-world slices plus a bounded pure Remix branch, but the current provider still contains compatibility implementations for biomes, caves, ores, dungeon generation and later special-seed modifiers. The pinned 1.4.5.8 catalog contains 109 registered passes. Completing ordinary or Remix parity means replacing those compatibility groups with source-backed pass sequences and then adding reference-world comparison, not adding more approximate heuristics to the compatibility layer.
