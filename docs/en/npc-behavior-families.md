# NPC behavior-family dispatch

[Русский](../ru/npc-behavior-families.md) · [Gameplay](gameplay.md) · [Gameplay decomposition roadmap](../roadmap/gameplay-decomposition-and-catalogs.md)

## Purpose

TerraRuntime keeps two different NPC concepts separate:

- `NpcAiStyleId` is a source-backed Terraria 1.4.5.8 fact stored in the vanilla definition;
- `VanillaNpcBehaviorFamily` is a runtime-owned opt-in to an implementation strategy that has actually been verified for that definition.

They are deliberately not interchangeable. Terraria contains many NPCs that share an `aiStyle` while still having type-specific branches, parameters or lifecycle rules. Automatically routing every future NPC with `aiStyle = 3` through the current Zombie implementation would turn a useful source fact into an unsafe assumption.

## Current verified mapping

| NPC | source `AiStyle` | runtime behavior family |
| --- | --- | --- |
| Blue Slime | `Slime` | `SlimeGround` |
| Demon Eye | `DemonEye` | `FlyingEye` |
| Zombie | `Fighter` | `GroundFighter` |
| Eye of Cthulhu | `EyeOfCthulhu` | `EyeOfCthulhu` |
| Servant of Cthulhu | `Flyer` | `Flyer` |
| Skeleton | `Fighter` | `GroundFighter` |
| King Slime | `KingSlime` | `KingSlime` |

Only entries already present in the version-pinned `VanillaNpcDefinitionCatalog` receive a family. Adding another vanilla definition requires an explicit behavior-family decision; sharing an aiStyle alone is not sufficient evidence.

## Ownership after decomposition

The public compatibility facade no longer contains the family implementations.

```mermaid
flowchart LR
    Snapshot["NpcSnapshot"] --> Facade["VanillaNpcTargetingAiStepper<br/>type + definition + family dispatch"]
    Facade --> Context["VanillaNpcBehaviorContext<br/>bounded candidates + world conditions"]
    Facade --> Slime["VanillaSlimeGroundNpcBehaviorStrategy"]
    Facade --> Eye["VanillaFlyingEyeNpcBehaviorStrategy"]
    Facade --> Fighter["VanillaGroundFighterNpcBehaviorStrategy"]
    Slime --> Context
    Eye --> Context
    Fighter --> Context
    Facade --> Fallback["bounded inner stepper"]
```

Ownership is intentionally narrow:

- `VanillaNpcTargetingAiStepper` resolves `NpcTypeId`, looks up one `VanillaNpcDefinition`, selects the explicit family and preserves the existing fallback contract;
- `VanillaNpcBehaviorContext` owns the fixed-size target-candidate scratch buffer, target geometry helpers, world-surface conversion and current day/slime-rain facts;
- `VanillaSlimeGroundNpcBehaviorStrategy` owns Slime-family engagement/targeting input and the verified `VanillaBlueSlimeMotion` transition;
- `VanillaFlyingEyeNpcBehaviorStrategy` owns FlyingEye target refresh before the independently implemented eye AI receives the state;
- `VanillaGroundFighterNpcBehaviorStrategy` owns Fighter-family target prepass, overlap semantics, day/surface pursuit policy and the verified `VanillaZombieMotion` compatibility transition. `VanillaGroundFighterBehaviorCatalog` retains admitted type-specific parameters such as Skeleton's `1.5f` base speed.

The facade keeps `EnableBlueSlimeMotion`, `EnableZombieMotion`, `SetWorldConditions` and `SetCandidates` so runtime composition and existing callers do not need a simultaneous API migration. Those methods now configure the context rather than accumulating behavior inside the dispatcher.

## Dispatch invariants

Each concrete strategy still checks the expected source-backed `AiStyle`. `BehaviorFamily` chooses the implementation, while `AiStyle` proves that the selected definition still satisfies the source invariant used to verify that implementation.

A family that is not enabled falls through to the bounded inner stepper exactly as before. A valid NPC type that is not present in the definition catalog also falls through rather than inheriting behavior from a numerically similar type or aiStyle.

The separation remains:

```text
Terraria fact             TerraRuntime implementation decision
AiStyle = Fighter   !=    BehaviorFamily = GroundFighter
```

## Eye of Cthulhu difficulty boundary

`VanillaEyeOfCthulhuMotion` now consumes the live Expert-mode world condition for the complete first-phase `AI_004` branch verified against TerrariaServer `1.4.5.8`. That slice includes the Expert hover speed and acceleration, the $210\,\text{tick}$ hover window, the $44\,\text{tick}$ Servant cadence at any vertical offset, $6\,\text{pixels/tick}$ Servant launches, $7\,\text{pixels/tick}$ direct dashes, the sequential `0.98f` and `0.985f` dash slowdown, the $100\,\text{tick}$ dash window and the transition below $65\%$ life. Servant creation remains a post-commit spawn intent, so cadence state and the irreversible child allocation cannot diverge.

This is a bounded capability, recorded as `BossExpertPhaseOneSlice`; it is not a full difficulty claim. Expert transformation and phase two remain fail-closed because transformation introduces random Servant spawns and later states introduce RNG-shaped rapid dashes. Master damage scaling and `getGoodWorld` parameter/effect branches are also still outside this slice. Classic behavior remains unchanged.

## GroundFighter door and tall-gate interactions

The admitted `GroundFighter` slice now carries the full source-backed door-pressure path:

- `VanillaWorldZombieDoorContact` accumulates per-contact `ai[1]` pressure (5 per door, 2 per tall-gate, plus type-specific bonuses) and emits a typed `VanillaGroundFighterDoorOpeningIntent` at the vanilla 10-point threshold;
- `VanillaWorldGroundFighterDoorOpeningService` executes the exact `WorldGen.OpenDoor` / `ShiftTallGate` mutations (locked-door rejection, `1x3 -> 2x3` frame/style transform, paint/coating transfer, `tileCut` clearance, `388 -> 389` type shift);
- `VanillaWorldUnbreakableWallScan` replicates `UnbreakableWallScan.InsideUnbreakableWalls` (8 directions ×250 tiles, wall 350, color ≥16) and feeds `TargetInsideUnbreakableWalls` into the pressure policy for the `+6` bonus;
- `RuntimeTallGateOccupancyProbe` implements `Collision.EmptyTile(ignoreTiles:true)` by testing live player (`20×42`) and NPC (live hitbox) rectangles against each gate tile, now wired in `ServerRuntimeState` through `RuntimeGroundFighterDoorOpeningSink` with packet-19 replication.

Normal doors open without an occupancy probe; tall-gates fail closed when any of the five gate tiles is occupied and succeed only when all five are free, matching vanilla `ShiftTallGate` semantics. The `GetGoodWorld` seed flag and `insideUnbreakableWalls` target state are now projected from live world state rather than defaulting to `false`.

## Why the roadmap AI-decomposition item is complete

The D4 `AI family/behavior decomposition` item tracks ownership and dispatch architecture, not exhaustive support for every Terraria NPC. For the authoritative vanilla NPC slice currently admitted by `VanillaNpcDefinitionCatalog`, family selection, shared context and family behavior are now separate units with executable coverage. Adding future NPC definitions extends this architecture instead of reopening a monolithic dispatcher.

All D4 checkboxes describe decomposition/ownership for admitted slices. They do not claim exhaustive NPC support. `VanillaNpcAiCoverageCatalog` keeps every current `FullVanillaAiParity` claim false, and the remaining roster is tracked in the [NPC/AI parity roadmap](../roadmap/npc-ai-parity.md).

## Verification

`VanillaNpcBehaviorFamilyDispatchTests` pins the fail-closed dispatch contract: disabled families fall back, unknown catalog types do not inherit a behavior, and FlyingEye target refresh occurs in the family strategy before delegation. NPC-specific suites cover the admitted ordinary and boss slices, while `VanillaNpcAiCoverageCatalogTests` prevents those slices from being mislabeled as full parity.
