# Authoritative-размещение объектов

Gameplay-граница packet 79 намеренно остаётся sparse. Первая production-транзакция разрешает только обычный vanilla-предмет Chest и базовый объект `Containers`. Клиент не может превратить корректно удерживаемый предмет в произвольный tile/style claim.

## Первая разрешённая связь

| Удерживаемый предмет | Item id | Object tile | Tile id | Style | Alternate |
| --- | ---: | --- | ---: | ---: | ---: |
| Chest | 48 | Containers | 21 | 0 | 0 |

Другие стили контейнеров, `Containers2`, dressers и alternate placement variants остаются unsupported, пока их source contracts не будут закреплены независимо. Поля packet random/direction в этом срезе остаются wire state; они не могут переопределить проверенную связь held-item → tile/style/alternate.

## Production ownership

```mermaid
flowchart LR
    Socket["Socket / packet 79"] --> Sink["ObjectPlacementFrameSink"]
    Sink --> Ingress["RuntimeProjectileNetworkIngress\nIObjectPlacementNetworkIngress"]
    Ingress --> Queue["Bounded authoritative queue"]
    Queue --> State["ServerRuntimeState"]
    State --> Processor["RuntimeObjectPlacementCommandProcessor"]
    Processor --> Catalog["Held-item → object catalog"]
    Processor --> World["Multi-tile + chest metadata"]
    Processor --> Inventory["Authoritative inventory consumption"]
    Processor --> Relay["Peer packet-79 replication"]
```

Production использует один gameplay ingress для projectile, packet-17 tile и packet-79 object traffic. `ProjectileLifecycleFrameSink` композирует tile- и object-sink под существующей chest/sign chain, поэтому серверу не нужен второй command queue или параллельный connection lifecycle.

Конкретный загруженный `WorldTileStore` связывается со своим runtime chest metadata lifecycle через weak-key runtime composition registry. Persistence создаёт эту связь до конструирования `ServerRuntimeState`. Registry не вводит process-global «current world» и не удерживает уже неиспользуемый мир в памяти.

## Транзакция

```mermaid
flowchart TD
    Request["Decoded PlaceObject + connection/player generation"] --> Player["Capture authoritative PlayerStateSnapshot"]
    Player --> Item["Read selected inventory slot"]
    Item --> Catalog["VanillaItemObjectPlacementCatalog"]
    Catalog -->|match| World["VanillaMultiTileObjectMutationService"]
    Catalog -->|mismatch / unsupported| Reject["Reject без mutation"]
    World -->|placement + chest metadata committed| Consume["Обычный PlayerEquipmentRuntimeCommand: stack - 1"]
    World -->|support/occupancy/metadata veto| Reject
    Consume -->|committed| Relay["Relay packet 79 playing-peers"]
    Consume -->|rejected| Rollback["Break только что созданного пустого объекта + remove metadata"]
```

Multi-tile service владеет геометрией 2×2, placement origin, support checks, frame cells и chest metadata lifecycle. Для `Containers` координаты packet передаются как vanilla placement origin. Object catalog переводит этот origin в нормализованный top-left anchor metadata сундука.

Расход предмета не выполняется изменением отдельного shadow inventory. Processor формирует нормализованное packet-5-style equipment state и проводит его через обычный authoritative equipment path `ServerRuntimeState`. Так сохраняются player revisioning, generation checks, inventory normalization и equipment replication.

Если equipment commit не материализует ровно ожидаемый остаток stack, только что созданный chest всё ещё пуст и не открыт, поэтому тот же multi-tile lifecycle удаляет metadata и все четыре клетки до завершения command. Невозможность rollback считается нарушением invariant и вызывает fault, а не тихо создаёт бесплатный объект.

## Репликация

Обратно в packet 79 кодируется только committed placement. Исходное соединение исключается; playing-peers получают принятый request после успешной authoritative world+inventory transaction. Ошибки support, item mismatch и rollback paths не создают peer placement frame.

## Оставшаяся область

Production composition теперь подключён для проверенного базового Chest. Для более широкой D5 parity всё ещё нужны независимо закреплённые item/style mappings, alternate placement origins, support rules мебели/табличек, liquid rules, adapters metadata tile-entity, object-specific drops и вторичные эффекты. До их проверки эти пути остаются fail-closed, а не выводятся из внешнего сходства объектов.
