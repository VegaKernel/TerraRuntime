# Vanilla-определения тайлов и стен

TerraRuntime представляет content тайлов и стен Terraria `1.4.5.8` через типизированные version-pinned catalogs. Упакованные поля `ushort` остаются ABI world snapshot, а gameplay получает их семантику через `TileTypeId`, `WallTypeId`, `VanillaTileDefinitionCatalog` и `VanillaWallDefinitionCatalog`.

## Поток определений

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

`VanillaTileDefinitionCatalog` покрывает ровно `754` vanilla tile identities и работает как flyweight-таблица: mutable `WorldTile` хранит только состояние конкретной ячейки, а один immutable `VanillaTileDefinition` на type содержит collision/frame facts, mutation path, mining profile, source-pinned drop rule, contextual simple-cell strategy и failed-pick transform target. Так invariant behavior не копируется в миллионы клеток мира, а runtime-authority не обрастает параллельными allow-list'ами сырых TileID.

Drop-image для simple-cell тайлов pinned к TerrariaServer 1.4.5.8 `WorldGen.KillTile_GetItemDrops`. `Fixed`, `None`, `Contextual` и `Object` являются категориями поведения, а не одним `CanDrop` boolean: vines зависят от functional Cordage ближайшего игрока, Mushroom Vines используют vanilla RNG branch, Hive может оставить honey и породить Bee/SmallBee, а frame-important contextual identities остаются на собственном frame/object path.

`VanillaWallDefinitionCatalog` покрывает ровно `367` vanilla wall identities. Его packed definition image соответствует `Main.wallHouse`, `Main.wallDungeon` и `Main.wallLight` после `Main.Initialize_TileAndNPCData2`. `WallTypeId(0)` является валидной identity отсутствующей стены; `VanillaWallDefinition.IsPresent` отличает её от занятой wall-cell.

## Именованные progression identities

Именованная поверхность IDs растёт только тогда, когда identity действительно нужна production gameplay/worldgen и source contract может её проверить. Skyblock progression добавляет:

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

Эти имена являются типизированными aliases поверх уже существующего полного диапазона и не меняют snapshot ABI или vanilla counts.

## Правила boundary

- Неизвестные IDs отклоняются методом `TryGet`; отсутствие определения не трактуется как угаданные vanilla defaults.
- World-file decoders могут сохранять storage values по собственной compatibility policy, но authoritative gameplay обязан запросить version-pinned definition перед использованием content capabilities.
- Размер tile-object, origin, anchors и placement rules намеренно не входят в базовые определения и относятся к object/worldgen contracts.
- Collision- и frame-masks остаются независимо source-backed; tile catalog объединяет их вместо копирования ещё одной непроверенной таблицы.

Workflow `Tile Wall Definition Source Contract` загружает официальный сервер с закреплённым SHA-256, декомпилирует только `Main`, `TileID` и `WallID` и проверяет counts, именованные progression-константы и wall capability images, не добавляя decompiled source в репозиторий.
