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

Ordinary cactus growth now uses source-backed `PlantCactus`/`GrowCactus` instead of fixed short columns. The initial root attempt is followed by all 150 randomized candidate pairs, even after rejection. Growth owns the four conversion sands, nearby sand/cactus scan order, integer liquid-volume gate, slope-flattening roll, root/branch discovery, population cap, trunk-height rolls, raised arms and paint/coatings. The liquid scan covers $100\times50\,\text{tiles}$ and rejects only when integer liquid units exceed 25 full tiles. Cosmetic `CactusFrame` is intentionally not run: source `TileFrame` skips it during `generatingWorld`, so zero cosmetic frames are not themselves a generation defect. Scripted tests pin growth/RNG/limits; canonical Small seed `42` now requires raised cactus arms. Disabling arms fails both the scripted shape test and full-map assertion. Mixed-pass candidate density/oasis scheduling and special-seed/runtime growth remain open.

The later `Cactus, Palm Trees, & Coral` pass now uses the ordinary 1.4.5.8 `GrowPalmTree` growth slice rather than zero-framed short columns: four sand soils, dry/flat base, plant-wall and full-height clearance gates, shared-RNG height/bend/segment order, paint/coating and base/crown frames. The source's always-true bend condition still consumes its preceding short-circuit RNG calls. Surface search starts at the world top and stops before `worldSurface-1`; the former narrow layer window missed dry beaches. Scripted/gate tests and canonical seed `42` cover this fix. Placement density, oasis scheduling, coral/shell placement and special-seed variants remain partial; this is not full parity for that mixed pass.

Late vegetation must coexist with chests, doors, pots, traps, altars, fallen logs, the floating-island house and other framed objects already generated earlier. Placement therefore requires empty target cells and avoids nearby frame-important objects where larger structures such as trees need clearance.

Ordinary tile-`5` tree growth now uses a clean-room port of TerrariaServer 1.4.5.8 `WorldGen.GrowTree`. Version-pinned capability catalogs own the complete tree-ground, common-sapling, replaceable-growth and plant-growth-wall sets. The grower owns the source height and clearance gates, shared-RNG ordering, trunk variants, non-repeating branch rule, root normalization, paint/coating propagation and complete top framing. Raw content IDs and sprite-atlas coordinates do not leak into the growth algorithm: typed catalogs and the dedicated tree-frame catalog own them.

The `Planting Trees` pass still uses TerraRuntime's conservative candidate count and surface-column selection rather than claiming byte-identical `WorldGen.AddTrees` placement density or complete palm/vanity-tree branches. These are placement/content-family limitations, not a remaining segmented-trunk or missing-crown limitation for the ordinary trees that the pass grows.

Jungle detritus `233` is no longer published as an invalid single cell. The existing sampling pass now calls a source-backed `PlaceJunglePlant` clear-footprint slice: styles `0..7`, complete $3\times2$ framing, three flat unactuated Jungle-grass supports, inherited block paint/coating, independent liquid/wall/wires preserved. If the full placement cannot be admitted, no substitute is emitted. The full candidate distribution, active-plant replacement/cascades and generation of the separate $2\times2$ detritus branch remain open. Actual generated-world startup tests validate complete detritus before liquid preparation; the previous one-cell code fails those checks.

## Underworld ash vegetation

The ordinary `Underworld` pass now runs the source ash-vegetation scans immediately before `AddHellHouses`. Columns exclude the outer 25 cells and use the strict `x < width * 0.17 || x > width * 0.83` forest test. Exposed active Ash `57` becomes Ash Grass `633` when any of its eight neighbors is inactive. The lower grass-scan bound redraws `Next(-1,2)` on every loop-condition evaluation, including termination. A separate later scan attempts trees with `Next(3)==0` only above exposed Ash Grass.

`AshTreeGrower1458` implements the generation-time `GrowTreeWithSettings(Tree_Ash)` slice, not ordinary `GrowTree` with a substituted tile ID. It uses Ash Grass soil, existing plant-wall and replaceable-growth catalogs, a dry three-cell base, height $7..12\,\text{tiles}$, five-column clearance and four cells of top padding. It reuses the tree atlas while preserving source branch rerolls, paint/coatings, independent left/right root rolls (even with an ineligible side), generic tree-ground eligibility at roots, settings-specific base frames, and the final leafy/bare crown draws. Runtime growth and Drunk/Remix/special-seed branches are not admitted by this helper.

Scripted tests cover rejection without mutation, exact RNG/frames for root combinations and branch rerolls, height bounds, scan boundaries and grass exposure. Full canonical Small/Medium/Large maps retain edge grass and framed tree crowns after finalization; omitting the vegetation invocation fails all three. This verifies the admitted vegetation slice, not exact Underworld terrain or whole-seed equality.

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
