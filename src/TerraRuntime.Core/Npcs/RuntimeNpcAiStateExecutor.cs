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

/// <summary>Receives retained physical slots, including inactive state that vanilla AI may still read.</summary>
internal interface INpcAiRetainedSlotSnapshotConsumer
{
    void SetRetainedNpcSlots(ReadOnlySpan<VanillaNpcRetainedSlot> slots);
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
    private readonly INpcAiTauntCommitSink? _taunts;
    private readonly RuntimeProjectileStore? _projectiles;
    private readonly NpcSnapshot[] _snapshotBuffer;
    private readonly VanillaNpcRetainedSlot[] _retainedSlotBuffer;
    private readonly NpcAiSpawnIntent[] _spawnIntentBuffer;
    private readonly NpcAiProjectileIntent[] _projectileIntentBuffer;
    private readonly NpcAiProjectileMutationIntent[] _projectileMutationIntentBuffer;
    private readonly ProjectileSnapshot[] _projectileMutationScratch;

    public RuntimeNpcAiStateExecutor(RuntimeNpcStore npcs, RuntimeProjectileStore? projectiles = null,
        INpcAiHealingCommitSink? healing = null, INpcAiTauntCommitSink? taunts = null)
    {
        ArgumentNullException.ThrowIfNull(npcs);
        _npcs = npcs;
        _healing = healing;
        _taunts = taunts;
        _projectiles = projectiles;
        _snapshotBuffer = new NpcSnapshot[npcs.Capacity];
        _retainedSlotBuffer = new VanillaNpcRetainedSlot[npcs.Capacity];
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
        INpcAiForcedUpdateIntentPlanner? forcedUpdatePlanner =
            NpcAiStateStepperComposition.FindCapability<INpcAiForcedUpdateIntentPlanner>(stepper);
        INpcAiPeerSnapshotConsumer? peerConsumer =
            NpcAiStateStepperComposition.FindCapability<INpcAiPeerSnapshotConsumer>(stepper);
        INpcAiRetainedSlotSnapshotConsumer? retainedSlotConsumer =
            NpcAiStateStepperComposition.FindCapability<INpcAiRetainedSlotSnapshotConsumer>(stepper);

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
            if (retainedSlotConsumer is not null)
            {
                int slotCount = _npcs.CopyRetainedSlots(_retainedSlotBuffer);
                retainedSlotConsumer.SetRetainedNpcSlots(_retainedSlotBuffer.AsSpan(0, slotCount));
            }
            if (!stepper.TryStepState(in npc, out NpcStateUpdate next))
                continue;

            proposed++;
            if (spawnPlanner is not null && spawnPlanner.TryPlanInitialization(in npc, in next,
                _spawnIntentBuffer, out NpcStateUpdate initialization, out int initializationCount))
            {
                bool valid = RuntimeNpcStore.IsValid(in next) &&
                    (uint)initializationCount <= (uint)_spawnIntentBuffer.Length;
                for (int i = 0; valid && i < initializationCount; i++)
                    valid = !_spawnIntentBuffer[i].LinkSourceFollowerSlot && _spawnIntentBuffer[i].LinkSourceLocalAiSlot is null;
                if (!valid || !_npcs.TryGet(npc.Handle, out var initialSource) || initialSource.Revision != npc.Revision ||
                    !_npcs.TryUpdateUnpublished(npc.Handle, in initialization, out NpcSnapshot initialized))
                {
                    rejected++;
                    continue;
                }
                for (int i = 0; i < initializationCount; i++)
                {
                    // Spawn publication can reenter the store. A replacement or intervening revision ends this stage.
                    if (!_npcs.TryGet(initialized.Handle, out var live) || live.Revision != initialized.Revision)
                        break;
                    _npcs.TrySpawnIntent(in _spawnIntentBuffer[i], out _);
                }
                if (!_npcs.TryGet(initialized.Handle, out var currentInitialization) ||
                    currentInitialization.Revision != initialized.Revision)
                    continue;
                npc = initialized;
                if (peerConsumer is not null)
                {
                    int peerCount = _npcs.CopyActive(_snapshotBuffer);
                    peerConsumer.SetNpcPeers(_snapshotBuffer.AsSpan(0, peerCount));
                }
                if (retainedSlotConsumer is not null)
                {
                    int slotCount = _npcs.CopyRetainedSlots(_retainedSlotBuffer);
                    retainedSlotConsumer.SetRetainedNpcSlots(_retainedSlotBuffer.AsSpan(0, slotCount));
                }
                if (!stepper.TryStepState(in npc, out next) ||
                    !_npcs.TryGet(npc.Handle, out var continuedSource) || continuedSource.Revision != npc.Revision)
                {
                    rejected++;
                    continue;
                }
            }
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

            // Planning can reenter the store through extensions. The proposal belongs to this exact revision,
            // not merely the same generation; a newer owned update must not be overwritten or consume effects.
            if (!_npcs.TryGet(npc.Handle, out var currentSource) || currentSource.Revision != npc.Revision)
            {
                rejected++;
                continue;
            }
            bool deactivate = postCommitEffect?.DeactivatesAfterStep(in npc, in next) ?? false;
            bool deferPublication = postCommitEffect?.DefersStatePublication(in npc, in next) ?? false;
            bool forceUpdate = forcedUpdatePlanner?.RequiresForcedUpdate(in npc, in next) ?? false;
            if (!_npcs.TryGet(npc.Handle, out currentSource) || currentSource.Revision != npc.Revision)
            {
                rejected++;
                continue;
            }
            bool updated = deactivate || deferPublication
                ? _npcs.TryUpdateUnpublished(npc.Handle, in next, out NpcSnapshot committed)
                : _npcs.TryUpdate(npc.Handle, in next, out committed, forceSync: forceUpdate);
            if (updated)
            {
                applied++;
                if (deferPublication)
                {
                    var completed = postCommitEffect!.CompleteCommittedState(in npc, in committed, this);
                    if (completed.Handle != committed.Handle || !_npcs.TryGet(completed.Handle, out var finalized) ||
                        finalized.Revision != completed.Revision || !_npcs.TryPublishUpdate(in finalized))
                        continue;
                    committed = finalized;
                    if (!_npcs.TryGet(committed.Handle, out var published) || published.Revision != committed.Revision)
                        continue;
                }
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

    bool INpcAiCommittedNpcMutationSink.TryUpdateAi(in NpcSnapshot expected, NpcAiState ai, out NpcSnapshot committed)
    {
        committed = default;
        if (!_npcs.TryGet(expected.Handle, out var current) || current.Revision != expected.Revision) return false;
        var update = new NpcStateUpdate(current.Type, current.NetId, current.PositionX, current.PositionY,
            current.VelocityX, current.VelocityY, current.Target, ai, current.Simulation);
        return _npcs.TryUpdateUnpublished(current.Handle, in update, out committed);
    }

    bool INpcAiCommittedNpcMutationSink.TrySpawnProjectile(in NpcSnapshot source,
        in NpcAiProjectileIntent intent, out ProjectileSnapshot spawned)
    {
        spawned = default;
        return _projectiles is not null && _npcs.TryGet(source.Handle, out var current) &&
            current.Revision == source.Revision &&
            RuntimeNpcProjectileIntentApplier.TryApply(_projectiles, source.Handle, in intent, out spawned);
    }

    bool INpcAiCommittedNpcMutationSink.TryAnnounceSkeletronTaunt(in NpcSnapshot source, int variant)
    {
        if (_taunts is null || variant is < 2 or > 5 ||
            source.TypeIdentity != TerraRuntime.Contracts.Gameplay.VanillaNpcIds.SkeletronHead ||
            !_npcs.TryGet(source.Handle, out var current) || current.Revision != source.Revision) return false;
        _taunts.SkeletronTaunt(in current, variant);
        return true;
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

    bool INpcAiCommittedNpcMutationSink.TryDespawn(NpcHandle npc) =>
        _npcs.TryDespawn(npc);

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

    bool INpcAiCommittedNpcMutationSink.TrySpawn(in NpcSnapshot source,
        in NpcAiSpawnIntent intent, out NpcSnapshot spawned) =>
        _npcs.TrySpawnIntent(in source, in intent, out spawned);

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
