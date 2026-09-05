# Vanilla world generation: starting Guide persistence

This note describes the first generated-NPC boundary in the Terraria 1.4.5.8-compatible `terraruntime:vanilla` pipeline.

## Scope

The ordinary canonical pipeline now advances from `Grass Wall` through the pinned `Guide` generation pass. The built-in generator identity does not change: `terraruntime:flat` remains the separate minimal generator and `terraruntime:vanilla` remains the single progressively source-backed vanilla profile.

For canonical ordinary worlds the production plan grows from 88 to 89 entries. `Guide` executes immediately after `Grass Wall` and before the compatibility `SecretSeeds` barrier, matching the pinned Terraria pass catalog.

## Generated NPC ownership

A generated town NPC is not a tile. Keeping the Guide only in transient generation code would make the fresh world appear correct until persistence, then silently lose the NPC.

`Workspace` therefore owns a generated town-NPC side table alongside the existing generated chest side table. Generation registers the NPC before publication; `RuntimeWorldCreationPersistencePipeline` captures that side table with the finalized candidate; `WorldFileFreshComposer326` forwards it to the existing canonical `WorldFileNpcEncoder`.

The composer then reloads the completed byte image through `WorldFileLoader` and verifies that the encoded NPC counts survive the transaction. This keeps generated NPC data inside the same candidate-to-file atomic boundary as tiles and chests.

## Guide contract

The source-verified internal Terraria NPC identity is `22` (`Guide`). The ordinary-world Guide is placed at the world spawn published by the preceding `Spawn Point` pass. NPC position is stored in Terraria pixel coordinates while home coordinates use tile coordinates.

The initial Guide is persisted as homeless because a newly generated ordinary world does not yet contain a player-built valid house. A stable vanilla-valid Guide name (`Andrew`) is used without consuming the shared world-generation RNG. Terraria NPC given-name selection belongs to a separate naming/random surface; synthesizing it from `WorldGen.genRand` would incorrectly shift every later vanilla generation RNG read.

That naming detail is intentionally documented rather than hidden as false byte-parity.

## Validation

Focused contracts cover:

- the 89-entry canonical plan;
- `Grass Wall -> Guide -> SecretSeeds` ordering;
- `VanillaSharedRng` descriptor ownership without invented Guide RNG consumption;
- noncanonical compatibility fallback;
- duplicate generated-town-NPC rejection;
- Guide placement at source-backed spawn coordinates;
- fresh `.wld` NPC encode/load round-trip preserving ID, name, position and homeless state;
- full official TerrariaServer 1.4.5.8 generated-world acceptance.

## Next boundary

The following pinned passes return to tile/object vegetation work: `Sunflowers`, `Planting Trees`, `Herbs`, `Dye Plants`, `Webs And Honey`, `Weeds`, and the subsequent plant/biome decoration stages. They can build on the NPC bridge without inventing another persistence path.
