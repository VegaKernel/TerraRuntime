using System.Reflection;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class OrdinaryMeleeCurrentness1458Tests
{
    [Fact]
    public void Raw_player_zero_remains_current_even_outside_the_target_candidate_span()
    {
        foreach (bool change in new[] { false, true })
        {
            using var f = new Fixture(524);
            f.KillPlayer();
            f.ChangeNpc(target: 255);
            // Exercise the typed targeting boundary separately from NpcAuthority's
            // candidate copy. AI003 can still read player[0] with no eligible targets.
            f.Targeting.SetCandidates([]);
            var before = f.Current();
            var checkpoint = f.Random.Clone();
            Assert.True(f.Targeting.TryStepState(in before, out var proposal));
            Assert.True(f.Npcs.TryUpdateUnpublished(before.Handle, in proposal, out var accepted));
            Assert.True(checkpoint.HasSameState(f.Random));

            Assert.True(f.World.TryCapturePlayerSnapshot(f.Connection.Player, out var playerBefore));
            if (change) f.MovePlayer(playerBefore.PositionX + 16f, playerBefore.PositionY);
            Assert.True(f.World.TryCapturePlayerSnapshot(f.Connection.Player, out var playerAfter));
            Assert.Equal(playerBefore.IsDead, playerAfter.IsDead);
            Assert.Equal(change, playerBefore.Revision != playerAfter.Revision);

            var completed = f.Motion.CompleteCommittedState(in before, in accepted,
                new RuntimeNpcAiStateExecutor(f.Npcs));
            Assert.Equal(!change, completed.Handle.IsAssigned);
            if (change)
            {
                Assert.True(checkpoint.HasSameState(f.Random));
                Assert.Equal(accepted, f.Current());
            }
            else
            {
                Assert.False(checkpoint.HasSameState(f.Random));
                Assert.Equal(completed, f.Current());
            }
        }
    }

    [Fact]
    public void A_changed_world_surface_refuses_the_retained_phase_without_adopting_entropy()
    {
        foreach (bool change in new[] { false, true })
        {
            using var f = new Fixture(524);
            var before = f.Current();
            var checkpoint = f.Random.Clone();
            Assert.True(f.Targeting.TryStepState(in before, out var proposal));
            Assert.True(f.Npcs.TryUpdateUnpublished(before.Handle, in proposal, out var accepted));
            Assert.True(checkpoint.HasSameState(f.Random));
            if (change) f.Targeting.SetWorldBounds(200, 81d, worldHeightTiles: 200);

            var completed = f.Motion.CompleteCommittedState(in before, in accepted,
                new RuntimeNpcAiStateExecutor(f.Npcs));
            Assert.Equal(!change, completed.Handle.IsAssigned);
            if (change)
            {
                Assert.True(checkpoint.HasSameState(f.Random));
                Assert.Equal(accepted, f.Current());
            }
            else
            {
                Assert.False(checkpoint.HasSameState(f.Random));
                Assert.Equal(completed, f.Current());
            }
        }
    }

    [Fact]
    public void A_dead_raw_target_still_supplies_the_source_lunge_geometry()
    {
        using var f = new Fixture(104);
        f.KillPlayer();
        f.ChangeNpc(target: 0, velocity: 1.1f);
        Assert.True(f.World.TryCapturePlayerSnapshot(f.Connection.Player, out var dead));
        Assert.True(dead.IsDead);
        Assert.Equal(1063f, dead.PositionX);
        Assert.Equal(1558f, dead.PositionY);
        var before = f.Current();
        NonresidentBunnyNpcPhase1458.Tick(f.World);
        var after = f.Current();
        // NPC.AI_003_Fighters 1.4.5.8 reads player[target]'s rectangle here
        // even when TargetClosest has no living candidate. Collision follows AI.
        Assert.Equal((ushort)0, after.Target);
        Assert.True(after.Revision.Value > before.Revision.Value);
        // Independent full NPC.UpdateNPC capture: actor (1023,1560), vx=1.1,
        // dead active player0 (1063,1558), jump -4 followed by .3 gravity.
        Assert.Equal(0x4015c290u, BitConverter.SingleToUInt32Bits(after.VelocityX));
        Assert.Equal(0xc06ccccdu, BitConverter.SingleToUInt32Bits(after.VelocityY));
        Assert.Equal(0x4015c290u, BitConverter.SingleToUInt32Bits(after.Simulation.OldVelocityX));
        Assert.Equal(0xc06ccccdu, BitConverter.SingleToUInt32Bits(after.Simulation.OldVelocityY));
        Assert.True(after.PositionY < before.PositionY);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly WorldTileStore Tiles = new(new WorldDimensions(200, 200));
        internal readonly RuntimeNpcStore Npcs = new();
        internal readonly VanillaUnifiedRandom1458 Random = new(1458);
        internal readonly ServerRuntimeState World;
        internal readonly ConnectionHandle Connection;
        internal readonly VanillaNpcTargetingAiStepper Targeting;
        internal readonly VanillaNpcWorldMotionAiStepper Motion;
        private readonly PlayerJoinSession session;
        private readonly NpcHandle handle;

        internal Fixture(int type)
        {
            Assert.True(Tiles.TryAttachWorldSurface(80d));
            for (int x = 0; x < 200; x++)
                Tiles.Set(x, 100, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            World = new(npcs: Npcs, worldTiles: Tiles,
                worldClock: new RuntimeWorldClock(0, false, (VanillaMoonPhase)0, 0, 0),
                naturalSpawnRandom: new SystemVanillaNpcRandom(Random),
                worldItemSpawnRandom: new SystemWorldItemSpawnRandom(Random));
            var slots = new PlayerSlotPool(1);
            Assert.True(slots.TryAcquireConnection(out var lease));
            session = new(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            session.ObserveWorldRequest();
            session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(9081), session.Handle);
            World.Apply(new PlayerSpawnRuntimeCommand(Connection, session,
                new(session.Slot, 64, 100, 0, 0, 0, 0, 0)));
            MovePlayer(1063f, 1558f);
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(type), out var definition));
            Assert.True(definition.TryResolveHitbox(
                NpcSimulationState.Initial with { Scale = definition.Scale }, out var body));
            float y = 1600f - body.Height;
            var creation = new NpcStateUpdate(type, (short)type, 1023f, y, .9f, 0f, 255, default,
                NpcSimulationState.Initial with {
                    DirectionX = 1, DirectionY = 1, SpriteDirection = 1,
                    OldPositionX = 1022f, OldPositionY = y, OldVelocityX = .9f });
            Assert.True(Npcs.TrySpawnVanilla(in creation, out var spawned));
            handle = spawned.Handle;
            // Initialize the actual world's candidates, clock, status and physical
            // frame state before separating planning and completion below.
            NonresidentBunnyNpcPhase1458.Tick(World);
            Assert.True(Current().Revision.Value > spawned.Revision.Value);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var graph = Assert.IsType<ServerRuntimeComposition>(typeof(ServerRuntimeState)
                .GetField("_runtime", flags)!.GetValue(World));
            Targeting = Assert.IsType<VanillaNpcTargetingAiStepper>(typeof(NpcAuthority)
                .GetField("vanillaTargeting", flags)!.GetValue(graph.Npcs));
            Motion = Assert.IsType<VanillaNpcWorldMotionAiStepper>(typeof(NpcAuthority)
                .GetField("pirateGhostMotion", flags)!.GetValue(graph.Npcs));
        }

        internal NpcSnapshot Current()
        {
            Assert.True(Npcs.TryGet(handle, out var current));
            return current;
        }

        internal void MovePlayer(float x, float y) => World.Apply(new PlayerMovementRuntimeCommand(Connection,
            new(session.Slot, 0, 0, 0, 0, 0, x, y, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));

        internal void KillPlayer() => World.Apply(new PlayerHealthRuntimeCommand(Connection, new(session.Slot, 0, 400)));

        internal void ChangeNpc(ushort target, float? velocity = null)
        {
            var current = Current();
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(current.TypeIdentity, current.NetIdentity, out var definition));
            Assert.True(definition.TryResolveHitbox(current.Simulation, out var body));
            float x = velocity.HasValue ? 1023f : current.PositionX;
            float y = velocity.HasValue ? 1600f - body.Height : current.PositionY;
            var state = new NpcStateUpdate(current.Type, current.NetId, x, y,
                velocity ?? current.VelocityX, velocity.HasValue ? 0f : current.VelocityY, target, current.Ai,
                current.Simulation with { DirectionX = 1, DirectionY = 1, SpriteDirection = 1,
                    OldPositionX = x - 1f, OldPositionY = y });
            Assert.True(Npcs.TryUpdate(handle, in state, out _));
        }

        public void Dispose() => session.Dispose();
    }
}
