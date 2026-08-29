using System.Reflection;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcActorControlRegistryConcurrencyTests
{
    [Fact]
    public async Task CommitPending_does_not_wait_for_the_control_plane_monitor()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var store = new RuntimeNpcStore(capacity: 1);
        NpcStateUpdate initial = CreateNpc(VanillaNpcIds.Zombie.Value);
        Assert.True(store.TrySpawn(0, in initial, out NpcSnapshot npc));

        var controls = new RuntimeNpcActorControlRegistry(store);
        Assert.Equal(
            NpcActorControlAcquireResult.Acquired,
            controls.TryAcquire(npc.Handle, new ActorControllerId("test:monitor-free"), out _));

        FieldInfo gateField = typeof(RuntimeNpcActorControlRegistry).GetField(
            "_gate",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new Xunit.Sdk.XunitException("Control-plane gate field was not found.");
        object gate = gateField.GetValue(controls)
            ?? throw new Xunit.Sdk.XunitException("Control-plane gate was null.");

        using var gateHeld = new ManualResetEventSlim(false);
        using var releaseGate = new ManualResetEventSlim(false);
        var holder = new Thread(() =>
        {
            lock (gate)
            {
                gateHeld.Set();
                releaseGate.Wait();
            }
        })
        {
            IsBackground = true,
            Name = "npc-actor-control-gate-holder"
        };

        holder.Start();
        gateHeld.Wait(cancellationToken);

        try
        {
            Task<bool> commit = Task.Run(controls.CommitPending, cancellationToken);
            Task completed = await Task.WhenAny(
                commit,
                Task.Delay(TimeSpan.FromSeconds(2), cancellationToken));

            Assert.Same(commit, completed);
            Assert.True(await commit);
            Assert.True(controls.Snapshot.TryGet(npc.Handle, out _));
        }
        finally
        {
            releaseGate.Set();
            Assert.True(holder.Join(TimeSpan.FromSeconds(2)));
        }
    }

    private static NpcStateUpdate CreateNpc(int type) =>
        new(
            Type: type,
            NetId: checked((short)type),
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial);
}
