# Runtime-controlled actors, fake players and commerce roadmap

This roadmap extends the gameplay/world-generation extensibility plan with runtime-owned actor control and interaction surfaces required by Vega plugins and minigames.

The governing rule remains unchanged:

> **Vega/plugins submit intent and policy. TerraRuntime owns simulation, validation, state mutation and replication.**

This stage is mandatory for the gameplay extensibility story. It is not a packet-scripting convenience layer.

---

## G6 - Runtime-controlled actors and commerce

### Goals

TerraRuntime must support:

- runtime-controlled NPC actors that use authoritative NPC movement, collision, gravity, liquids and lifecycle;
- high-level NPC intent such as `FollowPlayer`, `MoveTo`, `Stop`, `Face`, `KeepDistance` and later bounded patrol/escort behaviors;
- fake/server-controlled players that are real runtime entities rather than a stream of fabricated movement packets;
- server-controlled player movement using the same validated movement/collision/physics primitives as the runtime-owned player simulation path;
- interaction-capable NPCs and fake players where the official client protocol permits a useful representation;
- runtime-owned shop/catalog/transaction APIs so a Vega plugin can provide custom merchandise and pricing without mutating inventories or currency directly;
- deterministic lifecycle and hot-reload cleanup for actors, controllers and shop registrations.

### Non-goals

- Vega/plugins do not directly mutate NPC/player arrays;
- plugins do not simulate movement on their own thread;
- fake players are not implemented by blindly replaying packet 13/14/etc.;
- plugins do not directly debit coins, create inventory stacks or forge purchase-result packets;
- custom shops do not pretend the official client supports arbitrary new UI widgets. The runtime must use protocol-compatible vanilla UI when possible and expose a server-mediated fallback interaction model when it is not.

---

## G6.1 - Actor identity and control intent

Introduce stable runtime actor identities separate from protocol slots and connection identities.

Conceptual contracts:

```text
RuntimeActorId
RuntimeActorKind = Npc | FakePlayer
ActorControllerId
ActorControlLease

NpcActorCommand
  FollowPlayer(PlayerHandle target, FollowOptions options)
  MoveTo(Vector2 target, MoveOptions options)
  Stop()
  Face(Direction direction)
  KeepDistance(PlayerHandle target, float min, float max)

FakePlayerCommand
  MoveTo(...)
  FollowPlayer(...)
  Jump(...)
  UseItem(...)
  Stop()
```

Requirements:

- actor identity is generation-safe;
- controller ownership is explicit and lease-based;
- only one exclusive movement controller owns an actor at a time;
- command changes publish at an authoritative tick boundary;
- stale commands against reused NPC/player slots fail closed;
- commands are intent, not position writes;
- extension unload retires the controller and returns the actor to a defined fallback (`vanilla`, `idle`, or controlled despawn).

---

## G6.2 - Runtime-controlled NPC actors

The first implementation target is NPC actors because TerraRuntime already owns NPC AI state and source-backed world motion/collision.

Target pipeline:

```text
plugin/Vega command
  -> actor command registry
  -> resolve exact NPC handle/generation
  -> resolve target player snapshot
  -> actor intent stepper
  -> existing NPC behavior pipeline
  -> vanilla/source-backed world movement + collision
  -> RuntimeNpcStore validation/commit
  -> normal NPC replication
```

`FollowPlayer` must not teleport or directly set final positions. It should produce bounded movement intent/velocity/targeting that then passes through the normal runtime NPC movement path.

Initial `FollowOptions` should include runtime-validated values such as:

- desired distance;
- maximum horizontal speed;
- acceleration;
- jump/step behavior only where source-backed collision helpers support it;
- stop radius;
- optional maximum chase distance;
- fallback when the target player disconnects/dies/changes generation.

The runtime must expose observable command state for diagnostics without exposing mutable internals.

---

## G6.3 - Fake/server-controlled players

A fake player must be a runtime-owned player entity, not a connection pretending to be a human client.

Target architecture:

```text
FakePlayerArchetype / identity
  -> runtime player slot allocation
  -> server-owned PlayerHandle/generation
  -> appearance/equipment/vitals/inventory state
  -> runtime player physics simulation
  -> validated state commit
  -> ordinary player replication to real clients
```

Before this can be considered complete, TerraRuntime needs a server-owned player movement simulator. Current client player movement ingress is not sufficient because accepting plugin-supplied final position/velocity would bypass physics.

Required player-physics scope:

- gravity/fall speed;
- horizontal acceleration/friction;
- jump state;
- tile/platform collision;
- liquids;
- mount state only when explicitly supported;
- death/respawn lifecycle;
- deterministic authoritative tick ownership;
- no connection/session requirement for fake players.

Fake players must use protocol-valid player slots and appearance/equipment values. Slot allocation must coexist with real connections and never steal a live human player's slot.

A fake player's interaction authority remains server-side. Client packets claiming to own/control a fake player slot must be rejected.

---

## G6.4 - Interaction surface

Actors should expose semantic interaction rather than raw packet hooks.

Conceptual runtime events/requests:

```text
ActorInteractionRequest
ActorInteractionKind
NpcConversationRequest
NpcShopOpenRequest
NpcShopPurchaseRequest
```

The runtime validates:

- interacting player handle/session generation;
- target actor handle/generation;
- distance/range where applicable;
- actor availability;
- shop registration generation;
- requested item/quantity;
- inventory capacity;
- payment/currency rules;
- transaction replay/duplicate protection where required.

Plugins receive semantic requests and return policy/offer decisions, not raw network frames.

---

## G6.5 - Custom NPC shops

A Vega plugin must be able to register a shop for a runtime NPC/custom archetype.

Conceptual contracts:

```text
ShopId
ShopRegistrationLease
ShopCatalogSnapshot
ShopOffer
ShopPurchaseRequest
ShopPurchaseDecision
ShopPurchaseCommit

RegisterShop(ShopId, ActorSelector, IShopProvider)
OpenShop(PlayerHandle, NpcHandle/RuntimeActorId)
TryPurchase(...)
```

`ShopOffer` should support at minimum:

- protocol-valid vanilla item ID;
- stack/quantity limit;
- price;
- currency kind;
- optional availability predicate resolved by the provider;
- optional stable offer ID so catalog order is not identity.

### Transaction rule

Purchases must be atomic from the plugin's perspective:

```text
capture player + shop generation
  -> validate actor/range/catalog
  -> provider decision
  -> validate price/currency
  -> reserve/debit payment
  -> validate/commit inventory grant
  -> publish purchase result/event
```

If any authoritative commit fails, the purchase must fail without partial money/item mutation.

Plugins must never directly edit `RuntimePlayerInventoryStore` as the normal shop API.

### Currency

Initial implementation should support vanilla coin currency first. Custom plugin currencies may be added later through an explicit transactional currency provider contract. A custom currency provider must participate in prepare/commit/rollback semantics or another runtime-defined atomic protocol; a plain callback that says "I removed 50 tokens" is not sufficient.

### UI compatibility

The official client constrains what can be displayed.

- use vanilla shop/UI protocol where the selected actor/presentation permits it;
- never emit unknown item/content IDs;
- if arbitrary dynamic catalog UI cannot be represented safely in the vanilla shop protocol, expose a server-mediated interaction/command surface rather than spoofing unsupported client state;
- a future modified-client protocol extension may provide richer UI without changing the core transaction model.

---

## G6.6 - Vega/plugin SDK boundary

Expected Vega-facing convenience API:

```text
SpawnNpcActor(...)
ControlNpc(...)
FollowPlayer(...)
StopActor(...)
SpawnFakePlayer(...)
ControlFakePlayer(...)
RegisterShop(...)
UpdateShopCatalog(...)
```

These calls adapt to TerraRuntime contracts. Vega remains responsible for permissions, plugin configuration and plugin lifetime. TerraRuntime remains responsible for actual actor simulation and commerce transactions.

A plugin unload must retire:

- actor control leases;
- actor-specific extension state;
- shop registrations/catalog snapshots;
- pending purchase intents;
- fake-player ownership according to configured fallback/despawn policy.

---

## Delivery order

### G6-A - NPC actor control foundation

- stable actor/controller IDs;
- generation-safe NPC control bindings;
- `Stop`, `MoveTo`, `FollowPlayer` command state;
- player snapshot query adapter;
- NPC control stepper integrated with existing behavior/world-motion path;
- target disconnect/generation change handling;
- focused tests proving movement goes through authoritative NPC physics rather than position teleport.

### G6-B - NPC interactions and shops

- actor interaction request boundary;
- stable `ShopId` and registration lease;
- immutable shop catalog snapshot;
- vanilla item/price validation;
- atomic inventory + coin transaction path;
- purchase commit diagnostics/events;
- Vega adapter proof-of-concept with a custom merchant NPC.

### G6-C - Fake player foundation

- server-owned player identity/slot allocation separate from connection ownership;
- fake-player appearance/vitals/equipment/inventory state;
- replication to real players;
- rejection of client control packets for server-owned slots.

### G6-D - Runtime player physics

- server-owned player physics stepper;
- source-backed movement/collision/gravity/jump/liquid semantics;
- fake-player `MoveTo`/`FollowPlayer` controller;
- deterministic tick integration and performance gate.

### G6-E - Gameplay integration

- NPC escort/follow example;
- custom merchant example;
- fake-player/bot example;
- per-world enable/disable;
- plugin hot-reload/retirement tests;
- NativeAOT Linux/Windows coverage.

---

## Definition of done

G6 is not complete until:

- a Vega plugin can spawn/control an NPC and tell it to follow a specific live player while TerraRuntime performs the actual movement/collision physics;
- the NPC continues to replicate through the ordinary authoritative NPC path;
- player disconnect/slot reuse cannot redirect the NPC to a different generation by accident;
- a Vega plugin can attach a custom shop to a runtime NPC and supply protocol-valid vanilla merchandise;
- a purchase is validated and committed atomically by TerraRuntime, not by direct plugin inventory mutation;
- a fake player can exist without a client connection and is allocated without colliding with real-player slots;
- fake-player movement is produced by a runtime-owned player physics path rather than direct packet/position scripting;
- real clients cannot seize control of a fake player's slot;
- actor/shop/plugin unload leaves no stale callbacks, control leases, catalog state or entity-generation state;
- zero actor/shop registrations keep the ordinary vanilla/runtime path allocation-light;
- NativeAOT and normal CI remain green.
