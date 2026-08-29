# Synchronization, bootstrap and interest management

[Русский](../ru/synchronization.md) · [Documentation](README.md) · [Architecture](architecture.md) · [Performance roadmap](../roadmap/performance-tick-stability.md)

## 1. Scope

Synchronization is the boundary between authoritative runtime state and the per-client state that must be transmitted over Terraria protocol 326.

It includes initial join/bootstrap state, section delivery, player movement/event fanout, NPC/projectile/world-item/object replication, recipient selection, and future interest-management suppression/resync semantics.

Synchronization does not own gameplay mutation. It observes authoritative state/events and projects them to clients.

## 2. High-level flow

```text
authoritative game state/event
        |
        v
replication registry / projection
        |
        v
recipient decision
        |
        +--> global/vanilla-like routing
        +--> future interest-managed routing
        |
        v
packet encode / reusable frame
        |
        v
bounded connection queue
        |
        v
socket writer
```

A recipient decision must never mutate the underlying entity merely to make synchronization convenient.

## 3. Initial bootstrap

A player cannot enter normal gameplay immediately after TCP handshake. The runtime must establish enough world and entity state for the official client to transition safely.

The bootstrap path includes verified classes of traffic such as world metadata, required initial tile sections, global post-section state, currently relevant runtime entity bootstrap frames, and the final enter-world handoff.

The exact protocol ordering is protected by live official-world probes rather than documented here as a frozen packet-number recipe. When packet ordering changes based on verified 1.4.5.8 behavior, the executable live probe is the source of truth.

## 4. Bootstrap frame budget

Initial bootstrap is explicitly bounded so a valid world cannot fill the production outbound queue simply because one player joins.

`PlayerBootstrapFrameBudget` derives the structural maximum from fixed pre-enter frames, maximum initial tile-section count, maximum global post-section frames, and maximum world-item bootstrap frames.

The live integration probe currently uses a hard budget of **1536 frames**, deliberately below the production outbound queue depth of **4096 frames**.

This is a safety invariant, not a claim that ordinary joins should approach either number.

## 5. Sections

World state is partitioned into Terraria sections for initial and ongoing world synchronization.

Section work must eventually satisfy three independent constraints:

1. correctness: the client receives the world region required for its current state;
2. invalidation: a mutated section cannot remain indefinitely represented by stale encoded data;
3. budget: generating/compressing uncached sections cannot monopolize one simulation tick.

The long-term join pipeline therefore uses global subsystem budgets rather than granting a complete section-generation budget to each joining player.

## 6. Replication registries

TerraRuntime keeps replication separate from authoritative storage.

Current runtime code has dedicated replication state/registries for multiple domains including player events/movement, NPCs, projectiles, world items, chests, signs and tile manipulation.

This allows runtime stores to remain concerned with authoritative identity/state while synchronization tracks what needs to be projected or fanned out.

## 7. Shared frame principle

When multiple recipients need identical bytes, the preferred architecture is to encode one immutable frame and share that frame among recipient queues rather than re-encode identical state for every player.

This optimization is valid only when bytes are truly recipient-independent. Packets containing recipient-specific identity, slot remapping, visibility state or other personalized fields require separate projection.

## 8. Interest management ownership

Interest management belongs to TerraRuntime itself.

External hosts such as Vega receive only `IInterestManagementControl` with `IsEnabled` and `SetEnabled(bool)`.

Hosts may enable or disable the feature. They do not control spatial layout, enter/leave radius, hysteresis, entity-specific recipient policy, forced resync deadlines, full-state-on-entry behavior or stale visibility recovery.

Those rules remain runtime implementation details so two hosts cannot accidentally create incompatible networking semantics for the same TerraRuntime version.

## 9. Current spatial foundation

`RuntimeInterestRouter` already owns a section-based player spatial index and visibility tracker when world dimensions are available.

The current default player radii are:

```text
enter radius = 3 sections
leave radius = 4 sections
```

The larger leave radius provides hysteresis so a player near a boundary does not oscillate visible/invisible every time it crosses one section edge.

The router updates membership on tracked player movement and removes membership on disconnect/removal.

Invalid, non-finite or otherwise unusable positions must not leave stale spatial membership behind. Visibility infrastructure should fail open when it lacks trustworthy location data.

## 10. Current suppression status

This distinction is critical:

**interest-management state tracking exists, but the default enabled policy is currently `PassthroughInterestPolicy`.**

That policy returns `true` for player observation. Therefore enabling interest management today establishes ownership/control and maintains spatial/visibility state, but does **not yet suppress player movement updates**.

This is deliberate. Packet suppression remains disabled until the runtime has fully verified enter transitions, leave transitions, full state on entry, out-of-range semantics, teleport/respawn handling, slot reuse and bounded forced resynchronization.

The project prefers temporarily sending too much state over permanently hiding required state from a client.

## 11. Fail-open behavior

Interest management is a performance optimization. It must not become a correctness dependency.

When disabled, `ShouldRelayPlayerMovement(...)` returns true. When state is insufficient or the feature is not fully proven, routing should remain vanilla-like/broad.

A synchronization optimization that can leave a remote player, NPC or projectile frozen forever is not acceptable even if average bandwidth looks excellent.

## 12. Enter/leave semantics

The target visibility model is stateful.

```text
not visible -> visible
    send complete state required to begin observing

visible -> still visible
    send deltas/normal replication

visible -> not visible
    apply verified out-of-range/despawn semantics

not visible -> still not visible
    suppress ordinary deltas
```

Hysteresis and forced resync prevent boundary flicker and long-lived stale state.

## 13. Teleports, respawn and slot reuse

Spatial visibility must react immediately to discontinuous identity/location changes: teleport across many sections, respawn at a new position, disconnect and slot reuse, invalid position becoming valid again, and server-controlled player creation/despawn.

A generation-safe entity identity prevents stale visibility state from being accidentally applied to a new entity that reused an old numeric slot.

## 14. Player movement relay

Movement is one of the first high-frequency candidates for interest-managed routing because unconditional fanout becomes approximately O(players²) at scale.

Current `RuntimeInterestRouter.ShouldRelayPlayerMovement` behaves as follows:

```text
interest disabled -> relay
interest enabled + current passthrough policy -> relay
```

Future suppression should be introduced only together with transition/full-resync tests, not by replacing the predicate with a raw distance comparison.

## 15. NPC/projectile/item visibility

The same broad architecture can apply to other dynamic entities, but each class needs its own semantics.

Questions that must be answered per entity family include first-observation state, verified out-of-range/despawn behavior, death/despawn representation, re-entry without slot reuse, forced resync interval, and which updates remain global for gameplay correctness.

Do not apply player movement policy blindly to NPCs or projectiles.

## 16. Dirty-state replication

The target runtime avoids scanning every entity for every client every tick.

Preferred direction:

```text
authoritative mutation
   -> dirty/revision/event state
   -> one synchronization pass
   -> recipient filtering
   -> encode/fanout
```

The runtime already has revision/replication registries that provide the foundation for this approach.

## 17. Bootstrap versus steady state

Bootstrap and normal gameplay have very different traffic shapes.

Bootstrap is bursty, section-heavy, ordering-sensitive and needs global work budgets plus queue headroom. Steady state is mostly incremental, latency-sensitive and should rely more heavily on dirty/revision tracking and recipient selection.

One queue/budget assumption should not be chosen solely from one of these phases.

## 18. Slow clients

Synchronization ends at a bounded connection queue. A client that cannot drain its outbound data cannot be allowed to create unlimited retained frames.

The runtime's slow-client policy therefore closes an overloaded connection rather than blocking authoritative simulation.

Interest management can reduce bandwidth and queue pressure in the future, but queue bounds remain required even when filtering is enabled.

## 19. Observability

Useful synchronization telemetry includes or is expected to include per-connection outbound depth, slow-client disconnects, bootstrap frame count, deferred section work, spatial-index membership changes, invalid-position removals, visibility transitions, suppressed versus relayed counts, forced resync counts and oldest pending synchronization work.

Telemetry must be bounded and should not allocate formatted strings on every high-frequency movement update.

## 20. Evidence

Relevant evidence includes spatial-index tests, visibility/hysteresis tests, interest-control tests, bootstrap frame-budget tests, entity bootstrap frame tests, replication registry tests, slow-client/process tests and live `Vanilla World Load` join/movement probes.

Once actual suppression is enabled, live tests must prove both that updates are absent while hidden and that complete correct state returns when visibility is restored.

## 21. Current limitations

Not finished yet:

- actual player movement suppression under the default policy;
- complete enter/leave outbound wiring;
- full-state-on-entry for all entity types;
- forced resync policy;
- generalized NPC/projectile/item interest routing;
- measurement-derived final radii and queue sizing;
- complete section-cache invalidation/per-client delivery accounting.

The existing spatial tracker is therefore a foundation, not a bandwidth-reduction claim.

## 22. Change checklist

A synchronization change is incomplete unless, where relevant, authoritative state remains separate from replication state, join work is bounded globally, queue growth is bounded, enter/leave/full-resync behavior is tested before suppression is enabled, invalid/unknown position fails open, slot reuse cannot inherit stale visibility, live official-client behavior is checked for protocol-sensitive transitions, and this page plus `docs/ru/synchronization.md` change together.
