# Built-in vanilla world generation

[Русский](../ru/vanilla-world-generation.md) · [World generation](world-generation.md) · [Roadmap](../roadmap/gameplay-worldgen-extensibility.md)

TerraRuntime exposes two runtime-owned world generators through the ordinary world-generation provider contract:

- `terraruntime:flat` remains the minimal deterministic baseline used for contract and persistence testing;
- `terraruntime:vanilla` is the built-in Terraria 1.4.5.8 compatibility generator being migrated toward source-backed parity.

The vanilla generator is clean-room runtime code. It does not embed TerrariaServer implementation source. Compatibility passes are replaced incrementally under the same generator identity.

## Execution model

```mermaid
flowchart LR
    Request["WorldGenerationRequest"] --> Resolve["Resolve seed profile"]
    Resolve --> Reset["Reset bootstrap"]
    Reset --> Terrain["Terrain"]
    Terrain --> Biomes["Biomes + oceans"]
    Biomes --> Caves["Caves"]
    Caves --> Ores["Ore tiers"]
    Ores --> Dungeon["Dungeon anchors"]
    Dungeon --> Secrets["Special / secret seed modifiers"]
    Secrets --> Metadata["Spawn + dungeon + layers"]
    Metadata --> Finalize["Candidate finalization"]
    Finalize --> Wld["Fresh .wld v326 persistence"]
```

Every built-in vanilla pass uses `WorldGenerationRngMode.VanillaSharedRng`. TerraRuntime resolves the Terraria world seed from `SeedText` using the pinned 1.4.5.8 rules: a valid `Int32` is used directly, otherwise CRC32 of the UTF-8 seed text is used. One `VanillaUnifiedRandom1458(worldSeed)` is then shared by the complete vanilla plan so RNG consumption carries from bootstrap into later passes.

For ordinary canonical worlds, `terraria:1.4.5.8/Reset` now consumes the source-backed pre-Terrain RNG sequence and records beach bounds, dungeon/jungle/snow origins, ore tiers, tree/cave/background styles and related initial state. `Terrain` consumes that state directly. Special seeds and non-canonical dimensions remain on compatibility branches until their Reset behavior is source-ported.

The ordinary isolated deterministic RNG remains available to custom runtime passes. `CustomProviderRng` stays fail-closed until a provider-owned RNG contract is explicitly defined.

## Special and secret seeds

`VanillaWorldSeedResolver1458` converts seed text into one immutable `VanillaWorldSeedProfile1458`. Generation and persistence consume the same profile, so seed behavior cannot silently disappear between candidate generation and the first restart.

The resolver recognizes all nine Terraria 1.4.5.8 special-world families: Drunk World, For the Worthy, Celebration Mk10, The Constant, Not the Bees, Don't Dig Up / Remix, No Traps, Get Fixed Boi / Zenith and Skyblock. Zenith expands to the combined special-seed profile. Matching is case-insensitive and ignores non-alphanumeric characters.

The resolver also recognizes the 37 Terraria 1.4.5 secret-seed phrases as independent flags. Multiple secret phrases may be combined with `|`, including Terraria-style prefixed input such as `1.1.1.0.planetoids|bring a towel`.

Generation currently applies runtime-owned compatibility behavior for terrain-affecting secret profiles such as Planetoids, Beam Me Up, Waterpark, Not the Bees, Toadstool, Mole People, Such Great Heights, Winter Is Coming, Sandy Britches and Save the Rainforest. Runtime-state secret flags are persisted through the fresh `.wld` v326 metadata writer.

## Publication and persistence

Generation occurs inside `RuntimeWorldGenerationWorkspace`, which is unpublished and therefore uses the initial-population tile-write path. Generated tiles do not manufacture network or persistence dirty queues before the world becomes authoritative.

The final metadata snapshot contains spawn, dungeon, world layers and the resolved vanilla seed profile. `WorldFileFreshRuntimeMetadata326Encoder` maps supported special/secret state into the canonical Terraria 1.4.5.8 `.wld` v326 metadata fields when a fresh world is persisted.

## Verification

Source-backed worldgen work has two complementary acceptance layers:

- source-contract workflows decompile the pinned TerrariaServer 1.4.5.8 binary and verify the implemented Reset/Terrain assumptions against it;
- `terraria-vanilla-generated-world-acceptance.yml` creates a real canonical small `.wld`, validates it with TerraRuntime, then boots the official TerrariaServer 1.4.5.8 with that file.

The separate `terraruntime:flat` acceptance path remains unchanged.

## Parity boundary

`terraruntime:vanilla` is usable and deterministic, and ordinary canonical worlds now have source-backed Reset and Terrain slices. It is **not** yet reference-world or byte-identical Terraria generation.

Exact parity for the complete source-pinned 109-pass Terraria 1.4.5.8 catalog remains roadmap work. Biomes, caves, ores, structures, decoration, special-seed Reset branches and other passes still contain compatibility implementations that must be replaced incrementally.
