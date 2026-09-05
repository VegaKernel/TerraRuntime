# Terraria 1.4.5.8 vanilla world-generation parity

`terraruntime:flat` remains a separate minimal deterministic generator. Vanilla parity work happens only under the existing `terraruntime:vanilla` generator identity.

## Current migration model

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

## Acceptance

`.github/workflows/terraria-vanilla-generated-world-acceptance.yml` performs four checks for the canonical small world:

1. builds TerraRuntime and runs the focused worldgen tests;
2. creates a real `.wld` using `terraruntime:vanilla`;
3. loads it through TerraRuntime's world verifier;
4. boots pinned TerrariaServer 1.4.5.8 with that world and requires the server listener to open.

The existing flat-world acceptance remains unchanged.

## Remaining parity work

Reset and Terrain now have source-backed ordinary-world slices plus a bounded pure Remix branch, but the current provider still contains compatibility implementations for biomes, caves, ores, dungeon generation and later special-seed modifiers. The pinned 1.4.5.8 catalog contains 109 registered passes. Completing ordinary or Remix parity means replacing those compatibility groups with source-backed pass sequences and then adding reference-world comparison, not adding more approximate heuristics to the compatibility layer.
