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

/// <summary>Bounded accounting for one state-transition pass over the live NPC table.</summary>
public readonly record struct NpcAiStateTickSummary(
    int Examined,
    int Proposed,
    int Applied,
    int Rejected);

/// <summary>
/// Runs allocation-stable NPC AI state transitions against a pre-pass snapshot of the live NPC table. Speculative
/// spawn intents are collected before source commit but are applied only after it succeeds. Irreversible effects that
/// require exact source ordering use <see cref="INpcAiStatePostCommitEffect"/> instead; that capability receives only
/// a narrow generation-safe mutation sink after the source update is accepted.
/// </summary>
public sealed class RuntimeNpcAiStateExecutor : INpcAiCommittedNpcMutationSink
{
    private readonly RuntimeNpcStore _npcs;
    private readonly NpcSnapshot[] _snapshotBuffer;
    private readonly NpcAiSpawnIntent[] _spawnIntentBuffer;

    public RuntimeNpcAiStateExecutor(RuntimeNpcStore npcs)
    {
        ArgumentNullException.ThrowIfNull(npcs);
        _npcs = npcs;
        _snapshotBuffer = new NpcSnapshot[npcs.Capacity];
        _spawnIntentBuffer = new NpcAiSpawnIntent[npcs.Capacity];
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

            if (_npcs.TryUpdate(npc.Handle, in next, out NpcSnapshot committed))
            {
                applied++;
                postCommitObserver?.NpcAiStateCommitted(in npc, in committed);
                postCommitEffect?.ApplyCommittedEffect(in npc, in committed, this);
                commitSink?.NpcAiStateCommitted(in committed);

                for (int spawnIndex = 0; spawnIndex < spawnCount; spawnIndex++)
                {
                    NpcAiSpawnIntent intent = _spawnIntentBuffer[spawnIndex];
                    if (!RuntimeNpcSpawnIntentApplier.TryApply(_npcs, in intent, out NpcSnapshot spawned) ||
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
        RuntimeNpcSpawnIntentApplier.TryApply(_npcs, in intent, out spawned);

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
