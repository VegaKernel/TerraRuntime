# Built-in vanilla world generation

[Русский](../ru/vanilla-world-generation.md) · [World generation](world-generation.md) · [Roadmap](../roadmap/gameplay-worldgen-extensibility.md)

`terraruntime:vanilla` is TerraRuntime's runtime-owned clean-room TerrariaServer 1.4.5.8 generator. The public generator ID is stable while the runtime owns the exact generation plan behind it.

## Ordinary canonical pipeline

For the three canonical Terraria dimensions (`4200x1200`, `6400x1800`, `8400x2400`) with an ordinary seed profile, the production provider now composes the source-backed/source-shaped overlays through the end of the pinned 109-pass registration catalog.

```mermaid
flowchart LR
    Reset["Reset"] --> Terrain["Terrain"]
    Terrain --> Early["early terrain / caves / biomes"]
    Early --> Structures["dungeon / jungle / temple / hives"]
    Structures --> Objects["liquids / chests / spawn / vegetation"]
    Objects --> Micro["Micro Biomes"]
    Micro --> Settle["Settle Liquids Again"]
    Settle --> Nature["Cactus, Palm Trees, & Coral"]
    Nature --> Cleanup["Tile Cleanup"]
    Cleanup --> Altar["Lihzahrd Altars"]
    Altar --> Water["Water Plants"]
    Water --> Stalac["Stalac"]
    Stalac --> Traps["Remove Broken Traps"]
    Traps --> Final["Final Cleanup"]
    Final --> Secrets["compatibility SecretSeeds barrier"]
    Secrets --> Metadata["Metadata + fresh .wld v326"]
```

Every source-backed ordinary-world pass that participates in vanilla generation retains `WorldGenerationRngMode.VanillaSharedRng`. The name denotes Terraria's shared world-generation RNG API **inside a pass**, not one continuously advancing stream across the entire plan. Pinned TerrariaServer 1.4.5.8 `WorldGenerator.RunPass` assigns `Main.rand = new UnifiedRandom(_seed)` before applying each enabled pass, so TerraRuntime creates a fresh `VanillaUnifiedRandom1458` from the resolved world seed for each such pass. RNG calls within one pass advance that pass-local stream normally; carrying RNG state from one registered pass into the next is a compatibility bug.

The permanent `terraria-worldgen-pass-catalog.yml` source contract decompiles the pinned official server and now fails unless that `RunPass` reseed remains present before pass application. This prevents a self-consistent runtime test from silently redefining vanilla RNG lifetime.

## Final eight-pass overlay

`SourceBackedVanillaWorldGenerationFinal1458` completes the ordinary canonical pass identity sequence after `Micro Biomes` with the final eight TerrariaServer 1.4.5.8 registrations:

1. `Settle Liquids Again`
2. `Cactus, Palm Trees, & Coral`
3. `Tile Cleanup`
4. `Lihzahrd Altars`
5. `Water Plants`
6. `Stalac`
7. `Remove Broken Traps`
8. `Final Cleanup`

The implementation keeps these as separate passes rather than a single aggregate cleanup step. That preserves the source order, the per-pass RNG reseed boundary, pass-level progress reporting, dependency diagnostics, and a clean replacement point for deeper parity work.

The late passes perform deterministic liquid compaction, beach/desert vegetation and coral placement, normalized tile state, temple-altar placement, aquatic decoration, cave stalactite/stalagmite decoration, orphan-trap cleanup and a final vanilla-content/flag validation sweep before the compatibility secret-seed barrier.

## Selection and fallbacks

The full source-backed chain is selected only when both conditions are true:

- the seed profile is ordinary/default;
- the world dimensions are one of Terraria's canonical sizes.

Special/secret seeds and noncanonical synthetic dimensions deliberately replay the compatibility provider. They are not allowed to consume ordinary-world source-backed implementations as an approximation of their own generation rules.

The production registration in `BuiltInWorldGeneratorSource` resolves `terraruntime:vanilla` to `SourceBackedVanillaWorldGenerationFinal1458`. The older overlay classes remain implementation layers in the chain, not alternative public generators.

## Persistence and authority

Generation writes into an unpublished `RuntimeWorldGenerationWorkspace` backed by the contiguous `WorldTileStore`. Generated tiles, chests, starting town NPC metadata, spawn/dungeon anchors and layers remain candidate state until validation succeeds. No generation pass mutates the live network-visible world.

Final cleanup rejects out-of-catalog tile/wall identities and unknown runtime tile flags before the normal world-generation finalizer and fresh `.wld` v326 composition take ownership.

## Verification boundary

There are two separate milestones and they must not be conflated:

- **complete source-pinned pass coverage**: the ordinary canonical plan reaches all 109 TerrariaServer 1.4.5.8 pass identities through `Final Cleanup`;
- **reference-world parity**: fixed official seeds produce reference-equivalent output with verified per-pass RNG behavior and geometry/content parity.

The first milestone is implemented by the final overlay. The second remains an evidence task until reference-world differential tests prove it. Several existing source-shaped algorithms intentionally preserve pass boundaries and deterministic ownership while still awaiting deeper parity.

`terraria-vanilla-generated-world-acceptance.yml` remains the executable production gate: build the runtime, run focused world-generation contracts, generate a real canonical vanilla world, load the resulting `.wld` through TerraRuntime and boot the pinned official TerrariaServer 1.4.5.8 against it.
