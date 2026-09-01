# Optimized world generation

`terraruntime:optimized` is TerraRuntime's production-oriented custom world generator. It intentionally does **not**
promise seed-identical Terraria world generation. Its contract is different: the same TerraRuntime version and seed
must reproduce the same candidate, mandatory world roles must fit, official-client content IDs must remain valid, and
the result must be visually coherent and playable without importing a second world.

`terraruntime:vanilla` remains the source/reference-parity profile. Optimized generation does not replace it.

## Source layout

Built-in generator implementations are separated by profile:

```text
src/TerraRuntime.World/Generation/
├── Flat/
├── Optimized/
├── Skyblock/
└── Vanilla/
```

The optimized profile is layered instead of growing one monolithic provider:

```mermaid
flowchart TD
    Base["OptimizedWorldGenerationProvider<br/>layout / terrain / biomes / caves / islands / ores / mandatory structures"]
    Play["OptimizedPlayableWorldGenerationProvider<br/>large caverns / shafts / underground lakes / Life Crystals / generic caches"]
    Land["OptimizedLandmarkWorldGenerationProvider<br/>organic transitions / landmarks / micro-biomes / landmark caches"]
    Meta["metadata + base validator"]
    PVal["playability validator"]
    LVal["landmark validator"]
    Shape["surface shaping<br/>natural top slopes / half-block transitions"]
    Surf["OptimizedSurfaceDecorationWorldGenerationProvider<br/>foliage-anchored trees / undergrowth / sunflowers"]
    Prog["OptimizedProgressionValidationWorldGenerationProvider<br/>resource / structure / reachability gate"]
    Commit["candidate finalization / commit"]

    Base --> Play --> Land --> Meta --> PVal --> LVal --> Shape --> Surf --> Prog --> Commit
```

All optimized passes use `WorldGenerationRngMode.IsolatedDeterministic`. Adding an unrelated later pass therefore does
not silently shift the random stream of an existing pass.

## Current generated world

The optimized profile currently produces and validates:

- a protected central spawn and starting Guide;
- both oceans and beaches with validated continuous basin floors;
- forest, snow, desert, jungle, corruption/crimson and underground mushroom regions;
- an Underworld band with Lava, Hellstone and a Hellforge;
- small correlated caves plus large warped caverns, vertical shafts and inland underground lakes;
- multiple floating islands;
- a dungeon, jungle hive, Jungle Temple and Aether/Shimmer pocket;
- pre-Hardmode ore tiers;
- a world-area-scaled Life Crystal budget;
- persistent surface, underground and cavern exploration caches;
- persistent sky houses on a subset of floating islands;
- explicit Floating Lakes on other islands;
- deterministic desert pyramids with internal chambers and persistent caches;
- hollow Living Wood trees with roots, underground rooms and persistent caches;
- bounded Underworld houses connected by undulating platform bridges;
- granite, marble and spider/cobweb micro-biomes;
- an explicit readable dungeon opening;
- domain-warped material tongues at snow, desert, jungle and world-evil boundaries;
- deterministic ordinary forest, jungle and snow trees plus grass/jungle undergrowth and sunflower patches, all placed after landmarks so progression objects and caches are protected.
- a deterministic surface-finishing pass converts clean one-tile natural height transitions into persisted walkable slopes/half-blocks; ordinary optimized trees mark their crown cells with the vanilla tree foliage-frame contract instead of ending as bare trunk tiles.

The landmark layer uses only tile/wall identities already source-backed by the repository's TerrariaServer `1.4.5.8`
world-generation work. Landmark cache loot remains deliberately custom and conservative until the full vanilla
biome-loot catalog is source-backed.

## Organic transitions

The base generator still owns the large biome layout. The landmark pass does not reshuffle biome positions after major
structures have been reserved. Instead it measures the existing material band, finds each edge and grows deterministic
noise-shaped tongues into adjacent natural terrain. Only natural terrain families are eligible for replacement, so ores,
frame-important objects and mandatory structures are not treated as paintable transition material.

This removes the most obvious straight vertical material boundaries while preserving the bounded layout contract.

## Floating-island roles

Sky terrain is scanned as separate horizontal masses. The landmark pass assigns two distinct roles:

- **sky house**: Sunplate shell, Disc Wall interior and a persistent custom sky cache;
- **Floating Lake**: a carved bounded water basin reinforced inside the existing island mass.

Both roles have explicit minimum budgets. A pass that cannot place the requested house/lake counts fails generation
rather than silently returning a visually incomplete world.

The current sky cache is not claimed to reproduce vanilla Skyware loot. Source-backed Starfury/Horseshoe/Balloon roles
remain a separate progression task.

## Surface and underground landmarks

### Pyramids

Desert surface spans are detected from generated material rather than hard-coded X coordinates. The generator derives a
world-width-scaled pyramid budget, builds a solid sandstone-brick mass, then carves a surface opening, internal shaft
and chamber before persisting a cache inside the chamber.

### Living trees

Forest surface candidates are selected away from the protected spawn envelope. Each generated tree has a Living Wood
trunk, Leaf Block crown, roots, a hollow vertical core, a Living Wood underground room and a persistent cache.

### Underworld settlements

The Underworld receives a bounded number of Ash houses. Open doorways and platform bridges keep the structures usable
without requiring guessed furniture/door frame metadata. Later work can replace the conservative shell with richer
vanilla-inspired furniture sets after those content contracts are source-backed.

## Micro-biomes

The landmark pass adds bounded granite and marble lenses plus spider grottoes. Spider grottoes carve an underground
chamber, apply the source-backed unsafe spider wall and seed Cobweb tiles. Placement rejects areas near frame-important,
hive, temple, dungeon, chest, Honey or Shimmer content.

These are visual/exploration roles, not claims of exact vanilla placement algorithms.

## Validation

Generation remains fail-closed. The landmark validator runs after the existing geography and playability validators and
requires:

- the exact sky-house and Floating-Lake budgets;
- the exact pyramid, Living Tree and Underworld-house budgets;
- the exact granite, marble and spider-grotto budgets;
- a non-trivial number of warped biome-transition cells;
- persistent landmark chest side-table entries;
- minimum generated material/wall counts for each landmark family;
- a successfully opened dungeon entrance.

This is deliberately stronger than checking for one representative tile. A half-generated landmark set is rejected.

A final `OptimizedProgressionValidationWorldGenerationProvider` then scans the post-landmark candidate. It enforces
area-scaled minimum quantities for Copper, Iron, Silver, Gold and Hellstone; verifies complete 3x2 Demon/Crimson Altar,
Hellforge and Lihzahrd Altar footprints; requires non-trivial connected dungeon, hive and Jungle Temple interiors; and
builds a bounded excavation-aware reachability graph from spawn to snow, desert, jungle, world evil, the dungeon
entrance, hive interior, Jungle Temple entrance and Underworld Hellforge. Ordinary terrain contributes excavation cost,
while dense Lihzahrd barriers and deep Lava are treated as blocking. This is a structural topology gate, not a claim of
pixel-exact Terraria player movement or tool progression.

## Compatibility and non-goals

The same seed is **not** expected to create the Terraria world for that seed. Use `terraruntime:vanilla` for
source/reference parity.

Optimized worlds still target official-client-compatible tile, wall, liquid, object and `.wld` finalization contracts.
Loading an existing vanilla `.wld` remains independent of which generator is used for new worlds.

## Remaining work

The landmark and final progression-validation slices close substantial visual/content and structural gaps, but
`terraruntime:optimized` is not yet production-complete. Important remaining items include:

- Shadow Orb / Crimson Heart anchors;
- true source-backed biome and Skyware loot families;
- dungeon locked chest/key progression and richer dungeon branches/traps;
- multiple hives and stronger Queen Bee space on larger worlds;
- glowing-mushroom and additional decorative micro-biomes;
- Hardmode-ready mutation anchors;
- Small/Medium/Large generation-time and peak-memory measurements;
- deterministic map/screenshot visual-regression fixtures;
- pinned TerrariaServer `1.4.5.8` acceptance plus official-client join smoke.

See [`../roadmap/optimized-worldgen.md`](../roadmap/optimized-worldgen.md) for the implementation checklist.
