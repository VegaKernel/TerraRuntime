using System.Reflection;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GroundCritterLowerWorld1458Tests
{
    [Fact]
    public void Actual_npc_phase_strikes_lower_world_ai7_but_continues_the_same_squirrel_after_its_real_turn()
    {
        foreach (bool hostile in new[] { false, true })
        {
            using var f = new Fixture();
            if (hostile) f.Turn();
            var before = f.MoveBelow();
            // Original NPC.UpdateNPC91439 checks live aiStyle==7, not the type's definition.
            Assert.Equal(hostile ? 3 : 7, before.Simulation.AiStyleOverride?.Value ?? 7);
            f.Tick();
            Assert.Equal(hostile, f.Npcs.TryGet(before.Handle, out var after));
            if (hostile)
            {
                Assert.Equal(before.Handle, after.Handle);
                Assert.True(after.Revision.Value > before.Revision.Value);
                Assert.Equal(25, after.Simulation.Life);
                Assert.Equal(3, after.Simulation.AiStyleOverride?.Value);
            }
        }
    }

    [Fact]
    public void Lower_world_death_owner_itself_refuses_retained_ai3_without_npc_or_entropy_mutation()
    {
        foreach (bool hostile in new[] { false, true })
        {
            using var f = new Fixture();
            if (hostile) f.Turn();
            var before = f.MoveBelow();
            var checkpoint = f.Random.Clone();
            Assert.Equal(!hostile, f.Lifecycle.TryCommitLowerWorldDeath(in before, f.Random));
            if (hostile)
            {
                Assert.Equal(before, f.Current());
                Assert.True(checkpoint.HasSameState(f.Random));
            }
            else
            {
                Assert.False(f.Npcs.TryGet(before.Handle, out _));
                var after = f.Random.Clone();
                Assert.False(f.Lifecycle.TryCommitLowerWorldDeath(in before, f.Random));
                Assert.True(after.HasSameState(f.Random));
            }
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly RuntimeNpcStore Npcs = new();
        internal readonly VanillaUnifiedRandom1458 Random = new(1458);
        internal readonly ServerRuntimeState World;
        internal readonly RuntimeGroundCritterLifecycle1458 Lifecycle;
        private readonly PlayerJoinSession session;
        private readonly ConnectionHandle connection;
        private readonly NpcHandle handle;

        internal Fixture()
        {
            var tiles = new WorldTileStore(new(200, 200));
            Assert.True(tiles.TryAttachWorldSurface(80d));
            for (int x = 0; x < 200; x++) tiles.Set(x, 100, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            World = new(npcs: Npcs, worldTiles: tiles,
                worldClock: new RuntimeWorldClock(0, true, (VanillaMoonPhase)0, 0, 0),
                naturalSpawnRandom: new SystemVanillaNpcRandom(Random), worldItemSpawnRandom: new SystemWorldItemSpawnRandom(Random));
            var slots = new PlayerSlotPool(1);
            Assert.True(slots.TryAcquireConnection(out var lease));
            session = new(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            connection = new(GameCommandSourceId.FromConnection(9440), session.Handle);
            World.Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 81, 100, 0, 0, 0, 0, 0)));
            Appearance("Critter witness");
            MovePlayer(1558f);
            var creation = new NpcStateUpdate(299, 299, 1023f, 1580f, .9f, 0f, 0, new(1, 60, 0, 0),
                NpcSimulationState.Initial with { DirectionX = 1, DirectionY = 1, SpriteDirection = 1,
                    OldPositionX = 1022f, OldPositionY = 1580f, OldVelocityX = .9f,
                    HomeTileX = 64, HomeTileY = 100, Homeless = false, FrameIndex = 0 });
            Assert.True(Npcs.TrySpawnVanilla(in creation, out var born));
            handle = born.Handle;
            Tick();
            Assert.True(Current().Revision.Value > born.Revision.Value);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var graph = Assert.IsType<ServerRuntimeComposition>(typeof(ServerRuntimeState).GetField("_runtime", flags)!.GetValue(World));
            Lifecycle = Assert.IsType<RuntimeGroundCritterLifecycle1458>(typeof(NpcAuthority).GetField("gemBunnyLifecycle", flags)!.GetValue(graph.Npcs));
        }

        internal void Turn()
        {
            var before = Current();
            Appearance("Cenx");
            Tick();
            var turned = Current();
            Assert.Equal(before.Handle, turned.Handle);
            Assert.Equal(3, turned.Simulation.AiStyleOverride?.Value);
            Assert.Equal(25, turned.Simulation.LifeMax);
        }

        internal NpcSnapshot MoveBelow()
        {
            MovePlayer(2758f);
            var current = Current();
            var update = new NpcStateUpdate(current.Type, current.NetId, current.PositionX, 2780f,
                current.VelocityX, 0f, 0, current.Ai, current.Simulation with {
                    OldPositionX = current.PositionX - 1f, OldPositionY = 2780f, OldVelocityY = 0f });
            Assert.True(Npcs.TryUpdate(current.Handle, in update, out var below));
            return below;
        }

        private void Appearance(string name) => World.Apply(new PlayerAppearanceRuntimeCommand(connection,
            new(session.Slot, 0, 0, 0, 0, name, 0, 0, 0, default, default, default, default, default, default, default, 0, 0, 0)));
        private void MovePlayer(float y) => World.Apply(new PlayerMovementRuntimeCommand(connection,
            new(session.Slot, 0, 0, 0, 0, 0, 1300f, y, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        internal void Tick() => NonresidentBunnyNpcPhase1458.Tick(World);
        internal NpcSnapshot Current() { Assert.True(Npcs.TryGet(handle, out var current)); return current; }
        public void Dispose() => session.Dispose();
    }
}
