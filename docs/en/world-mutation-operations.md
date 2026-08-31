# Authoritative world mutation operations

TerraRuntime separates decoded Terraria packet/file representation from authoritative world mutation. The runtime-owned semantic implementation is `VanillaWorldTileMutationService` in `TerraRuntime.World`.

## Scope

The service owns the currently verified single-tile storage operations:

- `PlaceTile` for ordinary non-frame-important vanilla tiles admitted by the caller;
- `KillTile` only for the source-backed simple-removal slice `Dirt`, `Stone` and `Sand`;
- `PlaceWall` and `KillWall` with typed `WallTypeId` validation;
- `SetShape` for solid, non-platform, non-frame-important tiles;
- bounded simple-tile frame canonicalization and network/persistence dirty propagation.

Each request is expressed as `WorldTileMutationRequest`. Raw packet action numbers are not accepted by this API. Network ingress must first decode and authenticate the packet and higher gameplay layers remain responsible for reach, held item/tool power, inventory consumption, protection policy and drop reservation.

`VanillaSimpleTileKillCatalog` is deliberately a **mutation-capability** catalog, not a global mining-hardness table. A tile is admitted there only when the generic storage transition and the production drop path are both modelled well enough that clearing the cell does not silently erase vanilla behavior.

## Why generic `KillTile` is fail-closed

Pinned TerrariaServer 1.4.5.8 source proves that ordinary-looking tiles do not share one generic mining rule. `Player.PickTile` accumulates hit damage to a threshold of `100`; `Player.GetPickaxeDamage` contains tile-specific power gates; `WorldGen.CanKillTile` depends on surrounding tiles, containers and other world state; and `Player.DoesPickTargetTransformOnKill` turns families such as Grass into another tile instead of simply clearing them.

The source contract currently pins representative rules including Ebonstone/Crimstone at `65` pick power and Lihzahrd content at `210`, while Grass is explicitly in the transform-on-kill family. Treating every non-frame-important tile as `Type = 0; Active = false` would therefore be behavior corruption, not a harmless simplification.

Until those families receive their own typed semantics, the generic mutation service returns `UnsupportedState`. The production packet-17 path consequently cannot use a Copper Pickaxe to erase Grass, Snow, Lihzahrd Brick or any other unmodelled tile just because its storage definition is non-frame-important.

## State ownership

A tile block, wall, wire set and liquid are independent pieces of runtime state. Killing a supported ordinary block clears block-owned state (`Active`, tile type, shape, tile paint, actuator/inactive and block visibility/fullbright flags) while preserving wall, wall paint, wires and liquid. Wall removal clears wall-owned state without destroying the tile block.

Every committed cell write goes through `WorldTileStore.Set`, so section seqlock versions, network dirty sections and persistence dirty sections advance through the same single-writer boundary.

## Framing

For non-frame-important tiles, vanilla `.wld` does not persist meaningful tile-frame coordinates. TerraRuntime therefore canonicalizes the affected `3×3` simple-tile neighborhood to frame `(0,0)` instead of implementing client sprite-frame selection in authoritative storage. Wall mutations mark every section touched by that bounded frame neighborhood dirty.

Frame-important and known multi-tile content are deliberately rejected by the generic service. Their placement/break path must use verified `TileObjectData` geometry, anchors, style/frame mapping and metadata lifecycle rather than guessing frame arithmetic.

## Packet-17 mining boundary

The current production packet-17 tool proof remains intentionally narrow: the selected item must resolve to the source-verified Copper Pickaxe (`pick = 35`). A successful client `KillTile` command may then commit only if the target also belongs to `VanillaSimpleTileKillCatalog`.

This does **not** claim full vanilla mining parity. TerraRuntime does not yet reproduce the complete `HitTile` accumulation lifecycle, the full pickaxe catalog, all world-position/progression gates, transforming tiles, or a server-owned reach model. Those remain explicit gameplay tasks. The important invariant is that missing semantics fail closed instead of being converted into destructive generic behavior.

`tools/ci/probe_tile_mining.py` pins this boundary directly against the official TerrariaServer 1.4.5.8 binary. Changes to the simple-kill catalog, mutation service or its packet-level regression tests retrigger that source contract.

## Roadmap status

The D5 placement/break/framing **operation boundary** remains complete for its declared supported slice: typed requests, one authoritative commit owner, bounded simple framing and separate replication are in production use. The latest authority hardening narrows generic `KillTile` to the proven Dirt/Stone/Sand slice and converts unsupported mining families into explicit capability gaps.

Full mining parity still requires source-backed tool breadth, hit accumulation, environment-dependent `CanKillTile` rules, transform-on-kill families, object destruction and reach/protection policy. Those must be added through typed boundaries rather than reopening generic packet-driven cell clearing.
