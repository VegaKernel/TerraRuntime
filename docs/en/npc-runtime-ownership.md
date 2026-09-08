# NPC runtime ownership boundaries

Clientless server players remain combat participants but have no packet90 consumer. Difficulty-loot recipient projection now requires an exact-generation playing/open client-local item receiver; otherwise personalized rewards are withheld rather than aborting the lethal hit, granting items directly or redirecting them to observers. This is an explicit fail-closed runtime-extension policy, not a vanilla player-eligibility claim. Classic world drops and ordinary bot pickup are unchanged. Twelve synthetic boundary cases and two real Guard-bot Expert/Master kills reproduce the former exception; restoring the gate passes, including repeated bot runs. An actor-owned personalized-loot adapter remains open.

Eye of Cthulhu now has its missing NPC-specific death-loot dispatch and first-boss progression. Classic ore/seeds follow the world-owned evil flag; Expert bag, Master relic/Aviator Sunglasses/per-player pet and all-mode trophy preserve source rule order. The existing network/server-player/town-NPC death branches mark the Eye milestone, and the existing world-header patcher changes only downedBoss1. Tests cover both world evils/all difficulties, recipient isolation, duplicate kills and byte-exact/idempotent persistence (27 focused tests; 11 negative-control failures). Global event/announcement, global loot and full encounter AI remain open.

Ordinary mechanical encounter loot now uses the same death boundary: Classic Hallowed Bars15–30/Souls25–40 and masks, Expert bags, Master relic/pets, later trophies. MissingTwin checks active opposite eyes before encounter rewards; each eye's trophy is independent. NPC.ApplyInteraction propagation now credits all active Twins or Prime parts through the existing generation-safe ledger in network and server-player damage paths. Twenty verified item defaults preserve no-gravity Soul velocity RNG. Source-order and production-delivery tests cover all roots/difficulties, both eye kill orders and four Prime arm-credit paths (65 tests; 18 negative-control failures). Mechdusa/Waffle Iron, global loot, bag opening and full encounter AI remain open.

Queen Slime's NPC-specific death rewards now use the existing imported-loot boundary and item delivery/lease stores. Source-ordered Classic rules include gel, mask, one armor piece, Blade Staff, mount and the raw-RNG hook; Expert/Master bags remain addressed packet90 copies, with Master relic/per-interactor pet and all-mode trophy. Twelve world-drop definitions do not admit unverified item-use behavior. Blade Staff natural prefix validity respects damage6/zero knockback. Exact-RNG and real authoritative-player-kill tests cover all three difficulties, packet21/90 recipients, unpublished leases and duplicate-kill rejection; negative controls produce ten failures. Global drops, opening bags and other Hardmode boss tables remain open.

AI70 now initializes speed/scale/offsets only for an incoming unassigned target, then applies homing, jitter and the world-owned wind input. Loaded wind comes from the persisted target, as in `WorldFile.LoadWorld`; weather evolution/rain amplification remains open. Detonation checks all active living players using the source's asymmetric integer rectangle and base player dimensions. `checkDead` prepares the four-update detonation through shared damage instead of ordinary death/loot. The physical body expands from $36\times36\,\mathrm{px}$ to $100\times100\,\mathrm{px}$ around its old center independently of scale. Bounded nullable `NpcSimulationState.HitboxOverride` shares the authoritative revision; targeting, collision, combat and packet anchors consume it through the definition helper. State-only updates preserve it; new-definition defaults do not inherit it. Target loss cannot suspend expiry. Tests cover source boundaries, expansion, invalid bodies, preservation, packet geometry and production bot contact; removing the physical/death/proximity rules causes eleven failures. Mounted-player proximity dimensions and live weather remain limitations.

Duke bubble creation now uses incoming `ai[2] % 4 == 0`, including tick zero. Phase one creates an unassigned, zero-velocity/default-AI bubble; phase two uses the incoming (pre-circle-rotation) direction for the mouth and perpendicular speed, assigns the target and draws only scale. AI70's lifetime no longer increments during detonation: the countdown reaches zero and the existing post-AI expiry path retires that exact NPC without combat loot/progression. Fourteen tests cover spawn timing/defaults/geometry and the four-update timeout sequence. The follow-on AI70 implementation below adds motion initialization and physical detonation; live weather evolution remains open.

The next verified AI69 slice restores phase-one/two attack-cycle resets and waits for the hover boundary before phase changes. Phase-one bubble/shark attacks leave cycles `1`/`0`; phase-two uses six dash steps, then circle/shark steps, resetting to `1`/`0`. Phase-two circle and state `13` rotate incoming velocity instead of hovering/freezing; hover reversal acceleration and facing follow the source. Tests cover each selector/reset and signed circle motion; reverting resets/rotation causes eight failures. Ocean/enrage and remaining phase details are still open, so this does not close the Duke encounter.

TZ-35 adds server-owned nullable `Friendly`, `Chaseable` and `Immortal` to the committed simulation revision. Null means unspecified (targeting fails closed); verified spawn defaults materialize values, state-only updates preserve them, and AI writes live transitions atomically. Controlled magic and bot Guard consume these flags through the source-backed `CanBeChasedBy` predicate; NPC contact also rejects friendly instances. Clone 440 and Ancient Doom 523 start unchaseable. Duke states 10/12 clear chaseability, 11 sets it, and untouched branches retain it, including transition ticks. Cultist intro/ritual and reposition damage gates follow the executed source branch. This does not claim every NPC's transient allegiance/immortality branch or temporary catchable immunity is implemented.

[Русский](../ru/npc-runtime-ownership.md) · [NPC behavior families](npc-behavior-families.md) · [Gameplay decomposition roadmap](../roadmap/gameplay-decomposition-and-catalogs.md)

The follow-on Duke Fishron AI69 slice fixes actual phase-three relocation at incoming timer `15`, its opposite-side destination, damping/fade, and the nine-step one/two/three-dash cycle between teleports. Focused tests pin the adjacent timer boundaries and all nine decisions; restoring the old wait-only/modulo-four behavior makes six regressions fail. This is not full Duke parity: ocean/enrage inputs, remaining phase details and official-client combat acceptance remain open.

TerraRuntime keeps NPC storage, spawn/default materialization, AI, physics, combat and loot as separate ownership layers. The point is not directory decoration. Each layer must be able to evolve without teaching the slot store about vanilla combat rules or teaching physics about concrete NPC content IDs.

`TerraRuntime.Gameplay.Npcs` owns the immutable source-backed vanilla definition layer: `VanillaNpcDefinition`, behavior/physics family metadata, net variants, and the admitted slime/flying-eye/flyer/worm/AI17-20-21/town definition catalogs. Core consumes those facts but does not own them. Mutable NPC slots, authoritative state transitions, AI execution, combat and world-item transactions remain in Core/application runtime.

The same ownership rule now covers protocol-neutral NPC simulation and loot. Source-backed gravity, targeting, knockback, motion/check-active primitives, ordinary spawn cadence, town identity/rescue rules, AI coverage and boss-loot evaluators live in `TerraRuntime.Gameplay.Npcs`. Core keeps the mutable behavior context/state steppers, generation-safe interaction ledger, world-item materialization transactions and death/finalization steps. `NpcLootWorldItemOrigin` and the vanilla interactable-player slot ceiling are gameplay facts rather than properties of a particular runtime store.

The same boundary applies to NPC catching: `VanillaNpcCatchCatalog1458` owns immutable critter/catch-item facts in Gameplay, while `VanillaNpcCatchWorldItem1458` remains in Core because it materializes the captured world-item state and reservation policy.

```mermaid
flowchart LR
    Spawn["Spawn/update request"] --> Policy["RuntimeNpcStateOwnershipPolicy\nlocal defaults + preservation"]
    Policy --> Store["RuntimeNpcStore\nslot + generation + revision + commit"]
    Store --> AI["Behavior-family AI"]
    AI --> Physics["Physics-family world motion"]
    Store --> CustomRole["RuntimeNpcRoleBoundary\ncustom archetype role"]
    Store --> VanillaRole["RuntimeVanillaNpcRoleBoundary\nvanilla ordinary / town / boss"]
    Store --> Combat["RuntimeNpcDamageExecutor"]
    Combat --> Store
    Store --> DeathLoot["RuntimeNpcDeathLootFinalizer\nverified loot path"]
    Store --> DeathLifecycle["RuntimeNpcDeathLifecycleFinalizer\nunsupported-loot fallback"]
    DeathLoot --> Loot["Loot rules + world-item transaction"]
```

## Spawn and local state

`RuntimeNpcStore` owns addressable slots, active state, generation/revision monotonicity, snapshots and commit ordering. It no longer owns Terraria definition lookup or vanilla local-state defaults.

`RuntimeNpcStateOwnershipPolicy` owns the current verified spawn/update rules for fields that are not packet identity:

- definition-backed `Life/LifeMax` materialization;
- default active lifetime (`TimeLeft`);
- initial sprite direction;
- preservation of combat/lifetime/presentation state when an AI/state update deliberately leaves those fields unspecified.

This keeps storage generic while preserving the existing sentinel contract (`LifeMax == 0`, `TimeLeft == -1`, compatible zero sprite direction ingress).

AI-triggered child NPC creation crosses `INpcAiSpawnIntentPlanner` rather than mutating the slot store from inside AI. The executor supplies bounded scratch storage, planners may emit an ordered batch of zero or more intents, and the batch is applied only after the exact source generation commits. A rejected/stale source transition therefore cannot leak children into the world. After the source commit, individual child allocation follows vanilla-style best effort: if the NPC table fills mid-batch, already accepted children remain and later spawns may fail.

## AI family versus physics family

`VanillaNpcBehaviorFamily` and `VanillaNpcPhysicsFamily` are intentionally separate metadata. Sharing an AI implementation does not automatically prove shared collision, platform, gravity or obstacle behavior.

Current verified mappings are:

| NPC | behavior family | physics family |
| --- | --- | --- |
| Blue Slime | `SlimeGround` | `SlimeGround` |
| Demon Eye | `FlyingEye` | `FlyingEye` |
| Zombie | `GroundFighter` | `GroundFighter` |
| Eye of Cthulhu | `EyeOfCthulhu` | `NoClipFlight` |
| Servant of Cthulhu | `Flyer` | `NoClipFlight` |
| Skeleton | `GroundFighter` | `GroundFighter` |
| King Slime | `KingSlime` | `SlimeGround` |

The names line up only where the admitted source-backed behavior proves that relationship. They remain different fields so future definitions can diverge safely.

`VanillaNpcWorldMotionAiStepper` dispatches special movement and platform behavior from `PhysicsFamily`, not from `NpcTypeId`. `VanillaNpcGravity` accepts a resolved definition as its authoritative gameplay overload; raw/typed ID overloads remain compatibility boundaries and resolve the definition before entering the physics implementation.

## Door and tall-gate production wiring

`VanillaNpcWorldMotionAiStepper` now owns the production door-pressure projection and the tall-gate occupancy probe. `RuntimeWorldClock` supplies live `BloodMoonActive` (already filtered by `GetGoodWorld`) and `GetGoodWorld` itself; `VanillaWorldUnbreakableWallScan` supplies `TargetInsideUnbreakableWalls` (8×250 scan for wall 350, color ≥16); `RuntimeTallGateOccupancyProbe` supplies `IsActorFree` by testing live player (`20×42`) and NPC (live hitbox) rectangles via `Collision.EmptyTile(ignoreTiles:true)` semantics. The resolved `VanillaGroundFighterDoorEnvironment` therefore carries the exact vanilla policy inputs, and `VanillaWorldGroundFighterDoorOpeningService` executes the mutation behind `RuntimeGroundFighterDoorOpeningSink` which also replicates packet-19 to playing peers. Tall-gate opening fails closed without the probe; normal doors do not require it.

## Combat, death and loot

Combat remains owned by `RuntimeNpcDamageExecutor` plus `VanillaNpcDamageResolver`. Lethal damage commits `Life = 0` and does not despawn or roll loot inside the damage resolver.

For NPC types with imported source-backed loot, death/loot remains owned by `RuntimeNpcDeathLootFinalizer`, `VanillaNpcLootRules`, the vanilla world-item materializer and the generation-safe loot transaction. The store therefore does not know drop tables, prefix RNG or world-item capacity semantics.

A separate `RuntimeNpcDeathLifecycleFinalizer.TryFinalizeWhenLootUnsupported` closes entity lifecycle only for dead vanilla types whose loot table is not yet imported. It deliberately refuses any type already admitted by `VanillaNpcLootRuleCatalog`, so verified drops cannot be bypassed accidentally. A successful fallback means **loot parity is unresolved**, not that the vanilla drop set is empty. This lets partially implemented bosses such as the current Eye of Cthulhu slice despawn generation-safely at `Life = 0` without pretending its drops are complete.

## Town and boss role boundaries

`NpcArchetypeRole` classifies policy as `Ordinary`, `Town` or `Boss`, but runtime-defined/custom and vanilla identities reach that policy through different trusted sources.

`RuntimeNpcRoleBoundary` resolves a custom archetype role through the exact live `NpcHandle`, generation-safe archetype binding and one published descriptor revision. The role is custom runtime identity metadata and is never inferred from its vanilla presentation type or AI style.

`RuntimeVanillaNpcRoleBoundary` resolves a live vanilla generation through `RuntimeNpcStore` and the version-pinned `VanillaNpcDefinitionCatalog`. It fails closed for stale generations and unsupported vanilla types. The current source-backed Eye of Cthulhu definition therefore selects `Boss` lifecycle policy through its exact live handle, while Blue Slime, Demon Eye, Zombie and Servant remain `Ordinary`.

Both classification results expose mutually exclusive policy gates for town interaction, boss lifecycle or ordinary lifecycle. This prevents housing/shop policy from entering ordinary combat AI and prevents boss progression/despawn policy from becoming a raw type-number branch in the store.

These are ownership boundaries, not complete vanilla town/boss parity. Housing, boss progression, boss bars, remaining boss-specific death effects and broad boss AI still require separate source-backed implementation. The actor-commerce smoke continues to mark its custom merchant archetype as `Town` explicitly.

## D4 completion boundary

The roadmap item `spawn/physics/combat/loot separation` is considered complete for the currently admitted authoritative NPC slice because:

- slot storage no longer contains vanilla definition/default materialization;
- physics dispatch no longer branches on concrete Blue Slime/Demon Eye/Zombie IDs;
- AI child spawns cross a bounded post-commit intent boundary rather than mutating the store speculatively;
- combat, entity death lifecycle and verified death/loot execute through distinct generation-safe components;
- custom and vanilla role policy resolve through explicit generation-safe boundaries;
- tests pin catalog family selection and local-state ownership behavior;
- future definitions must explicitly opt into behavior and physics families.

This does not claim complete Terraria NPC support. Vanilla town/housing, boss progression and boss behavior breadth remain open even though their ownership boundaries are now explicit.
