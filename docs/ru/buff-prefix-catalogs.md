# Каталоги buffs и prefixes

[English](../en/buff-prefix-catalogs.md) · [Roadmap декомпозиции gameplay](../roadmap/gameplay-decomposition-and-catalogs.md)

TerraRuntime представляет buffs и item prefixes TerrariaServer 1.4.5.8 через protocol-neutral значения `BuffTypeId` и `PrefixId`. Raw packet/file bytes проходят validation до превращения в эти identities.

## Диапазоны identities

`VanillaBuffIds` закрепляет valid buff range `0..400` (`Count = 401`), а `VanillaPrefixIds` — prefixes `0..97` (`Count = 98`). Zero является normalized none identity. Named members добавляются, когда их используют runtime rules; range validation не означает реализацию каждого buff effect или prefix stat family.

## Buff definitions

`VanillaBuffDefinitionCatalog` предоставляет dense identity view и выбранные source-backed traits `BuffID.Sets`:

- membership well-fed и более широкого fed state;
- membership flask/weapon-imbue;
- extension времени debuff с game difficulty.

Unknown behavior не выводится из имени buff. Combat effects, stacking, immunity, removal и replication остаются отдельными authoritative subsystems.

## Prefix definitions

`VanillaItemPrefixCatalog` теперь предоставляет catalog validation через `VanillaPrefixDefinition`, named summon-rollable identities и проверенный trait reduced-natural-chance. Существующий natural roller `Prefix(-1)` использует эти named definitions и сохраняет source-backed RNG order и rounding guard Slime Staff.

Definition catalog проверяет каждую vanilla prefix identity, но заявляет behavioral knowledge только через explicit traits. Stat multipliers других item families и reforging rules остаются capability gaps.
