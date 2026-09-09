# Vanilla world generation: early 1.4.5.8 pipeline

`terraruntime:vanilla` now expands the first generation stage instead of jumping from `Terrain` into the old aggregate `Biomes` pass.

For ordinary canonical Terraria worlds the runtime executes the early pass graph in source order:

`Reset → Terrain → TerrainLayers → Dunes → OceanSand → SandPatches → Tunnels → MountCaves → DirtWallBackgrounds → RocksInDirt → DirtInRocks → Clay → SmallHoles → DirtLayerCaves → RockLayerCaves → SurfaceCaves → WavyCaves → GenerateIceBiome → Grass → Jungle`.

Pinned `WorldGenerator.RunPass` reseeds `Main.rand` before each enabled pass. `VanillaSharedRng` preserves the shared call order inside that pass, not a stream carried from the previous pass. Generation-local passes use `IsolatedDeterministic`.

## Terrain completion

Terrain itself consumes both final `waterLine`/`lavaLine` rolls from its already advanced RNG and publishes the result. TerrainLayers transfers that state and the retained Reset state without drawing random values; missing liquid state throws. Drawing in the separately reseeded bridge was a confirmed bug, corrected on2026-09-08. Nine independent official Terrain fixtures now pin cells, liquid lines and next RNG.

## Early terrain mutation

The stage-one pipeline now owns source-shaped implementations for dunes, ocean sand, sand patches, tunnels, mount caves, dirt/rock mixing, clay, small holes, dirt- and rock-layer caves, surface caves, the ice biome, grass and the first Jungle pass. Hot tile mutation uses the candidate world's contiguous `WorldTileStore` directly; this keeps the large Jungle `TileRunner` path practical without publishing candidate writes as live-world dirty work.

The ordinary `Wavy Caves` branch is an explicit no-op because its mutations belong to special seed modes that are still handled by the compatibility path.

## Compatibility boundary

This document describes the early overlay, not the whole shipping plan. `SourceBackedFinal1458` subsequently replaces the ordinary aggregate mutations through Final Cleanup; Biomes/Caves/Ores are non-mutating compatibility barriers there. Registered pass coverage does not prove exact geometry. The [complete audit](vanilla-worldgen-pass-audit.md) lists every owner and remaining debt. Existing special-seed/noncanonical paths remain separate; the bounded pure Remix slice is not full Remix parity.

## Verification

The generated-world acceptance workflow remains the release gate: build, focused worldgen contracts, real canonical `terraruntime:vanilla` `.wld` generation, TerraRuntime loader verification, then a boot test with the pinned official TerrariaServer 1.4.5.8 binary.
