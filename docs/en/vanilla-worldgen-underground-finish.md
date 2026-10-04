# Vanilla world generation: underground finish through Larva

This note describes the Terraria 1.4.5.8-compatible underground finishing block added after late vegetation in `terraruntime:vanilla`.

## Scope

For ordinary canonical worlds the production graph advances from 100 to 105 entries with the pinned sequence:

1. `Gems In Ice Biome`
2. `Random Gems`
3. `Moss Grass`
4. `Muds Walls In Jungle`
5. `Larva`

The block deliberately stops before `Micro Biomes`, which is a separate complexity boundary containing multiple structure generators rather than one homogeneous material-decoration stage.

`terraruntime:flat` remains unchanged and separate. The public vanilla identity remains `terraruntime:vanilla`.

## Source-backed content identities

The implementation uses verified Terraria content identities:

- Ice Block: tile `161`;
- gem blocks: tiles `63` through `68`;
- moss on stone: tiles `179` through `183`;
- moss growth: tile `184`;
- Mud Wall unsafe: wall `15`;
- Jungle Wall unsafe: wall `64`;
- Hive: tile `225`;
- Hive Wall unsafe: wall `86`;
- Larva: tile `231`, frame-important 3x3 object.

These identities are kept local to the clean-room pass until the shared typed catalog is expanded from pinned source evidence. The code does not manufacture unrelated IDs merely to make the table look comprehensive.

## Behavior

### Gems In Ice Biome

The pass samples rows between the surface/rock midpoint and the lava line, then columns within each row's retained snow bounds. An active Ice-family candidate offers a small rectangle of exposed gem objects (tile `178`) in adjacent air; six styles and the object-frame variation consume the shared RNG. Direct official registered-pass differentials at `600 × 800` and canonical `4200 × 1200` match the cells and next RNG.

### Random Gems

Two source-ordered offer streams place exposed gem objects (tile `178`): a deep-cavern search with wall/liquid exclusions, then a rectangle scatter around eligible unsafe-wall cells. Each object requires an attachment anchor and receives a source-selected style/frame. Direct official registered-pass differentials at `600 × 800` and canonical `4200 × 1200` match gem object positions/styles and next RNG.

### Moss Grass

The pass extends moss onto exposed Stone and places matching moss-growth decoration next to existing moss. Green, Brown, Red, Blue, and Purple moss identities are used. Exact vanilla moss spread helper iteration remains a parity target; this implementation owns the correct material family and underground domain without claiming reference-world byte equality.

### Muds Walls In Jungle

The pass finds the leftmost and rightmost surface Jungle Grass columns, then scans all rows above `worldSurface + 20` between them. Only Dirt-family unsafe walls (`2`/`59`) convert to Mud unsafe wall (`15`), with source-ordered random draws at the two lateral edges. Direct official registered-pass differentials at `600 × 500` and canonical `4200 × 1200` match wall coordinates/count and next RNG.

### Larva

Larva is not represented as a single anchor tile. Terraria defines it as a frame-important 3x3 background object. The pass scans existing Hive regions for empty Hive-wall pockets surrounded by Hive material and writes all nine framed Larva cells with 18-pixel frame increments. Placement also rejects nearby frame-important objects.

This is important for both file validity and gameplay semantics: a partially emitted Larva is an orphan framed object and is rejected by the gameplay mutation path. A complete object can now trigger the source-shaped nearest-player Queen Bee spawn.

## RNG and gating

All five passes use the one shared Terraria-compatible `UnifiedRandom` stream via `VanillaSharedRng`. They are enabled only for ordinary seeds and the three canonical Terraria dimensions. Special seeds and synthetic dimensions retain the compatibility graph until their own branches are ported.

## Validation

Focused contracts verify:

- the 105-entry canonical graph;
- exact pinned order from `Mushrooms` through `Larva`;
- `VanillaSharedRng` ownership;
- `Micro Biomes` as the next source boundary;
- Larva frame-important identity and complete 3x3 framing contract;
- noncanonical and special-seed fallback.

The normal vanilla generated-world acceptance then composes a real format-326 `.wld`, reloads it through TerraRuntime, and boots the pinned official TerrariaServer 1.4.5.8 with that file.
