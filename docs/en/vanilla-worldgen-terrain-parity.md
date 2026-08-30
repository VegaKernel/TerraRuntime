# Terraria 1.4.5.8 vanilla world-generation parity

`terraruntime:flat` remains a separate minimal deterministic generator. Vanilla parity work happens only under the existing `terraruntime:vanilla` generator identity.

## Current migration model

The built-in vanilla generator is intentionally migrated pass by pass. `SourceBackedVanillaWorldGenerationProvider1458` keeps the previous compatibility passes, inserts source-backed prerequisites where required, and replaces individual pass implementations under the same generator identity.

This prevents a half-ported pass set from being presented as a second generator and keeps existing special-seed compatibility behavior available while source-backed implementations are introduced.

## Shared random stream

TerrariaServer 1.4.5.8 advances one world-generation `UnifiedRandom` stream through bootstrap and generation work. `WorldGenerationRngMode.VanillaSharedRng` follows that lifetime: the runtime constructs one `VanillaUnifiedRandom1458` adapter for the execution plan and reuses it for every vanilla-shared pass.

This is a prerequisite for source parity. Re-seeding between passes can be deterministic, but it cannot preserve Terraria's cross-pass RNG state.

## Source-backed Reset bootstrap

For ordinary seeds on Terraria's three canonical world sizes, the plan now starts with `terraria:1.4.5.8/Reset`. The bootstrap consumes the pre-Terrain RNG sequence and retains the generated state required by later passes, including:

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

## Terrain parity slice

`terraria:1.4.5.8/Terrain` is source-backed for ordinary seeds on Terraria's canonical world dimensions:

| Size | Tiles |
| --- | ---: |
| Small | $4200 \times 1200$ |
| Medium | $6400 \times 1800$ |
| Large | $8400 \times 2400$ |

The implementation ports the pinned TerrainPass surface-feature state machine, dirt/stone column fill, surface-history retargeting, six-tile rock-layer quantization and `FlatBeachPadding = 5` semantics. Terrain now consumes the randomized beach boundaries produced by the preceding Reset bootstrap instead of a fixed compatibility value.

Non-canonical dimensions and special/secret seeds still use the previous compatibility Terrain path. Their Reset branches are not yet claimed source-exact, and the source-backed Reset pass intentionally consumes no additional RNG for those compatibility cases.

## Metadata ownership

The Terrain replacement publishes its source-shaped world-surface and rock-layer values to the candidate metadata workspace. The compatibility metadata pass still computes spawn, dungeon anchor and seed-profile persistence, after which the source-backed layer values are restored. Reset state is retained internally so later Jungle, desert, ocean, structure and background ports can consume the same initial choices rather than rerolling them.

## Acceptance

`.github/workflows/terraria-vanilla-generated-world-acceptance.yml` performs four checks for the canonical small world:

1. builds TerraRuntime and runs the focused worldgen tests;
2. creates a real `.wld` using `terraruntime:vanilla`;
3. loads it through TerraRuntime's world verifier;
4. boots pinned TerrariaServer 1.4.5.8 with that world and requires the server listener to open.

The existing flat-world acceptance remains unchanged.

## Remaining parity work

Reset and Terrain now have source-backed ordinary-world slices, but the current provider still contains compatibility implementations for biomes, caves, ores, dungeon generation and secret-seed modifiers. The pinned 1.4.5.8 catalog contains 109 registered passes. Completing parity means replacing those compatibility groups with the source-backed pass sequence and then adding reference-world comparison, not adding more approximate heuristics to the compatibility layer.
