# Authoritative world mutation operations

TerraRuntime separates decoded Terraria packet/file representation from authoritative world mutation. The runtime-owned semantic implementation is `VanillaWorldTileMutationService` in `TerraRuntime.World`.

## Scope

The service owns the currently verified single-tile storage operations:

- `PlaceTile` for ordinary non-frame-important vanilla tiles admitted by the caller;
- `KillTile` for definition-admitted ordinary single-cell vanilla tiles plus the explicit source-pinned `FrameImportantSingleCell` subset; other frame-important/multi-tile/object content uses separate paths;
- `PlaceWall` and `KillWall` with typed `WallTypeId` validation;
- `SetShape` for solid, non-platform, non-frame-important tiles;
- bounded simple-tile frame canonicalization and network/persistence dirty propagation.

Each request is expressed as `WorldTileMutationRequest`. Raw packet action numbers are not accepted by this API. Network ingress must first decode and authenticate the packet and higher gameplay layers remain responsible for reach, held item/tool power, inventory consumption, protection policy and drop reservation.

Ordinary mining is definition-driven. `VanillaTileDefinitionCatalog` is a flyweight table for every vanilla 1.4.5.8 tile identity and carries the mutation path, mining profile, drop rule and failed-pick transform semantics. There is no positive allow-list of "supported" ordinary tiles: normal single-cell removal is the default path, while frame-important objects, contextual drops, transforms and progression gates select explicit specialized behavior. Ordinary Dirt follows that same simple-cell path: an adjacent active terrain tile is not a reason to reject the completed packet-17 break, and the committed mutation preserves all neighbouring cells while materializing the Dirt Block drop through the same reservation boundary.

## Why generic `KillTile` is fail-closed

Pinned TerrariaServer 1.4.5.8 source proves that ordinary-looking tiles do not share one generic mining rule. `Player.PickTile` accumulates hit damage to a threshold of `100`; `Player.GetPickaxeDamage` contains tile-specific power gates; `WorldGen.CanKillTile` depends on surrounding tiles, containers and other world state; and `Player.DoesPickTargetTransformOnKill` turns families such as Grass into another tile instead of simply clearing them.

The source contract currently pins representative rules including Ebonstone/Crimstone at `65` pick power and Lihzahrd content at `210`, while Grass is explicitly in the transform-on-kill family. Treating every non-frame-important tile as `Type = 0; Active = false` would therefore be behavior corruption, not a harmless simplification.

The storage mutation service accepts ordinary `SimpleCell` definitions and a deliberately tiny `FrameImportantSingleCell` family whose one-cell footprint and fixed 1.4.5.8 drop are independently pinned. The gameplay authority resolves mining power, failed-pick transforms, contextual drop semantics and progression-sensitive requirements before committing that storage mutation. The currently admitted framed single-cell identities are Water Candle, Switch, and the six coloured Team Platforms. Ordinary Platforms and Torches remain fail-closed because their drop/style semantics depend on frame state. Multi-tile content uses its separate object transaction. The production packet-17 bridge also admits the exact base Chest identity, where coherent 2x2 geometry, runtime metadata removal and the authoritative Chest item drop commit as one bounded operation; other frame-important/object families remain fail-closed.

## State ownership

A tile block, wall, wire set and liquid are independent pieces of runtime state. Killing a supported ordinary block clears block-owned state (`Active`, tile type, shape, tile paint, actuator/inactive and block visibility/fullbright flags) while preserving wall, wall paint, wires and liquid. Wall removal clears wall-owned state without destroying the tile block.

Every committed cell write goes through `WorldTileStore.Set`, so section seqlock versions, network dirty sections and persistence dirty sections advance through the same single-writer boundary.

## Framing

For non-frame-important tiles, vanilla `.wld` does not persist meaningful tile-frame coordinates. TerraRuntime therefore canonicalizes the affected `3×3` simple-tile neighborhood to frame `(0,0)` instead of implementing client sprite-frame selection in authoritative storage. Wall mutations mark every section touched by that bounded frame neighborhood dirty.

Generic frame-important and known multi-tile content remain rejected by the storage service. The only exception is the explicit `FrameImportantSingleCell` catalog, where removal clears exactly one source-pinned cell and deliberately does not rewrite neighbouring frame coordinates. Wider placement/break support must still use verified `TileObjectData` geometry, anchors, style/frame mapping and metadata lifecycle rather than guessing frame arithmetic.

## Packet-17 mining boundary

Packet-17 mining resolves the selected item through the source-backed pick-tool catalog, then evaluates the target's `VanillaTileDefinition`. Ordinary single-cell tiles use the default mutation path; mining power, contextual drops, failed-pick transforms and object/frame-important paths are enforced by typed rules instead of raw TileID allow-lists.

This does **not** claim full vanilla mining parity. TerraRuntime still does not reproduce the complete `HitTile` accumulation lifecycle, every frame-important/object destruction rule, every world-position/progression gate, or a server-owned reach model. Failed-pick transform families and the complete 1.4.5.8 simple-cell drop table are definition-driven; missing object/frame semantics remain explicit fail-closed boundaries.

`tools/ci/probe_tile_mining.py` pins the mining identities and requirements directly against the official TerrariaServer 1.4.5.8 binary. Drop-rule data is version-pinned separately from `WorldGen.KillTile_GetItemDrops`; gameplay consumes typed definitions rather than a positive allow-list of raw TileIDs.

## Roadmap status

The D5 placement/break/framing **operation boundary** remains complete for its declared supported slice: typed requests, one authoritative commit owner, bounded simple framing and separate replication are in production use. Ordinary single-cell mining no longer depends on a manually maintained positive TileID allow-list; immutable per-type definitions select mining, transform and drop behavior.

Full mining parity still requires hit accumulation, the remaining environment-dependent `CanKillTile` rules, frame-important/object destruction and reach/protection policy. Those must be added through typed boundaries rather than reopening generic packet-driven cell clearing.
