# Runtime NPC behavior boundary

TerraRuntime exposes a trusted-host NPC behavior boundary without transferring simulation ownership to the host. The runtime remains the only authority that owns NPC lifecycle, generation identity, client-visible presentation, state validation, authoritative ticking, world motion/collision, combat and replication.

This document describes the TerraRuntime boundary only. Host-framework adapters, plugin discovery, permissions and Vega-specific APIs are intentionally outside this layer.

## Identity, presentation and behavior are separate

A server-defined NPC uses three independent identities:

- `GameplayArchetypeId` is the stable runtime/host identity of the custom archetype.
- `NpcArchetypeDescriptor.VanillaPresentationType` is the vanilla NPC type sent to an unmodified Terraria client.
- `NpcArchetypeDescriptor.BehaviorId` selects the runtime behavior registered for that archetype.

For example, `example:resident-zombie` may use `VanillaNpcIds.Zombie` as its presentation while selecting `example:resident-ai` as its behavior. The official client still renders a normal Zombie. TerraRuntime keeps the custom archetype identity separately and dispatches the registered behavior for the exact live NPC generation.

Behavior code cannot change `Type` or `NetId`. `NpcBehaviorState` deliberately contains only mutable simulation state such as position, velocity, target, `ai[]` and `NpcSimulationState`. TerraRuntime copies the current authoritative `Type` and `NetId` into the internal state transition after the callback returns.

## Registration surface

`INpcActorOperations` exposes two behavior registration lanes.

`RegisterBehaviorAsync(GameplayExtensionId, INpcBehaviorProvider, ...)` registers an archetype-addressed exclusive replacement. A custom `NpcArchetypeDescriptor` selects it through `BehaviorId`. Multiple custom archetypes can share the same vanilla presentation type while using different behavior IDs.

`RegisterPresentationBehaviorAsync(GameplayExtensionId, NpcTypeId, NpcBehaviorStage, int order, INpcBehaviorProvider, ...)` registers behavior against a vanilla presentation type. The supported stages are:

1. `Pre`, ordered decorators before the replacement/default behavior.
2. `Replacement`, the single type-level replacement when no archetype-specific replacement is selected.
3. `Post`, ordered decorators after the replacement/default behavior.

Archetype-specific replacement has priority over the type-level replacement. Type-level pre/post decorators still surround the selected replacement.

Registration is serialized through the authoritative runtime command queue. A successful result means the registration was accepted and staged; the immutable dispatch snapshot becomes visible at the next authoritative safe boundary. Disposing `INpcBehaviorRegistration` stages retirement, which is likewise published only at a safe boundary.

## Behavior callback

`INpcBehaviorProvider.TryStep` is synchronous and executes on the authoritative runtime thread. It receives a stack-only `NpcBehaviorContext` containing:

- the stable behavior ID;
- the custom archetype ID when the exact live generation belongs to a registered archetype;
- the current immutable `NpcSnapshot`;
- current authoritative tick number;
- generation-safe player and NPC snapshot queries;
- bounded NPC enumeration into caller-provided `Span<NpcSnapshot>`;
- solid-collision and line-of-sight world queries.

The callback proposes an `NpcBehaviorState`. TerraRuntime remains responsible for committing or rejecting that proposal and for all later authoritative simulation stages.

Callbacks must not block, sleep, perform I/O, wait on tasks or start a second simulation owner. The boundary does not expose mutable NPC arrays, tile storage, packet queues or arbitrary packet sending.

## Authoritative pipeline

For an uncontrolled NPC, the runtime behavior portion of the tick is conceptually:

```mermaid
flowchart TD
    Pre["presentation Pre decorators"] --> Choice{"replacement selected?"}
    Choice -->|archetype BehaviorId| Archetype["archetype replacement"]
    Choice -->|presentation replacement| Presentation["presentation replacement"]
    Choice -->|none| Vanilla["vanilla/default AI"]
    Archetype --> Post["presentation Post decorators"]
    Presentation --> Post
    Vanilla --> Post
    Post --> Intent["runtime-owned actor intent override, if leased"]
    Intent --> Motion["runtime-owned world motion/collision + remaining AI capabilities"]
    Motion --> Commit["authoritative store commit"]
    Commit --> Replication["replication"]
```

The behavior dispatcher is part of the production `NpcAuthority` AI chain driven by `ServerRuntimeState`. It is not a test-only registry. `INpcAiStateStepperWrapper` composition is preserved so nested vanilla capabilities such as targeting, spawn planners, projectile planners and post-commit hooks remain discoverable through the wrapper chain.

## Bounded world queries

`NpcBehaviorContext` deliberately exposes semantic queries rather than raw world data:

- `TryGetPlayer(PlayerHandle, ...)`
- `TryGetPlayer(PlayerSlotId, ...)`
- `TryGetNpc(NpcHandle, ...)`
- `CopyNpcs(Span<NpcSnapshot>)`
- `HasSolidCollision(NpcBehaviorBounds)`
- `HasLineOfSight(NpcBehaviorBounds, NpcBehaviorBounds)`

Player and NPC identities are generation-safe where a generation-bearing handle is used. Collision and line-of-sight delegate to TerraRuntime's version-pinned world collision primitives.

This slice does not expose arbitrary child-NPC or projectile spawning from a behavior callback. Those operations require separately bounded runtime-owned request APIs rather than leaking stores or packet access into the callback.

## Source-backed Wall of Flesh vertical slice

The pre-Hardmode boss boundary now also admits Wall of Flesh (`NPC 113`) and its linked server-owned children rather than relying on a generic fallback. The root performs the source-shaped corridor movement and initial 13-child bootstrap (two eyes plus eleven Hungry), while eye/Hungry state preserves explicit root ownership. Runtime post-state intents cover leeches, Good World Fire Imp support, Expert Hungry pressure and eye laser projectile `83`; eye damage is committed back to the shared root life before lethal finalization.

The death path owns the gameplay mutations that must happen on the server: normal/Expert/Master loot delivery, source-shaped recovery drops, the Demonite/Crimtane brick box with liquid clearing, child cleanup and the persisted Hardmode progression mutation. Cosmetic dust/gore/sound and client presentation remain outside this boundary.

## Source-backed Deerclops AI_123 slice

The current vanilla behavior chain includes the gameplay-owned TerrariaServer 1.4.5.8 Deerclops (`NPC 668`, `aiStyle 123`) vertical slice. The runtime retains the source state machine rather than collapsing the boss into a generic ground-chaser:

- state `0`: chase and source-ordered attack selection;
- states `1` and `4`: forward and bilateral ice-spike attacks;
- state `2`: rubble volley;
- state `3`: slow-scream timing;
- state `5`: six-shadow-hand burst;
- states `6` and `7`: return-home and teleport-home recovery;
- state `8`: timeout despawn without ordinary boss death loot.

`NpcAuthority` supplies a `WorldTileStore`-backed environment through semantic snow, walkability, solid-tile and collision queries. Deerclops projectile side effects remain runtime-owned post-state intents: ice spike `961`, rubble `962` and shadow hand `965` are admitted through the normal projectile authority rather than being published from inside the AI callback. The distance shield (`localAI[3]`) and its thirty-tick invulnerability threshold are authoritative gameplay state.

The dedicated-server-only Expert passive-shadow-hand branch is now authoritative. `localAI[2]` uses the source life-scaled 80→40 tick cadence, rotates through three player-slot groups, requires generation-safe per-NPC interaction credit and enforces the 1200-pixel range before staging projectile `965` with source damage `10`. The slow-scream state still deliberately does not apply vanilla `Slow (buff 32, 720 ticks)`: the pinned branch is excluded on `Main.netMode == 2`, so adding that buff on the dedicated server would be false parity rather than completion. Deerclops therefore remains below full vanilla AI parity only for broader/shared gaps, not for this server-executed projectile branch.

## Source-backed late Hardmode and endgame boss slice

The runtime-owned boss boundary now carries the remaining server-authoritative NPC-side state needed by the late Hardmode/endgame roster instead of leaving the encounter roots attached to metadata-only projectile placeholders. Duke Fishron admits AI 71 Sharkron/Sharkron2 emergence and charge state plus source-owned Sharknado `385`. Lunatic Cultist admits Ancient Vision/Light/Doom state, ritual Dragon-or-Vision spawning, ritual interruption by the real boss or a struck clone, and the source-owned `464/465/467/468/490/593` attack families.

Empress of Light stages the specialized lasting-rainbow, rainbow-streak, lance and sun-dance projectile families (`872/873/919/923`); daytime rage also promotes the source cadence and `9999` projectile damage rather than relying only on a remembered `ai[3]` flag. Moon Lord hand/head/True Eye attack clocks follow their pinned 600/1200-tick source sequences and stage Phantasmal eye/sphere/deathray/leech/bolt intents (`452/454/455/456/462`). The first lethal strike against a hand or head now follows the pinned `NPC.checkDead` survival transition: life is restored, the part becomes invulnerable in `ai[0] = -2`, exactly one True Eye is spawned from that part with the source 1200-tick loop / 588-base / 400-per-existing-eye phase calculation, and the core opens only while both owned hands plus the owned head still exist in their retired state. A retired head also enters `ai[0] = -3` when the core begins its death drama. Cosmetic sound/dust/gore remains outside authority. Remaining Moon Lord work is the core's full 600-tick terminal death sequence and exact vanilla child-slot-loss self-termination; specialized projectile-style internals and remaining seed-only Empress conditions also remain explicit parity work.

## Lifecycle and unload

Behavior registrations are leases. The extensible host scope tracks them together with custom actor and archetype leases. Scope retirement performs cleanup in this order:

1. release actor controllers;
2. despawn actors owned by the scope;
3. retire behavior registrations;
4. retire archetype registrations.

The registry publishes immutable snapshots only at safe boundaries, so callbacks are never removed by mutating a dispatch table while the authoritative tick is enumerating it. After retirement is committed, the published snapshot no longer retains the provider callback.

## Example: resident-like Zombie

A host can register a behavior ID, then register an archetype that presents as a vanilla Zombie and references that behavior:

```csharp
var behaviorId = new GameplayExtensionId("example:resident-ai");
var archetypeId = new GameplayArchetypeId("example:resident-zombie");

NpcBehaviorRegistrationResult behavior = await runtime.NpcActors.RegisterBehaviorAsync(
    behaviorId,
    new ResidentZombieBehavior());

runtime.NpcActors.TryRegisterArchetype(
    new NpcArchetypeDescriptor(
        archetypeId,
        VanillaNpcIds.Zombie,
        behaviorId),
    out INpcArchetypeRegistration? archetype);

NpcActorSpawnResult spawned = await runtime.NpcActors.SpawnAsync(
    new NpcActorSpawnRequest(archetypeId, 100f, 200f));
```

This is an AI/runtime customization boundary, not automatic Town NPC membership. Housing, happiness, pylons, shops, arrival rules and other Town NPC systems remain explicit gameplay subsystems and are not inferred from a custom NPC's presentation or behavior.
