# Terraria 1.4.5.8 vanilla world-generation parity

`terraruntime:flat` remains a separate minimal deterministic generator. Vanilla parity work happens only under the existing `terraruntime:vanilla` generator identity.

## Current migration model

The built-in vanilla generator is intentionally migrated pass by pass. `SourceBackedVanillaWorldGenerationProvider1458` delegates to the previous compatibility provider, then replaces individual pass implementations while retaining the same pass IDs and dependency graph.

This prevents a half-ported pass set from being presented as a second generator and keeps existing special-seed compatibility behavior available while source-backed implementations are introduced.

## Shared random stream

TerrariaServer 1.4.5.8 creates one `UnifiedRandom` for world generation and advances it across generation passes. `WorldGenerationRngMode.VanillaSharedRng` now follows that lifetime: the runtime constructs one `VanillaUnifiedRandom1458` adapter for the execution plan and reuses it for every vanilla-shared pass.

This is a prerequisite for later source-backed Jungle, caves, ores, structures and decoration. Re-seeding before every pass produced deterministic output, but it could never reproduce Terraria's pass-to-pass random consumption.

## Terrain parity slice

The first source-backed replacement is `terraria:1.4.5.8/Terrain` for ordinary seeds on Terraria's canonical world dimensions:

| Size | Tiles |
| --- | ---: |
| Small | $4200 \times 1200$ |
| Medium | $6400 \times 1800$ |
| Large | $8400 \times 2400$ |

The implementation ports the pinned TerrainPass surface-feature state machine, dirt/stone column fill, surface history retargeting, six-tile rock-layer quantization and `FlatBeachPadding = 5` semantics. Terrain tiles use frame coordinates $(-1,-1)$ as the vanilla pass does.

Non-canonical dimensions and special/secret seeds currently use the previous compatibility Terrain pass. This is deliberate. Vanilla Terrain depends on prerequisite state produced by `WorldGen.Reset`, especially randomized beach boundaries and random-stream consumption before the first registered generation pass. That bootstrap is not yet source-exact, so this stage does **not** claim reference-world or byte-for-byte parity.

For canonical ordinary worlds the temporary bootstrap uses a conservative beach boundary of 350 tiles on each side. Replacing that bootstrap with source-backed `WorldGen.Reset` state is the next Terrain parity step.

## Metadata ownership

The Terrain replacement publishes its source-shaped world-surface and rock-layer values to the candidate metadata workspace. The compatibility metadata pass still computes spawn, dungeon anchor and seed-profile persistence, after which the source-backed layer values are restored. This lets later passes migrate independently without losing the Terrain result.

## Acceptance

`.github/workflows/terraria-vanilla-generated-world-acceptance.yml` performs four checks for the canonical small world:

1. builds TerraRuntime and runs the focused worldgen tests;
2. creates a real `.wld` using `terraruntime:vanilla`;
3. loads it through TerraRuntime's world verifier;
4. boots pinned TerrariaServer 1.4.5.8 with that world and requires the server listener to open.

The existing flat-world acceptance remains unchanged.

## Remaining parity work

The current provider still contains compatibility implementations for biomes, caves, ores, dungeon generation and secret-seed modifiers. The pinned 1.4.5.8 pass catalog contains 109 registered passes, so completing vanilla parity means replacing those compatibility groups with the source-backed pass sequence rather than merely adding more heuristics to the seven-pass compatibility layer.
