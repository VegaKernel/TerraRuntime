using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

/// <summary>
/// Observes only NPC AI state transitions that were successfully committed to the generation-safe
/// authoritative store. The observer is called synchronously on the simulation thread after TryUpdate
/// succeeds, so it can publish immutable snapshots without re-reading or rescanning the NPC table.
/// </summary>
public interface INpcAiStateCommitSink
{
    void NpcAiStateCommitted(in NpcSnapshot snapshot);
}

/// <summary>
/// Optional capability owned by an AI composition layer that must react to its own proposed transition only after
/// the exact NPC generation has committed. Unlike the external commit sink this receives both the pre-step snapshot
/// and committed revision, allowing gameplay side effects to prove they correspond to the accepted transition.
/// </summary>
public interface INpcAiStatePostCommitObserver
{
    void NpcAiStateCommitted(in NpcSnapshot before, in NpcSnapshot committed);
}

/// <summary>
/// Optional source-scoped packet-23 intent. The ordinary replication cadence remains the default; an admitted
/// AI transition can request an immediate update only where TerrariaServer sets <c>NPC.netUpdate</c> itself.
/// </summary>
public interface INpcAiForcedUpdateIntentPlanner
{
    bool RequiresForcedUpdate(in NpcSnapshot before, in NpcStateUpdate proposed);

    /// <summary>
    /// Evaluates a source-scoped immediate packet-23 request after projectile planning. Implementations use this
    /// only where the matching Terraria AI sets <c>NPC.netUpdate</c> as part of an accepted projectile transition.
    /// </summary>
    bool RequiresForcedUpdateAfterPlanning(
        in NpcSnapshot before,
        in NpcStateUpdate proposed,
        ReadOnlySpan<NpcAiProjectileIntent> plannedProjectiles) => RequiresForcedUpdate(in before, in proposed);
}

/// <summary>
/// Narrow authoritative mutation surface for irreversible NPC side effects that must occur only after the source
/// NPC generation has committed. It deliberately exposes spawn and exact-generation update only, rather than the
/// mutable NPC store, so side-effect implementations cannot bypass lifecycle validation accidentally.
/// </summary>
public interface INpcAiCommittedNpcMutationSink
{
    /// <summary>Updates AI only at the expected revision, withholding publication until finalization completes.</summary>
    bool TryUpdateAi(in NpcSnapshot expected, NpcAiState ai, out NpcSnapshot committed);

    /// <summary>
    /// Applies one complete source state mutation at the expected revision while publication is deferred. The default
    /// keeps narrow test sinks source-safe; runtime-owned sinks opt in when an accepted AI tail also changes motion.
    /// </summary>
    bool TryUpdateState(in NpcSnapshot expected, in NpcStateUpdate update, out NpcSnapshot committed)
    {
        committed = default;
        return false;
    }

    int TryHeal(NpcHandle npc, int maximumAmount);

    bool TrySpawn(in NpcAiSpawnIntent intent, out NpcSnapshot spawned);

    /// <summary>Checks source ownership both before creation callbacks and immediately before allocation.</summary>
    bool TrySpawn(in NpcSnapshot source, in NpcAiSpawnIntent intent, out NpcSnapshot spawned);

    /// <summary>Allocates a projectile only while its source still owns the expected generation and revision.</summary>
    bool TrySpawnProjectile(in NpcSnapshot source, in NpcAiProjectileIntent intent, out ProjectileSnapshot spawned);

    /// <summary>Publishes one source-ordered Skeletron taunt only at the expected source revision.</summary>
    bool TryAnnounceSkeletronTaunt(in NpcSnapshot source, int variant);

    bool TryUpdateVelocity(NpcHandle npc, float velocityX, float velocityY, out NpcSnapshot committed);

    bool TryGetActive(byte slot, out NpcSnapshot npc);

    /// <summary>Deactivates one exact live generation through the authoritative store lifecycle.</summary>
    bool TryDespawn(NpcHandle npc);

    bool TryTranslate(NpcHandle npc, float deltaX, float deltaY, out NpcSnapshot committed);

    /// <summary>Links a committed chain member to a physical follower slot, including vanilla's full-table sentinel 200.</summary>
    bool TryLinkFollower(NpcHandle npc, byte followerSlot);
}

/// <summary>
/// Optional post-commit gameplay effect. This runs after the exact source state update succeeds and may perform
/// source-ordered irreversible mutations through <see cref="INpcAiCommittedNpcMutationSink"/>. It is distinct from
/// speculative spawn planning so RNG and world effects do not escape a rejected/stale source transition.
/// </summary>
public interface INpcAiStatePostCommitEffect
{
    /// <summary>
    /// Requests source-ordered deactivation after this step's committed effects and allocations. The executor
    /// withholds the intermediate update notification and publishes the final despawn before the next NPC slot.
    /// </summary>
    bool DeactivatesAfterStep(in NpcSnapshot before, in NpcStateUpdate proposed) => false;

    /// <summary>Defers notification until accepted random-dependent AI fields have been resolved.</summary>
    bool DefersStatePublication(in NpcSnapshot before, in NpcStateUpdate proposed) => false;

    /// <summary>Completes the unpublished accepted state; never runs for rejected speculative proposals.</summary>
    NpcSnapshot CompleteCommittedState(in NpcSnapshot before, in NpcSnapshot committed,
        INpcAiCommittedNpcMutationSink mutations) => committed;

    void ApplyCommittedEffect(
        in NpcSnapshot before,
        in NpcSnapshot committed,
        INpcAiCommittedNpcMutationSink mutations);

    /// <summary>Runs after planned NPC allocations and source-slot links have committed.</summary>
    void ApplyCommittedEffectAfterSpawns(
        in NpcSnapshot before,
        in NpcSnapshot committed,
        INpcAiCommittedNpcMutationSink mutations) { }
}

/// <summary>Publishes presentation after a generation-safe AI healing mutation, without manufacturing damage events.</summary>
public interface INpcAiHealingCommitSink
{
    void NpcHealed(in NpcSnapshot npc, int amount);
}

/// <summary>Presentation of AI_011's accepted localized taunt; variant is in the original range 2 through 5.</summary>
public interface INpcAiTauntCommitSink
{
    void SkeletronTaunt(in NpcSnapshot source, int variant);
}
