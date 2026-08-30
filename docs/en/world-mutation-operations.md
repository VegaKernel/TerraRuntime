# Authoritative world mutation operations

TerraRuntime separates decoded Terraria packet/file representation from authoritative world mutation. The first runtime-owned semantic implementation is `VanillaWorldTileMutationService` in `TerraRuntime.World`.

## Scope

The service owns the currently verified single-tile storage operations:

- `PlaceTile` for ordinary non-frame-important vanilla tiles;
- `KillTile` for ordinary non-frame-important vanilla tiles;
- `PlaceWall` and `KillWall` with typed `WallTypeId` validation;
- `SetShape` for solid, non-platform, non-frame-important tiles;
- bounded simple-tile frame canonicalization and network/persistence dirty propagation.

Each request is expressed as `WorldTileMutationRequest`. Raw packet action numbers are not accepted by this API. Network ingress must first decode and authenticate the packet and higher gameplay layers remain responsible for reach, held item/tool power, inventory consumption, protection policy and drop reservation.

## State ownership

A tile block, wall, wire set and liquid are independent pieces of runtime state. Killing an ordinary block clears block-owned state (`Active`, tile type, shape, tile paint, actuator/inactive and block visibility/fullbright flags) while preserving wall, wall paint, wires and liquid. Wall removal clears wall-owned state without destroying the tile block.

Every committed cell write goes through `WorldTileStore.Set`, so section seqlock versions, network dirty sections and persistence dirty sections advance through the same single-writer boundary.

## Framing

For non-frame-important tiles, vanilla `.wld` does not persist meaningful tile-frame coordinates. TerraRuntime therefore canonicalizes the affected `3×3` simple-tile neighborhood to frame `(0,0)` instead of implementing client sprite-frame selection in authoritative storage. Wall mutations mark every section touched by that bounded frame neighborhood dirty.

Frame-important and known multi-tile content are deliberately rejected by the generic service. Their placement/break path must use verified `TileObjectData` geometry, anchors, style/frame mapping and metadata lifecycle rather than guessing frame arithmetic.

## Existing packet-17 Dirt path

`VanillaDirtPlacement` remains the strict source-backed packet-17 compatibility facade for the currently admitted Dirt slice. Its isolation/preflight proof is unchanged, but the actual placement/removal commit now delegates to `VanillaWorldTileMutationService`. This keeps drop reservation and packet authority separate while preventing a second ad-hoc storage mutation implementation from growing beside the semantic service.

## Roadmap boundary

This is a substantial D5 foundation, not a claim that all Terraria placement/break/framing parity is complete. Multi-tile objects, attachment/support rules, object metadata creation/destruction, tool-power rules and the complete source-backed framing families remain separate work before the broad D5 placement/break/framing checkbox can be closed.
