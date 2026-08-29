# Built-in vanilla world generation

[Русский](../ru/vanilla-world-generation.md) · [World generation](world-generation.md) · [Roadmap](../roadmap/gameplay-worldgen-extensibility.md)

TerraRuntime exposes two runtime-owned world generators through the ordinary world-generation provider contract:

- `terraruntime:flat` remains the minimal deterministic baseline used for contract and persistence testing;
- `terraruntime:vanilla` is the built-in Terraria 1.4.5.8 compatibility generator.

The vanilla generator is clean-room runtime code. It does not embed or copy TerrariaServer implementation source. The implementation is deliberately split into replaceable generation passes so compatibility passes can be replaced by source-verified behavior as parity work advances.

## Execution model

```mermaid
flowchart LR
    Request["WorldGenerationRequest"] --> Resolve["Resolve seed profile"]
    Resolve --> Terrain["Terrain"]
    Terrain --> Biomes["Biomes + oceans"]
    Biomes --> Caves["Caves"]
    Caves --> Ores["Ore tiers"]
    Ores --> Dungeon["Dungeon anchors"]
    Dungeon --> Secrets["Special / secret seed modifiers"]
    Secrets --> Metadata["Spawn + dungeon + layers"]
    Metadata --> Finalize["Candidate finalization"]
    Finalize --> Wld["Fresh .wld v326 persistence"]
```

Every built-in vanilla pass uses `WorldGenerationRngMode.VanillaSharedRng`. TerraRuntime resolves the Terraria world seed from `SeedText` using the pinned 1.4.5.8 rules: a valid `Int32` is used directly, otherwise CRC32 of the UTF-8 seed text is used. A fresh `VanillaUnifiedRandom1458(worldSeed)` is created before each enabled vanilla pass, matching the verified Terraria 1.4.5.8 pass-level RNG lifecycle.

The ordinary isolated deterministic RNG remains available to custom runtime passes. `CustomProviderRng` stays fail-closed until a provider-owned RNG contract is explicitly defined.

## Special and secret seeds

`VanillaWorldSeedResolver1458` converts seed text into one immutable `VanillaWorldSeedProfile1458`. Generation and persistence consume the same profile, so seed behavior cannot silently disappear between candidate generation and the first restart.

The resolver recognizes all nine Terraria 1.4.5.8 special-world families:

- Drunk World;
- For the Worthy;
- Celebration Mk10;
- The Constant;
- Not the Bees;
- Don't Dig Up / Remix;
- No Traps;
- Get Fixed Boi / Zenith;
- Skyblock.

Special seed matching is case-insensitive and ignores non-alphanumeric characters. Zenith expands to the classic combined special-seed profile. Prefixed and pipe-combined input is also handled by the resolver.

The resolver also recognizes the 37 Terraria 1.4.5 secret-seed phrases as independent flags. Multiple secret phrases may be combined with `|`, including Terraria-style prefixed input such as `1.1.1.0.planetoids|bring a towel`.

Generation currently applies runtime-owned compatibility behavior for terrain-affecting secret profiles such as Planetoids, Beam Me Up, Waterpark, Not the Bees, Toadstool, Mole People, Such Great Heights, Winter Is Coming, Sandy Britches and Save the Rainforest. Runtime-state secret flags are persisted through the fresh `.wld` v326 metadata writer, including permanent seasonal modes, vampire/infected modes, team-based spawns, dual dungeons and lightning variants.

## Publication and persistence

Generation occurs inside `RuntimeWorldGenerationWorkspace`, which is unpublished and therefore uses the initial-population tile-write path. Generated tiles do not manufacture network or persistence dirty queues before the world becomes authoritative.

The final metadata snapshot contains spawn, dungeon, world layers and the resolved vanilla seed profile. `WorldFileFreshRuntimeMetadata326Encoder` maps supported special/secret state into the canonical Terraria 1.4.5.8 `.wld` v326 metadata fields when a fresh world is persisted.

## Parity boundary

`terraruntime:vanilla` is now a usable, non-flat, deterministic runtime-owned vanilla-style generator with verified seed/RNG semantics and persisted special/secret seed state. It is **not** yet byte-identical to TerrariaServer `WorldGen.AddPasses()` output.

Exact implementation and reference-world parity for the complete source-pinned 109-pass Terraria 1.4.5.8 catalog remain roadmap work. The pass-oriented architecture is intentionally designed so those compatibility implementations can be replaced incrementally without changing the host/provider contract or world publication pipeline.
