# Разреженный каталог определений vanilla items

TerraRuntime использует намеренно разреженный source-backed каталог item definitions. Отсутствующая metadata означает **ещё не проверено/не импортировано**, а не выдуманный vanilla-ноль или `false`.

## Владение данными

```mermaid
flowchart LR
    Source["TerrariaServer 1.4.5.8"] --> Probe["Pinned source contracts"]
    Probe --> Catalog["VanillaItemDefinitionCatalog"]
    Catalog --> Placement["Placement"]
    Catalog --> Tools["Tool authority"]
    Catalog --> WorldDrop["World-item materialization"]
    WorldDrop --> Prefixes["VanillaItemPrefixCatalog"]
```

`VanillaItemIds` владеет content identity. `TerraRuntime.Gameplay.Items.VanillaItemDefinitionCatalog` владеет неизменяемыми проверенными item-фактами, а связанные object-placement/prefix catalogs находятся в том же gameplay-слое. Runtime inventory/world-item stores в Core/application-коде владеют mutable stack, prefix, slot, generation и revision.

## Проверенные capabilities

| Item | Проверенные capability-факты |
|---|---|
| `DirtBlock` (`2`) | core defaults: `12×12`, maximum stack `9999`; placement: `createTile = 0`, `consumable = true`; swing use: animation $15\,\text{тиков}$, use time $10\,\text{тиков}$, auto-reuse, turn |
| `CopperPickaxe` (`3509`) | core defaults: `24×28`, maximum stack `9999`; pick tool: `pick = 35`, `tileBoost = -1`; swing use: animation $23\,\text{тика}$, use time $15\,\text{тиков}$, auto-reuse, turn |
| `Gel` (`23`) | core/world-drop размер `10×12`, maximum stack `9999`, обычная gravity, без natural-prefix family |
| `SlimeStaff` (`1309`) | core/world-drop размер `26×28`, maximum stack `9999`, обычная gravity, summon natural-prefix family; swing use: animation/use time $28\,\text{тиков}$, auto-reuse, без turn |

Каждое импортированное определение содержит валидный `VanillaItemRuntimeDefaults`. Optional capability records теперь такие:

- `VanillaItemPlacementDefinition`;
- `VanillaItemPickToolDefinition`;
- `VanillaItemUseTimingDefinition` с named `VanillaItemUseStyle`;
- `VanillaItemWorldDropDefinition`.

Gameplay запрашивает их через `TryGetPlacement`, `TryGetPickTool`, `TryGetUseTiming` и `TryGetWorldDrop`. Отсутствующая capability завершается fail-closed. Placement/tool semantic intents теперь несут verified timing snapshot, поэтому будущему executor не нужно восстанавливать `useStyle`, animation или reuse behavior по item ID.

## Source-backed defaults и проверка stack

В TerrariaServer 1.4.5.8 `Item.ResetStats` инициализирует `maxStack` значением `Item.CommonMaxStack`, равным `9999`; ни одно из четырёх импортированных определений не переопределяет его. `TryGetRuntimeDefaults` выдаёт проверенные размеры и maximum.

Inventory normalization, mutations сохранённого inventory и semantic item-use requests отвергают stack выше известного импортированного maximum. Каталог намеренно разрежен, поэтому положительные protocol-valid stacks для canonical, но ещё не импортированных item types остаются допустимыми до появления source-backed defaults: выдуманный maximum для отсутствующей metadata отвергал бы легальные items.

## World-drop defaults

Pinned NPC-loot source contract доказывает, что Gel и Slime Staff не входят в `ItemID.Sets.ItemNoGravity`. Поэтому vanilla `Item.NewItem` использует

$$
v_x=0.1R_x,\quad R_x\in[-30,30],
$$

$$
v_y=0.1R_y,\quad R_y\in[-40,-16].
$$

`VanillaItemWorldDropDefinition` хранит размеры, gravity branch и проверенную natural-prefix family. Это не попытка скопировать целиком `Item.SetDefaults`.

## Prefix metadata

`VanillaItemPrefixCatalog` содержит только source-backed prefix-факты, которые уже нужны gameplay. Для первого среза это точная summon family из 22 prefix ID и закреплённый набор `ReducedNaturalChance`. Item-specific проверка Slime Staff отвергает natural prefixes `55`, `89` и `91`: после применения их damage multiplier `Math.Round` снова даёт исходные `8` damage, поэтому vanilla `Prefix(-1)` делает reroll.

Эти данные использует `VanillaNaturalItemPrefixRoller`; они остаются отдельно от mutable `PrefixId` конкретного inventory/world item.

## Проверка источником

Три постоянных official-source gate защищают этот разреженный каталог:

- `probe_item_definitions.py` проверяет импортированные core defaults, maximum stack и факты use timing/control;
- `probe_tile_authority.py` проверяет placement/tool факты Dirt Block и Copper Pickaxe;
- `probe_npc_loot_spawn.py` проверяет размеры Gel/Slime Staff, gravity branch, summon family, ветки вероятностей natural prefix и item-specific prefix validity по pinned Windows TerrariaServer 1.4.5.8.

Runtime-тесты затем проверяют typed representation и fail-closed capability queries.

## Охват

Damage, ammo, healing, equipment behavior и остальные item-поля добавляются только тогда, когда authoritative gameplay действительно начинает их использовать и для них закреплено official-source evidence. Гигантская спекулятивная таблица просто превратила бы неизвестные значения в уверенно неправильные defaults, а подобной инженерной роскоши и без нас хватает.
