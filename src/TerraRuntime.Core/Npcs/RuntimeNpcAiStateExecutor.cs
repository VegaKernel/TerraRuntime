using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

/// <summary>
/// State-only NPC AI step. World side effects such as spawns, projectiles, transforms and tile actions
/// intentionally live outside this narrow primitive so they can be modeled explicitly rather than hidden
/// behind mutation of global state.
/// </summary>
public interface INpcAiStateStepper
{
    bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next);
}

/// <summary>Receives an immutable active-NPC view immediately before each authoritative AI step.</summary>
public interface INpcAiPeerSnapshotConsumer
{
    void SetNpcPeers(ReadOnlySpan<NpcSnapshot> peers);
}

/// <summary>
/// Bounded accounting for one state-transition pass over the live NPC table.
/// </summary>
public readonly record struct NpcAiStateTickSummary(
    int Examined,
    int Proposed,
    int Applied,
    int Rejected);

/// <summary>
/// Runs allocation-stable NPC AI state transitions in ascending live slot order.
/// Each slot is read when its turn arrives, so earlier peer mutations and newly spawned higher slots
/// participate in the same tick (TerrariaServer 1.4.5.8 Main.Update / NPC.UpdateNPC).
/// A proposal still commits only against the generation captured immediately before that step:
/// reentrant replacement during planning cannot inherit stale state or effects.
/// Optional intents are planned into bounded scratch storage and applied only after a successful commit.
/// </summary>
public sealed class RuntimeNpcAiStateExecutor : INpcAiCommittedNpcMutationSink
{
    private const int MaximumProjectileIntentsPerNpcStep = VanillaNpcBehaviorContext.MaximumPlayerCandidates;

    private readonly RuntimeNpcStore _npcs;
    private readonly INpcAiHealingCommitSink? _healing;
    private readonly RuntimeProjectileStore? _projectiles;
    private readonly NpcSnapshot[] _snapshotBuffer;
    private readonly NpcAiSpawnIntent[] _spawnIntentBuffer;
    private readonly NpcAiProjectileIntent[] _projectileIntentBuffer;
    private readonly NpcAiProjectileMutationIntent[] _projectileMutationIntentBuffer;
    private readonly ProjectileSnapshot[] _projectileMutationScratch;

    public RuntimeNpcAiStateExecutor(RuntimeNpcStore npcs, RuntimeProjectileStore? projectiles = null, INpcAiHealingCommitSink? healing = null)
    {
        ArgumentNullException.ThrowIfNull(npcs);
        _npcs = npcs;
        _healing = healing;
        _projectiles = projectiles;
        _snapshotBuffer = new NpcSnapshot[npcs.Capacity];
        _spawnIntentBuffer = new NpcAiSpawnIntent[npcs.Capacity];
        _projectileIntentBuffer = new NpcAiProjectileIntent[MaximumProjectileIntentsPerNpcStep];
        _projectileMutationIntentBuffer = new NpcAiProjectileMutationIntent[MaximumProjectileIntentsPerNpcStep];
        _projectileMutationScratch = projectiles is null ? [] : new ProjectileSnapshot[projectiles.Capacity];
    }

    public NpcAiStateTickSummary Tick(INpcAiStateStepper stepper) =>
        Tick(stepper, commitSink: null);

    public NpcAiStateTickSummary Tick(
        INpcAiStateStepper stepper,
        INpcAiStateCommitSink? commitSink)
    {
        ArgumentNullException.ThrowIfNull(stepper);

        int examined = 0;
        int proposed = 0;
        int applied = 0;
        int rejected = 0;
        INpcAiSpawnIntentPlanner? spawnPlanner =
            NpcAiStateStepperComposition.FindCapability<INpcAiSpawnIntentPlanner>(stepper);
        INpcAiProjectileIntentPlanner? projectilePlanner = _projectiles is null
            ? null
            : NpcAiStateStepperComposition.FindCapability<INpcAiProjectileIntentPlanner>(stepper);
        INpcAiProjectileMutationIntentPlanner? projectileMutationPlanner = _projectiles is null
            ? null
            : NpcAiStateStepperComposition.FindCapability<INpcAiProjectileMutationIntentPlanner>(stepper);
        INpcAiStatePostCommitObserver? postCommitObserver =
            NpcAiStateStepperComposition.FindCapability<INpcAiStatePostCommitObserver>(stepper);
        INpcAiStatePostCommitEffect? postCommitEffect =
            NpcAiStateStepperComposition.FindCapability<INpcAiStatePostCommitEffect>(stepper);
        INpcAiPeerSnapshotConsumer? peerConsumer =
            NpcAiStateStepperComposition.FindCapability<INpcAiPeerSnapshotConsumer>(stepper);

        for (int slot = 0; slot < _npcs.Capacity; slot++)
        {
            if (!_npcs.TryGetActive(checked((byte)slot), out NpcSnapshot npc))
                continue;
            examined++;
            if (peerConsumer is not null)
            {
                // A bounded full view keeps existing peer consumers coherent with earlier slot effects.
                // At most Capacity squared snapshots are copied (Capacity <= 256). If larger NPC tables
                // are admitted, replace this copy boundary with an authoritative read-only live lookup.
                int peerCount = _npcs.CopyActive(_snapshotBuffer);
                peerConsumer.SetNpcPeers(_snapshotBuffer.AsSpan(0, peerCount));
            }
            if (!stepper.TryStepState(in npc, out NpcStateUpdate next))
                continue;

            proposed++;
            int spawnCount = spawnPlanner?.PlanNpcSpawns(
                in npc,
                in next,
                _spawnIntentBuffer) ?? 0;
            if ((uint)spawnCount > (uint)_spawnIntentBuffer.Length)
            {
                rejected++;
                continue;
            }

            int projectileCount = projectilePlanner?.PlanProjectileSpawns(
                in npc,
                in next,
                _projectileIntentBuffer) ?? 0;
            if ((uint)projectileCount > (uint)_projectileIntentBuffer.Length)
            {
                rejected++;
                continue;
            }

            int projectileMutationCount = projectileMutationPlanner?.PlanProjectileMutations(
                in npc,
                in next,
                _projectileMutationIntentBuffer) ?? 0;
            if ((uint)projectileMutationCount > (uint)_projectileMutationIntentBuffer.Length)
            {
                rejected++;
                continue;
            }

            bool deactivate = postCommitEffect?.DeactivatesAfterStep(in npc, in next) ?? false;
            bool updated = deactivate
                ? _npcs.TryUpdateUnpublished(npc.Handle, in next, out NpcSnapshot committed)
                : _npcs.TryUpdate(npc.Handle, in next, out committed);
            if (updated)
            {
                applied++;
                postCommitObserver?.NpcAiStateCommitted(in npc, in committed);
                if (!_npcs.TryGet(committed.Handle, out NpcSnapshot observed) || observed.Revision != committed.Revision)
                    continue;
                postCommitEffect?.ApplyCommittedEffect(in npc, in committed, this);
                commitSink?.NpcAiStateCommitted(in committed);

                if (_projectiles is not null)
                {
                    for (int mutationIndex = 0; mutationIndex < projectileMutationCount; mutationIndex++)
                    {
                        NpcAiProjectileMutationIntent mutation = _projectileMutationIntentBuffer[mutationIndex];
                        RuntimeNpcProjectileMutationIntentApplier.ApplyMatching(
                            _projectiles,
                            committed.Handle,
                            in mutation,
                            _projectileMutationScratch);
                    }

                    for (int projectileIndex = 0; projectileIndex < projectileCount; projectileIndex++)
                    {
                        NpcAiProjectileIntent intent = _projectileIntentBuffer[projectileIndex];
                        RuntimeNpcProjectileIntentApplier.TryApply(_projectiles, committed.Handle, in intent, out _);
                    }
                }

                for (int spawnIndex = 0; spawnIndex < spawnCount; spawnIndex++)
                {
                    NpcAiSpawnIntent intent = _spawnIntentBuffer[spawnIndex];
                    if (intent.LinkSourceLocalAiSlot is > 3 ||
                        !_npcs.TrySpawnIntent(in intent, out NpcSnapshot spawned) ||
                        (!intent.LinkSourceFollowerSlot && intent.LinkSourceLocalAiSlot is null))
                    {
                        continue;
                    }

                    NpcAiState linkedAi = intent.LinkSourceFollowerSlot ? new(
                        spawned.Handle.Slot,
                        committed.Ai.Ai1,
                        committed.Ai.Ai2,
                        committed.Ai.Ai3) : committed.Ai;
                    NpcAiState local = committed.Simulation.LocalAi;
                    local = intent.LinkSourceLocalAiSlot switch
                    {
                        0 => local with { Ai0 = spawned.Handle.Slot },
                        1 => local with { Ai1 = spawned.Handle.Slot },
                        2 => local with { Ai2 = spawned.Handle.Slot },
                        3 => local with { Ai3 = spawned.Handle.Slot },
                        _ => local
                    };
                    var linkedUpdate = new NpcStateUpdate(
                        committed.Type,
                        committed.NetId,
                        committed.PositionX,
                        committed.PositionY,
                        committed.VelocityX,
                        committed.VelocityY,
                        committed.Target,
                        linkedAi,
                        committed.Simulation with { LocalAi = local });
                    if (_npcs.TryUpdate(committed.Handle, in linkedUpdate, out NpcSnapshot linked))
                    {
                        committed = linked;
                        commitSink?.NpcAiStateCommitted(in linked);
                    }
                    else
                    {
                        _npcs.TryDespawn(spawned.Handle);
                    }
                }
                if (_npcs.TryGet(committed.Handle, out NpcSnapshot current) && current.Revision == committed.Revision)
                    postCommitEffect?.ApplyCommittedEffectAfterSpawns(in npc, in committed, this);
                if (deactivate && _npcs.TryGet(committed.Handle, out current) && current.Revision == committed.Revision)
                    _npcs.TryDespawn(committed.Handle);
            }
            else
            {
                rejected++;
            }
        }

        return new NpcAiStateTickSummary(examined, proposed, applied, rejected);
    }

    int INpcAiCommittedNpcMutationSink.TryHeal(NpcHandle npc, int maximumAmount)
    {
        if (maximumAmount <= 0 || !_npcs.TryGet(npc, out NpcSnapshot current)) return 0;
        int amount = Math.Min(maximumAmount, current.Simulation.LifeMax - current.Simulation.Life);
        if (amount <= 0) return 0;
        var update = new NpcStateUpdate(current.Type, current.NetId, current.PositionX, current.PositionY,
            current.VelocityX, current.VelocityY, current.Target, current.Ai,
            current.Simulation with { Life = current.Simulation.Life + amount });
        if (!_npcs.TryUpdateUnpublished(npc, in update, out NpcSnapshot healed)) return 0;
        _healing?.NpcHealed(in healed, amount);
        return amount;
    }

    bool INpcAiCommittedNpcMutationSink.TryGetActive(byte slot, out NpcSnapshot npc) =>
        _npcs.TryGetActive(slot, out npc);

    bool INpcAiCommittedNpcMutationSink.TryLinkFollower(NpcHandle npc, byte followerSlot)
    {
        if (followerSlot > VanillaNpcSpawnRules.PhysicalSlotCount || !_npcs.TryGet(npc, out var current))
            return false;
        var update = new NpcStateUpdate(current.Type, current.NetId,
            current.PositionX, current.PositionY, current.VelocityX, current.VelocityY,
            current.Target, current.Ai with { Ai0 = followerSlot }, current.Simulation);
        return _npcs.TryUpdate(npc, in update, out _);
    }

    bool INpcAiCommittedNpcMutationSink.TryTranslate(
        NpcHandle npc, float deltaX, float deltaY, out NpcSnapshot committed)
    {
        committed = default;
        if (!float.IsFinite(deltaX) || !float.IsFinite(deltaY) || !_npcs.TryGet(npc, out NpcSnapshot current))
            return false;
        var update = new NpcStateUpdate(current.Type, current.NetId,
            current.PositionX + deltaX, current.PositionY + deltaY,
            current.VelocityX, current.VelocityY, current.Target, current.Ai, current.Simulation);
        return _npcs.TryUpdate(npc, in update, out committed, forceSync: true);
    }

    bool INpcAiCommittedNpcMutationSink.TrySpawn(
        in NpcAiSpawnIntent intent,
        out NpcSnapshot spawned) =>
        _npcs.TrySpawnIntent(in intent, out spawned);

    bool INpcAiCommittedNpcMutationSink.TryUpdateVelocity(
        NpcHandle npc,
        float velocityX,
        float velocityY,
        out NpcSnapshot committed)
    {
        committed = default;
        if (!float.IsFinite(velocityX) ||
            !float.IsFinite(velocityY) ||
            !_npcs.TryGet(npc, out NpcSnapshot current))
        {
            return false;
        }

        var update = new NpcStateUpdate(
            current.Type,
            current.NetId,
            current.PositionX,
            current.PositionY,
            velocityX,
            velocityY,
            current.Target,
            current.Ai,
            current.Simulation);
        return _npcs.TryUpdate(current.Handle, in update, out committed);
    }
}
