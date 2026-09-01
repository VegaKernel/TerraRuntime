# Muse Spark NPC AI integration audit

The `muse-spark` branch (`a6ab21a`) was audited against the pinned TerrariaServer 1.4.5.8 decompile before integration.
The branch is useful as a discovery map, but its broad capability claims are not mergeable as vanilla parity evidence.

## Accepted in this integration

- AI_017 Vulture (61) and Raven (301): exact SetDefaults facts and server-relevant activation, collision rebound,
  target steering and wet escape state are admitted.
- AI_020 Spike Ball (70): exact invulnerable SetDefaults state, RNG amplitude, vertical/horizontal phase transition
  and acceleration state machine are admitted.
- AI_021 Blazing Wheel (72): exact invulnerable SetDefaults state and collision-history wall-following state machine
  are admitted; rotation/lighting remain presentation-only and intentionally stay outside the authoritative slice.
- `VanillaNpcDefinition.DontTakeDamageAtSpawn` was added because invulnerability is a SetDefaults fact and must be
  materialized before the first AI tick instead of being repaired by a later behavior update.

## Rejected from direct merge

- The Muse `DemonTaxCollector = 169` identity conflicts with the already source-backed runtime identity 534.
- Queen Bee, Wall of Flesh, Skeletron, mechanical bosses and several later bosses are movement approximations, not
  TerrariaServer behavior. They remain unadmitted.
- `VanillaRemaining127NpcCatalog` fabricated behavior/physics enum values with `(aiStyle + 100)` and routed many unrelated
  AI styles through one generic steering algorithm. This violates fail-closed admission and is discarded.
- The Muse dispatcher routed unrelated bosses through Wall-of-Flesh-eye or Queen-Bee strategies. Those routes are discarded.
- Muse coverage tests largely proved that claims existed in the same catalog that declared them. No capability from that
  self-referential coverage expansion is retained without an independent behavior regression and pinned source contract.

## Next salvage order

The remaining Muse files are reference material only. Promote them family by family in increasing dependency cost:
AI_016 fish, AI_018 jellyfish, AI_019 antlion, AI_014 flying/bat variants, then AI_008 caster after an authoritative
teleport/world-query boundary exists. Boss families stay last because their child/projectile/progression ownership is not
replaceable by generic steering.
