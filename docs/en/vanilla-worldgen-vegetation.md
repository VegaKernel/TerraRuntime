# Vanilla world generation: vegetation block through Mushrooms

This note describes the late vegetation slice of the Terraria 1.4.5.8-compatible `terraruntime:vanilla` generator.

## Scope

For ordinary canonical worlds the production plan advances from the persisted starting `Guide` through eleven pinned passes:

1. `Sunflowers`
2. `Planting Trees`
3. `Herbs`
4. `Dye Plants`
5. `Webs And Honey`
6. `Weeds`
7. `Glowing Mushrooms and Jungle Plants`
8. `Jungle Plants`
9. `Vines`
10. `Flowers`
11. `Mushrooms`

This moves the canonical production graph from 89 to 100 entries. `terraruntime:flat` remains a separate generator and the public vanilla identity remains `terraruntime:vanilla`.

## Source identities and environment ownership

The implementation uses Terraria tile identities verified against the current 1.4.5.x data tables: Sunflower `27`, Trees `5`, Herbs `82`, Dye Plants `227`, Cobweb `51`, regular/Jungle vines `52/62`, Jungle plants `61/74/233`, Mushroom grass `70`, and Glowing Mushroom plants `71`.

Placement is constrained by the existing generated terrain instead of inventing another biome map. Forest plants require grass, Jungle vegetation requires Jungle grass, glowing mushrooms require Mushroom grass, snow trees require Snow Block, and honey pockets are restricted around the Reset-owned Jungle origin.

All eleven passes use the single shared Terraria-compatible `UnifiedRandom` stream supplied by `VanillaSharedRng`. They remain enabled only for ordinary worlds using the three canonical Terraria dimensions; noncanonical and special-seed requests retain the compatibility plan until their branches are ported explicitly.

## Frame-important safety

Late vegetation must coexist with chests, doors, pots, traps, altars, fallen logs, the floating-island house and other framed objects already generated earlier. Placement therefore requires empty target cells and avoids nearby frame-important objects where larger structures such as trees need clearance.

Ordinary tile-`5` tree growth now uses a clean-room port of TerrariaServer 1.4.5.8 `WorldGen.GrowTree`. Version-pinned capability catalogs own the complete tree-ground, common-sapling, replaceable-growth and plant-growth-wall sets. The grower owns the source height and clearance gates, shared-RNG ordering, trunk variants, non-repeating branch rule, root normalization, paint/coating propagation and complete top framing. Raw content IDs and sprite-atlas coordinates do not leak into the growth algorithm: typed catalogs and the dedicated tree-frame catalog own them.

The `Planting Trees` pass still uses TerraRuntime's conservative candidate count and surface-column selection rather than claiming byte-identical `WorldGen.AddTrees` placement density or complete palm/vanity-tree branches. Tall Jungle decoration framing also remains source-shaped. These are placement/content-family limitations, not a remaining segmented-trunk or missing-crown limitation for the ordinary trees that the pass grows.

## Pass behavior

- `Sunflowers` places framed 2x4 sunflower objects on contiguous surface grass.
- `Planting Trees` selects conservative forest, Jungle and snow candidates, then applies source-backed `WorldGen.GrowTree` growth gates and complete trunk/branch/root/top frames.
- `Herbs` selects herb families from compatible soil/biome types.
- `Dye Plants` places sparse biome-aware dye plants with local spacing.
- `Webs And Honey` adds cavern cobwebs and Jungle-biased honey pockets.
- `Weeds` populates ordinary grass with short wild plants.
- `Glowing Mushrooms and Jungle Plants` decorates Mushroom/Jungle grass in underground regions.
- `Jungle Plants` adds later, denser Jungle decoration identities.
- `Vines` grows regular and Jungle vine chains from suitable exposed grass.
- `Flowers` adds surface flower styles on ordinary grass.
- `Mushrooms` places ordinary surface mushrooms as the final stage in this block.

## Validation

Focused contracts pin the 100-entry graph, the exact pass segment after `Guide`, `VanillaSharedRng` ownership, the next source boundary (`Gems In Ice Biome`), canonical-size gating and special-seed fallback. Tree tests additionally pin exact scripted frames and RNG consumption, consecutive-branch rerolls, growth rejection gates, replaceable vegetation and all four source capability-set counts. The canonical generated-world test requires real crown, branch and root frames in the composed workspace. `tools/ci/probe_worldgen_tree_growth.py` independently checks the runtime catalogs and framing routes against the pinned 1.4.5.8 decompile. The full generated-world workflow then composes a real `.wld`, reloads it through TerraRuntime, and boots the pinned official server with that file.

## Next boundary

The next source block begins at `Gems In Ice Biome`, followed by `Random Gems`, `Moss Grass`, `Muds Walls In Jungle`, and `Larva`. Those passes return to underground material/biome decoration and are better validated separately from surface vegetation.
