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
/// changes cannot let stale AI work mutate a replacement NPC in the same slot.
/// </summary>
public sealed class RuntimeNpcAiStateExecutor
{
    private readonly RuntimeNpcStore _npcs;
    private readonly NpcSnapshot[] _snapshotBuffer;

    public RuntimeNpcAiStateExecutor(RuntimeNpcStore npcs)
    {
        ArgumentNullException.ThrowIfNull(npcs);
        _npcs = npcs;
        _snapshotBuffer = new NpcSnapshot[npcs.Capacity];
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

        for (int index = 0; index < examined; index++)
        {
            NpcSnapshot npc = _snapshotBuffer[index];
            if (!stepper.TryStepState(in npc, out NpcStateUpdate next))
                continue;

            proposed++;
            if (_npcs.TryUpdate(npc.Handle, in next, out NpcSnapshot committed))
            {
                applied++;
                commitSink?.NpcAiStateCommitted(in committed);
            }
            else
            {
                rejected++;
            }
        }

        return new NpcAiStateTickSummary(examined, proposed, applied, rejected);
    }
}
