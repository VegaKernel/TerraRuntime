using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// State-only NPC AI step. World side effects such as spawns, projectiles, transforms and tile actions
/// intentionally live outside this narrow primitive so they can be modeled explicitly rather than hidden
/// behind mutation of global state.
/// </summary>
public interface INpcAiStateStepper
{
    bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next);
}

/// <summary>Receives the immutable active-NPC pre-pass used by one authoritative AI tick.</summary>
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
/// Runs allocation-stable NPC AI state transitions against a pre-pass snapshot of the live NPC table.
/// The executor and store are authoritative-thread components. A proposed transition is committed only
/// if the exact generation captured at the start of the pass is still current, so reentrant lifecycle
/// changes cannot let stale AI work mutate a replacement NPC in the same slot. Optional NPC and projectile
/// spawn intents are planned speculatively into executor-owned bounded scratch storage and are applied in order
/// only after that source-state commit succeeds; spawned entities therefore cannot enter the same pre-pass or escape
/// from a rejected/stale transition. Irreversible effects that require exact source ordering use a post-commit
/// mutation surface and therefore cannot consume RNG or mutate the world for a rejected source generation.
/// </summary>
public sealed class RuntimeNpcAiStateExecutor : INpcAiCommittedNpcMutationSink
{
    private const int MaximumProjectileIntentsPerNpcStep = VanillaNpcBehaviorContext.MaximumPlayerCandidates;

    private readonly RuntimeNpcStore _npcs;
    private readonly RuntimeProjectileStore? _projectiles;
    private readonly NpcSnapshot[] _snapshotBuffer;
    private readonly NpcAiSpawnIntent[] _spawnIntentBuffer;
    private readonly NpcAiProjectileIntent[] _projectileIntentBuffer;

    public RuntimeNpcAiStateExecutor(RuntimeNpcStore npcs, RuntimeProjectileStore? projectiles = null)
    {
        ArgumentNullException.ThrowIfNull(npcs);
        _npcs = npcs;
        _projectiles = projectiles;
        _snapshotBuffer = new NpcSnapshot[npcs.Capacity];
        _spawnIntentBuffer = new NpcAiSpawnIntent[npcs.Capacity];
        _projectileIntentBuffer = new NpcAiProjectileIntent[MaximumProjectileIntentsPerNpcStep];
    }

    public NpcAiStateTickSummary Tick(INpcAiStateStepper stepper) =>
        Tick(stepper, commitSink: null);

    public NpcAiStateTickSummary Tick(
        INpcAiStateStepper stepper,
        INpcAiStateCommitSink? commitSink)
    {
        ArgumentNullException.ThrowIfNull(stepper);

        int examined = _npcs.CopyActive(_snapshotBuffer);
        int proposed = 0;
        int applied = 0;
        int rejected = 0;
        INpcAiSpawnIntentPlanner? spawnPlanner =
            NpcAiStateStepperComposition.FindCapability<INpcAiSpawnIntentPlanner>(stepper);
        INpcAiProjectileIntentPlanner? projectilePlanner = _projectiles is null
            ? null
            : NpcAiStateStepperComposition.FindCapability<INpcAiProjectileIntentPlanner>(stepper);
        INpcAiStatePostCommitObserver? postCommitObserver =
            NpcAiStateStepperComposition.FindCapability<INpcAiStatePostCommitObserver>(stepper);
        INpcAiStatePostCommitEffect? postCommitEffect =
            NpcAiStateStepperComposition.FindCapability<INpcAiStatePostCommitEffect>(stepper);
        INpcAiPeerSnapshotConsumer? peerConsumer =
            NpcAiStateStepperComposition.FindCapability<INpcAiPeerSnapshotConsumer>(stepper);
        peerConsumer?.SetNpcPeers(_snapshotBuffer.AsSpan(0, examined));

        for (int index = 0; index < examined; index++)
        {
            NpcSnapshot npc = _snapshotBuffer[index];
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

            if (_npcs.TryUpdate(npc.Handle, in next, out NpcSnapshot committed))
            {
                applied++;
                postCommitObserver?.NpcAiStateCommitted(in npc, in committed);
                postCommitEffect?.ApplyCommittedEffect(in npc, in committed, this);
                commitSink?.NpcAiStateCommitted(in committed);

                if (_projectiles is not null)
                {
                    for (int projectileIndex = 0; projectileIndex < projectileCount; projectileIndex++)
                    {
                        NpcAiProjectileIntent intent = _projectileIntentBuffer[projectileIndex];
                        RuntimeNpcProjectileIntentApplier.TryApply(_projectiles, committed.Handle, in intent, out _);
                    }
                }

                for (int spawnIndex = 0; spawnIndex < spawnCount; spawnIndex++)
                {
                    NpcAiSpawnIntent intent = _spawnIntentBuffer[spawnIndex];
                    if (!_npcs.TrySpawnIntent(in intent, out NpcSnapshot spawned) ||
                        !intent.LinkSourceFollowerSlot)
                    {
                        continue;
                    }

                    NpcAiState linkedAi = new(
                        spawned.Handle.Slot,
                        committed.Ai.Ai1,
                        committed.Ai.Ai2,
                        committed.Ai.Ai3);
                    var linkedUpdate = new NpcStateUpdate(
                        committed.Type,
                        committed.NetId,
                        committed.PositionX,
                        committed.PositionY,
                        committed.VelocityX,
                        committed.VelocityY,
                        committed.Target,
                        linkedAi,
                        committed.Simulation);
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
            }
            else
            {
                rejected++;
            }
        }

        return new NpcAiStateTickSummary(examined, proposed, applied, rejected);
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
