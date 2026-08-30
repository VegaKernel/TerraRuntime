# Vanilla world generation: early 1.4.5.8 pipeline

`terraruntime:vanilla` now expands the first generation stage instead of jumping from `Terrain` into the old aggregate `Biomes` pass.

For ordinary canonical Terraria worlds the runtime executes the early pass graph in source order:

`Reset → Terrain → TerrainLayers → Dunes → OceanSand → SandPatches → Tunnels → MountCaves → DirtWallBackgrounds → RocksInDirt → DirtInRocks → Clay → SmallHoles → DirtLayerCaves → RockLayerCaves → SurfaceCaves → WavyCaves → GenerateIceBiome → Grass → Jungle`.

The shared Terraria `UnifiedRandom` stream is used by the passes that participate in the source shared stream. Generation-local passes use `IsolatedDeterministic` and therefore cannot accidentally advance the shared vanilla RNG.

## Terrain completion

The bridge after `Terrain` publishes the already computed `WorldGen.Reset` state for the early passes and completes the layer state used by later world generation. In particular, ordinary 1.4.5.8 semantics consume the two post-terrain RNG calls for `waterLine` and `lavaLine` instead of inventing fixed depth thresholds.

## Early terrain mutation

The stage-one pipeline now owns source-shaped implementations for dunes, ocean sand, sand patches, tunnels, mount caves, dirt/rock mixing, clay, small holes, dirt- and rock-layer caves, surface caves, the ice biome, grass and the first Jungle pass. Hot tile mutation uses the candidate world's contiguous `WorldTileStore` directly; this keeps the large Jungle `TileRunner` path practical without publishing candidate writes as live-world dirty work.

The ordinary `Wavy Caves` branch is an explicit no-op because its mutations belong to special seed modes that are still handled by the compatibility path.

## Compatibility boundary

After source-backed Jungle, the old `Biomes` implementation remains only as a residual compatibility layer for world features not yet migrated. It receives a private compatibility RNG so it cannot corrupt the source shared stream, and its broad tile-59/tile-60 jungle repaint is filtered out so it cannot overwrite the source-backed Jungle.

Caves, ores, dungeon, secret-seed compatibility and final metadata remain downstream migration work. Special seeds and non-canonical dimensions continue to use the previous compatibility plan unchanged.

## Verification

The generated-world acceptance workflow remains the release gate: build, focused worldgen contracts, real canonical `terraruntime:vanilla` `.wld` generation, TerraRuntime loader verification, then a boot test with the pinned official TerrariaServer 1.4.5.8 binary.
