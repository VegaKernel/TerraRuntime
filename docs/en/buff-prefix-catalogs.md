# Buff and prefix catalogs

[Русский](../ru/buff-prefix-catalogs.md) · [Gameplay decomposition roadmap](../roadmap/gameplay-decomposition-and-catalogs.md)

TerraRuntime represents TerrariaServer 1.4.5.8 buffs and item prefixes with protocol-neutral `BuffTypeId` and `PrefixId` values. Raw packet/file bytes are validated before they become these identities.

## Identity ranges

`VanillaBuffIds` pins the valid buff range to `0..400` (`Count = 401`) and `VanillaPrefixIds` pins prefixes to `0..97` (`Count = 98`). Zero is the normalized none identity. Named members are added when runtime rules consume them; range validation does not imply that every buff effect or prefix stat family is implemented.

## Buff definitions

`VanillaBuffDefinitionCatalog` provides a dense identity view plus selected source-backed `BuffID.Sets` traits:

- well-fed and broader fed-state membership;
- flask/weapon-imbue membership;
- debuff-time extension with game difficulty.

Unknown behavior is not inferred from a buff name. Combat effects, stacking, immunity, removal and replication remain separate authoritative subsystems.

## Prefix definitions

`VanillaItemPrefixCatalog` now exposes catalog validation through `VanillaPrefixDefinition`, named summon-rollable identities and the verified reduced-natural-chance trait. The existing natural `Prefix(-1)` roller consumes those named definitions and retains its source-backed RNG order and Slime Staff rounding guard.

The definition catalog validates every vanilla prefix identity but only claims behavioral knowledge represented by explicit traits. Other item-family stat multipliers and reforging rules remain capability gaps.
