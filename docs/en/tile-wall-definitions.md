# Vanilla tile and wall definitions

TerraRuntime exposes Terraria `1.4.5.8` tile and wall content through typed, version-pinned definition catalogs. Packed `ushort` fields remain the world snapshot ABI, while gameplay resolves their meaning through `TileTypeId`, `WallTypeId`, `VanillaTileDefinitionCatalog` and `VanillaWallDefinitionCatalog`.

## Definition flow

```mermaid
flowchart LR
    Source["TerrariaServer 1.4.5.8"] --> Identity["VanillaTileIds / VanillaWallIds"]
    Source --> Facts["Pinned capability masks"]
    Identity --> Catalog["Typed definition catalogs"]
    Facts --> Catalog
    Snapshot["WorldTile packed fields"] --> Typed["TileTypeId / WallTypeId"]
    Typed --> Catalog
    Catalog --> Gameplay["World and gameplay policy"]
```

`VanillaTileDefinitionCatalog` covers exactly `754` vanilla tile identities. It is a flyweight table: mutable `WorldTile` cells keep only per-cell state, while one immutable `VanillaTileDefinition` per type carries collision/frame facts, mutation path, mining profile, source-pinned drop rule, contextual simple-cell strategy and failed-pick transform target. This avoids duplicating invariant behavior across millions of world cells and prevents runtime authority from growing parallel raw-TileID allow-lists.

The simple-cell drop image is pinned to TerrariaServer 1.4.5.8 `WorldGen.KillTile_GetItemDrops`. `Fixed`, `None`, `Contextual` and `Object` are behavior categories rather than a `CanDrop` boolean: vines depend on the nearest player's functional Cordage equipment, Mushroom Vines use their vanilla RNG branch, Hive can produce honey and Bee/SmallBee spawns, and frame-important contextual identities remain on their frame/object path.

`VanillaWallDefinitionCatalog` covers exactly `367` vanilla wall identities. Its packed definition image mirrors `Main.wallHouse`, `Main.wallDungeon` and `Main.wallLight` after `Main.Initialize_TileAndNPCData2`. `WallTypeId(0)` is a valid catalog identity for no wall; `VanillaWallDefinition.IsPresent` distinguishes it from an occupied wall cell.

## Named progression identities

The named ID surface grows only when production gameplay or world generation needs an identity and the source contract can verify it. The Skyblock progression slice adds the following typed names:

| Tile | ID |
|---|---:|
| DemonAltar | 26 |
| Cobweb | 51 |
| MushroomGrass | 70 |
| Hellforge | 77 |
| Hive | 225 |
| LihzahrdBrick | 226 |
| LihzahrdAltar | 237 |
| Marble | 367 |
| Granite | 368 |

| Wall | ID |
|---|---:|
| SpiderUnsafe | 62 |
| HiveUnsafe | 86 |
| LihzahrdBrickUnsafe | 87 |

These names are catalog conveniences over the existing complete numeric range; they do not change the snapshot ABI or vanilla counts.

## Boundary rules

- Unknown IDs fail `TryGet`; absence is not interpreted as guessed vanilla defaults.
- World-file decoders may preserve storage values under their own compatibility policy, but authoritative gameplay must request a typed version-pinned definition before using content capabilities.
- Tile object size, origin, anchors and placement rules are deliberately outside these base definitions and belong to object/worldgen contracts.
- Collision and frame masks remain independently source-backed; the tile catalog composes them rather than copying another unverified table.

The `Tile Wall Definition Source Contract` workflow downloads the SHA-256-pinned official server, decompiles only `Main`, `TileID` and `WallID`, and verifies identity counts, the named progression constants and the wall capability images without committing decompiled source.
