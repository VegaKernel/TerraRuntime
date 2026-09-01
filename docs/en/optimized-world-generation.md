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
    Meta["metadata"]
    Dungeon["optimized dungeon v2<br/>rooms / branches / locked loot / traps"]
    PVal["base + playability validators"]
    LVal["landmark validator"]
    Content["progression content<br/>evil anchors / Larva / forge pocket"]
    Shape["surface shaping<br/>natural top slopes / half-block transitions"]
    Surf["OptimizedSurfaceDecorationWorldGenerationProvider<br/>foliage-anchored trees / undergrowth / sunflowers"]
    Prog["OptimizedProgressionValidationWorldGenerationProvider<br/>resource / structure / reachability gate"]
    Commit["candidate finalization / commit"]

    Base --> Play --> Land --> Meta --> Dungeon --> PVal --> LVal --> Content --> Shape --> Surf --> Prog --> Commit
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
- a connected dungeon room/branch graph with a readable entrance, locked dungeon loot, Golden Keys, spikes and wired dart traps;
- a jungle hive, Jungle Temple and Aether/Shimmer pocket;
- pre-Hardmode ore tiers;
- a world-area-scaled Life Crystal budget;
- persistent surface, underground and cavern exploration caches;
- persistent sky houses on a subset of floating islands;
- explicit Floating Lakes on other islands;
- deterministic desert pyramids with internal chambers and persistent caches;
- hollow Living Wood trees with roots, underground rooms and persistent caches;
- bounded Underworld houses connected by undulating platform bridges;
- granite, marble and spider/cobweb micro-biomes;
- domain-warped material tongues at snow, desert, jungle and world-evil boundaries;
- deterministic ordinary forest, jungle and snow trees plus grass/jungle undergrowth and sunflower patches, all placed after landmarks so progression objects and caches are protected;
- a deterministic surface-finishing pass that converts clean one-tile natural height transitions into persisted walkable slopes/half-blocks and publishes vanilla-format tree foliage anchors.

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

## Dungeon v2

The final optimized profile rebuilds only the already-reserved Blue Dungeon footprint. It creates a deterministic chain
of traversable main rooms, alternating side branches and a surface-connected entrance while preserving any pre-existing
persistent chest footprints that happen to overlap the reservation. The pass then places an unlocked entrance cache with
enough Golden Keys for the generated locked chests, source-backed dungeon primary loot, bounded spike fields and paired
pressure-plate/dart-trap mechanisms connected by red wire.

The implementation is intentionally custom rather than seed-identical to Terraria. Source-backed 1.4.5.8 identities are
used for locked chest style/framing, Golden Keys, Muramasa, Cobalt Shield, Aqua Scepter, Blue Moon, Magic Missile, Valor,
Handgun, pressure plates and dart traps. Generation fails closed if room connectivity, locked-chest counts, key balance,
trap budgets, spike budgets, chest framing or the readable entrance contract are incomplete.

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

## Guaranteed progression content

After landmark validation, `terraruntime:optimized` now adds world-size-budgeted 2x2 Shadow Orbs or Crimson Hearts
using the pinned 1.4.5.8 frame contract (`+36` frame-X for Crimson), dry 3x3 Larva anchors inside the Hive, one persistent
Jungle Progression Cache containing source-backed Jungle Spores/Stingers/Vines, and a dry Underworld forge pocket with
reachable Obsidian plus exposed Hellstone. The final topology validator treats all four roles as mandatory route targets.

Larva placement proves the worldgen anchor only. Queen Bee activation/destruction semantics remain owned by gameplay
runtime work and are not falsely counted as complete here.

## Validation

Generation remains fail-closed. The validators require exact landmark budgets, persistent chest side-table entries,
minimum generated material/wall counts, a readable dungeon entrance, the Dungeon v2 room/loot/trap contracts, and final
progression topology. The final `OptimizedProgressionValidationWorldGenerationProvider` enforces area-scaled minimum
quantities for Copper, Iron, Silver, Gold and Hellstone; verifies complete progression-object footprints; requires
non-trivial connected dungeon, hive and Jungle Temple interiors; and builds a bounded excavation-aware reachability graph
from spawn to required surface/deep-world targets. This is a structural topology gate, not a claim of pixel-exact
Terraria player movement or tool progression.

## Compatibility and non-goals

The same seed is **not** expected to create the Terraria world for that seed. Use `terraruntime:vanilla` for
source/reference parity.

Optimized worlds still target official-client-compatible tile, wall, liquid, object and `.wld` finalization contracts.
Loading an existing vanilla `.wld` remains independent of which generator is used for new worlds.

## Remaining work

The optimized profile is not yet production-complete. Important remaining items include:

- true source-backed biome and Skyware loot families;
- multiple hives and stronger Queen Bee space on larger worlds;
- glowing-mushroom and additional decorative micro-biomes;
- Hardmode-ready mutation anchors;
- Small/Medium/Large generation-time and peak-memory measurements;
- deterministic map/screenshot visual-regression fixtures;
- pinned TerrariaServer `1.4.5.8` acceptance plus official-client join smoke.

See [`../roadmap/optimized-worldgen.md`](../roadmap/optimized-worldgen.md) for the implementation checklist.