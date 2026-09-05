# Sparse vanilla item definitions

TerraRuntime uses a deliberately sparse, source-backed item-definition catalog. Missing metadata means **not verified/imported**, never an invented vanilla zero or `false`.

## Ownership

```mermaid
flowchart LR
    Source["TerrariaServer 1.4.5.8"] --> Probe["Pinned source contracts"]
    Probe --> Catalog["VanillaDefinitionCatalog"]
    Catalog --> Placement["Placement"]
    Catalog --> Tools["Tool authority"]
    Catalog --> WorldDrop["World-item materialization"]
    WorldDrop --> Prefixes["VanillaItemPrefixCatalog"]
```

`VanillaItemIds` owns content identity. `TerraRuntime.Gameplay.Items.VanillaDefinitionCatalog` owns immutable verified item facts, and the related object-placement/prefix catalogs live in the same gameplay layer. Runtime inventory/world-item stores in Core/application code own mutable stack, prefix, slot, generation and revision state.

## Verified capabilities

| Item | Verified capability facts |
|---|---|
| `DirtBlock` (`2`) | core defaults: `12×12`, maximum stack `9999`; placement: `createTile = 0`, `consumable = true`; swing use: animation $15\,\text{ticks}$, use time $10\,\text{ticks}$, auto-reuse, turn |
| `CopperPickaxe` (`3509`) | core defaults: `24×28`, maximum stack `9999`; pick tool: `pick = 35`, `tileBoost = -1`; swing use: animation $23\,\text{ticks}$, use time $15\,\text{ticks}$, auto-reuse, turn |
| `Gel` (`23`) | core/world-drop size `10×12`, maximum stack `9999`, ordinary gravity, no natural prefix family |
| `SlimeStaff` (`1309`) | core/world-drop size `26×28`, maximum stack `9999`, ordinary gravity, summon natural-prefix family; swing use: animation/use time $28\,\text{ticks}$, auto-reuse, no turn |

Every imported definition contains valid `VanillaItemRuntimeDefaults`. Optional capability records are:

- `VanillaItemPlacementDefinition`;
- `VanillaItemPickToolDefinition`;
- `VanillaItemUseTimingDefinition` with named `VanillaItemUseStyle`;
- `VanillaItemWorldDropDefinition`.

Code consumes them through `TryGetPlacement`, `TryGetPickTool`, `TryGetUseTiming` and `TryGetWorldDrop`. An absent record fails closed. Placement/tool semantic intents now carry the verified timing snapshot, so later executors do not need to recover `useStyle`, animation or reuse behavior from the item ID.

## Source-backed defaults and stack validation

TerrariaServer 1.4.5.8 `Item.ResetStats` initializes `maxStack` from `Item.CommonMaxStack`, which is `9999`; none of the four imported definitions overrides it. `TryGetRuntimeDefaults` exposes the verified dimensions and maximum.

Inventory normalization, stored inventory mutations and semantic item-use requests reject a stack above a known imported maximum. The catalog is deliberately sparse, so positive protocol-valid stacks for canonical but unimported item types remain accepted until their defaults are source-backed; treating missing metadata as a guessed maximum would reject legal items.

## World-drop defaults

The pinned NPC-loot source contract proves Gel and Slime Staff are not members of `ItemID.Sets.ItemNoGravity`. Their vanilla `Item.NewItem` default velocity therefore uses

$$
v_x=0.1R_x,\quad R_x\in[-30,30],
$$

$$
v_y=0.1R_y,\quad R_y\in[-40,-16].
$$

`VanillaItemWorldDropDefinition` stores dimensions, gravity branch and verified natural-prefix family. It does not pretend to be a full `Item.SetDefaults` copy.

## Prefix metadata

`VanillaItemPrefixCatalog` contains only source-backed prefix facts currently needed by gameplay. For the initial slice this is the exact 22-entry summon family and the pinned `ReducedNaturalChance` set. Slime Staff's item-specific stat-rounding validation rejects natural prefixes `55`, `89` and `91`, so `Prefix(-1)` must reroll them.

This metadata is consumed by `VanillaNaturalItemPrefixRoller`; it remains separate from mutable `PrefixId` stored on an inventory/world item.

## Verification

Three permanent official-source gates currently protect this sparse catalog:

- `probe_item_definitions.py` verifies the imported core defaults, stack maximum and use timing/control facts;
- `probe_tile_authority.py` verifies Dirt Block and Copper Pickaxe placement/tool facts;
- `probe_npc_loot_spawn.py` verifies Gel/Slime Staff dimensions, gravity branch, summon family, natural-prefix probability branches and item-specific prefix validity against the pinned TerrariaServer 1.4.5.8 Windows assembly.

Runtime tests then verify the typed representation and fail-closed capability queries.

## Scope

Damage, ammo, healing, equipment behavior and other item fields are added only when authoritative gameplay consumes them and official-source evidence is pinned. A giant speculative item table would merely convert unknowns into confidently wrong defaults, which is an impressively inefficient way to create bugs.
