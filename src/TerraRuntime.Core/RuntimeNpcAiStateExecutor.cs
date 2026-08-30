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
/// changes cannot let stale AI work mutate a replacement NPC in the same slot. Optional NPC spawn intents
/// are planned speculatively into executor-owned bounded scratch storage and are applied in order only after
/// that source-state commit succeeds; newly spawned NPCs therefore cannot enter the same pre-pass or escape
/// from a rejected/stale transition. Decorator chains expose their inner stepper through
/// INpcAiStateStepperWrapper so optional planners and post-commit observers remain discoverable under production
/// composition layers.
/// </summary>
public sealed class RuntimeNpcAiStateExecutor
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
                commitSink?.NpcAiStateCommitted(in committed);

                for (int spawnIndex = 0; spawnIndex < spawnCount; spawnIndex++)
                {
                    NpcAiSpawnIntent intent = _spawnIntentBuffer[spawnIndex];
                    RuntimeNpcSpawnIntentApplier.TryApply(_npcs, in intent, out _);
                }
            }
            else
            {
                rejected++;
            }
        }

        return new NpcAiStateTickSummary(examined, proposed, applied, rejected);
    }
}
