using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Application.Operations;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcOperationsTelemetryTests
{
    [Fact]
    public void Telemetry_projects_committed_npc_lifecycle_without_reading_mutable_store()
    {
        var telemetry = new RuntimeNpcOperationsTelemetry();
        var store = new RuntimeNpcStore(capacity: 2, commitSink: telemetry);
        NpcStateUpdate initial = CreateUpdate(positionX: 160f, velocityX: 0f);

        Assert.True(store.TrySpawn(1, in initial, out NpcSnapshot spawned));
        RuntimeNpcsSnapshot afterSpawn = telemetry.CaptureSnapshot();
        RuntimeNpcSnapshot first = Assert.Single(afterSpawn.Npcs.ToArray());
        Assert.Equal((byte)1, first.Slot);
        Assert.Equal(spawned.Handle.Generation.Value, first.Generation);
        Assert.Equal(1UL, first.Revision);
        Assert.Equal(1, afterSpawn.CommittedSpawns);
        Assert.Equal(0, afterSpawn.CommittedUpdates);
        Assert.Equal(0, afterSpawn.CommittedDespawns);

        NpcStateUpdate moved = initial with
        {
            PositionX = 192f,
            VelocityX = 1.5f,
            Ai = new NpcAiState(1f, 2f, 3f, 4f),
            Simulation = NpcSimulationState.Initial with
            {
                DirectionX = 1,
                CollideY = true,
                Wet = true
            }
        };
        Assert.True(store.TryUpdate(spawned.Handle, in moved, out NpcSnapshot updated));

        RuntimeNpcsSnapshot afterUpdate = telemetry.CaptureSnapshot();
        RuntimeNpcSnapshot current = Assert.Single(afterUpdate.Npcs.ToArray());
        Assert.Equal(updated.Revision.Value, current.Revision);
        Assert.Equal(192f, current.PositionX);
        Assert.Equal(1.5f, current.VelocityX);
        Assert.Equal(1f, current.Ai0);
        Assert.Equal(4f, current.Ai3);
        Assert.Equal(1, current.DirectionX);
        Assert.True(current.CollideY);
        Assert.True(current.Wet);
        Assert.Equal(1, afterUpdate.CommittedUpdates);

        Assert.True(store.TryDespawn(spawned.Handle));
        RuntimeNpcsSnapshot afterDespawn = telemetry.CaptureSnapshot();
        Assert.Empty(afterDespawn.Npcs.ToArray());
        Assert.Equal(1, afterDespawn.CommittedDespawns);
    }

    [Fact]
    public void Stale_despawn_event_cannot_remove_reused_slot_generation()
    {
        var telemetry = new RuntimeNpcOperationsTelemetry();
        var store = new RuntimeNpcStore(capacity: 1, commitSink: telemetry);
        NpcStateUpdate update = CreateUpdate(positionX: 32f, velocityX: 0f);

        Assert.True(store.TrySpawn(0, in update, out NpcSnapshot first));
        Assert.True(store.TryDespawn(first.Handle));
        Assert.True(store.TrySpawn(0, in update, out NpcSnapshot replacement));
        Assert.NotEqual(first.Handle.Generation, replacement.Handle.Generation);

        telemetry.NpcStateCommitted(NpcStateCommitKind.Despawn, in first);

        RuntimeNpcSnapshot active = Assert.Single(telemetry.CaptureSnapshot().Npcs.ToArray());
        Assert.Equal(replacement.Handle.Generation.Value, active.Generation);
    }

    private static NpcStateUpdate CreateUpdate(float positionX, float velocityX) =>
        new(
            Type: 1,
            NetId: 1,
            PositionX: positionX,
            PositionY: 64f,
            VelocityX: velocityX,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial);
}
