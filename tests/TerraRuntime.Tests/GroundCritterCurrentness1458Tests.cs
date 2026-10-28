using System.Reflection;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GroundCritterCurrentness1458Tests
{
    [Fact]
    public void Actual_appearance_change_refuses_old_plan_and_next_owned_phase_turns_the_same_squirrel_once()
    {
        foreach (bool change in new[] { false, true })
        {
            using var f = new Fixture(200, 1300f);
            var before = f.Current();
            var checkpoint = f.Random.Clone();
            Assert.True(f.Targeting.TryStepState(in before, out var proposal));
            Assert.True(f.Npcs.TryUpdateUnpublished(before.Handle, in proposal, out var accepted));
            Assert.True(checkpoint.HasSameState(f.Random));
            Assert.True(f.World.TryCapturePlayerSnapshot(f.Connection.Player, out var playerBefore));
            if (change) f.Appearance("Cenx");
            Assert.True(f.World.TryCapturePlayerSnapshot(f.Connection.Player, out var playerAfter));
            // Real packet4 appearance advances the raw player revision too. This
            // checks composed currentness, not an isolated name-only guard.
            Assert.Equal(change, playerBefore.Revision != playerAfter.Revision);
            Assert.Equal(playerBefore.PositionX, playerAfter.PositionX);
            Assert.Equal(playerBefore.PositionY, playerAfter.PositionY);
            var liveRandom = f.Random.Clone();
            var completed = f.Motion.CompleteCommittedState(in before, in accepted, new RuntimeNpcAiStateExecutor(f.Npcs));
            Assert.Equal(!change, completed.Handle.IsAssigned);
            if (!change)
            {
                Assert.Equal(completed, f.Current());
                Assert.Equal(before.Simulation.CatchableNpcTempImmunityCounter - 1,
                    completed.Simulation.CatchableNpcTempImmunityCounter);
                Assert.Null(completed.Simulation.AiStyleOverride);
            }
            else
            {
                Assert.Equal(accepted, f.Current());
                Assert.True(liveRandom.HasSameState(f.Random));
                f.Tick();
                var turned = f.Current();
                Assert.Equal(before.Handle, turned.Handle);
                Assert.Equal(3, turned.Simulation.AiStyleOverride?.Value);
                Assert.Equal(25, turned.Simulation.LifeMax);
                Assert.Equal(6, turned.Simulation.DefenseOverride);
                Assert.Equal(before.Simulation.CatchableNpcTempImmunityCounter - 1,
                    turned.Simulation.CatchableNpcTempImmunityCounter);
                f.Tick();
                var continued = f.Current();
                Assert.Equal(turned.Handle, continued.Handle);
                Assert.Equal(25, continued.Simulation.LifeMax);
                Assert.Equal(6, continued.Simulation.DefenseOverride);
                Assert.Equal(turned.Simulation.CatchableNpcTempImmunityCounter - 1,
                    continued.Simulation.CatchableNpcTempImmunityCounter);
            }
        }
    }

    [Fact]
    public void Far_los_corridor_section_change_refuses_completion_while_actor_and_player_facts_stay_current()
    {
        foreach (bool change in new[] { false, true })
        {
            using var f = new Fixture(600, 5000f);
            var before = f.Current();
            var checkpoint = f.Random.Clone();
            Assert.True(f.Targeting.TryStepState(in before, out var proposal));
            Assert.True(f.Npcs.TryUpdateUnpublished(before.Handle, in proposal, out var accepted));
            Assert.True(checkpoint.HasSameState(f.Random));
            Assert.True(f.World.TryCapturePlayerSnapshot(f.Connection.Player, out var playerBefore));
            var actorSection = TerrariaSectionGeometry.FromTile(f.Tiles.Dimensions, 64, 98);
            var corridorSection = TerrariaSectionGeometry.FromTile(f.Tiles.Dimensions, 300, 98);
            Assert.NotEqual(actorSection, corridorSection);
            long actorVersion = f.Tiles.GetSectionVersion(actorSection);
            long corridorVersion = f.Tiles.GetSectionVersion(corridorSection);
            if (change) f.Tiles.Set(300, 98, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            Assert.Equal(actorVersion, f.Tiles.GetSectionVersion(actorSection));
            Assert.Equal(change, corridorVersion != f.Tiles.GetSectionVersion(corridorSection));
            Assert.True(f.World.TryCapturePlayerSnapshot(f.Connection.Player, out var playerAfter));
            Assert.Equal(playerBefore, playerAfter);
            Assert.Equal(accepted, f.Current());
            var completed = f.Motion.CompleteCommittedState(in before, in accepted, new RuntimeNpcAiStateExecutor(f.Npcs));
            Assert.Equal(!change, completed.Handle.IsAssigned);
            if (change)
            {
                Assert.Equal(accepted, f.Current());
                Assert.True(checkpoint.HasSameState(f.Random));
            }
            else
            {
                Assert.Equal(completed, f.Current());
                Assert.Equal(before.Simulation.CatchableNpcTempImmunityCounter - 1,
                    completed.Simulation.CatchableNpcTempImmunityCounter);
            }
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly WorldTileStore Tiles;
        internal readonly RuntimeNpcStore Npcs = new();
        internal readonly VanillaUnifiedRandom1458 Random = new(1458);
        internal readonly ServerRuntimeState World;
        internal readonly ConnectionHandle Connection;
        internal readonly VanillaNpcTargetingAiStepper Targeting;
        internal readonly VanillaNpcWorldMotionAiStepper Motion;
        private readonly PlayerJoinSession session;
        private readonly NpcHandle handle;

        internal Fixture(int width, float playerX)
        {
            Tiles = new(new WorldDimensions(width, 200));
            Assert.True(Tiles.TryAttachWorldSurface(80d));
            for (int x = 0; x < width; x++) Tiles.Set(x, 100, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            World = new(npcs: Npcs, worldTiles: Tiles,
                worldClock: new RuntimeWorldClock(0, true, (VanillaMoonPhase)0, 0, 0),
                naturalSpawnRandom: new SystemVanillaNpcRandom(Random), worldItemSpawnRandom: new SystemWorldItemSpawnRandom(Random));
            var slots = new PlayerSlotPool(1);
            Assert.True(slots.TryAcquireConnection(out var lease));
            session = new(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(9420), session.Handle);
            World.Apply(new PlayerSpawnRuntimeCommand(Connection, session, new(session.Slot, 81, 100, 0, 0, 0, 0, 0)));
            Appearance("Critter witness");
            World.Apply(new PlayerMovementRuntimeCommand(Connection, new(session.Slot, 0, 0, 0, 0, 0, playerX, 1558f,
                false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(299), out var definition));
            Assert.True(definition.TryResolveHitbox(NpcSimulationState.Initial with { Scale = definition.Scale }, out var body));
            float y = 1600f - body.Height;
            var update = new NpcStateUpdate(299, 299, 1023f, y, .9f, 0, 0, new(1, 60, 0, 0),
                NpcSimulationState.Initial with { DirectionX = 1, DirectionY = 1, SpriteDirection = 1,
                    OldPositionX = 1022f, OldPositionY = y, OldVelocityX = .9f,
                    HomeTileX = 64, HomeTileY = 100, Homeless = false, FrameIndex = 0, FrameCounter = 0 });
            Assert.True(Npcs.TrySpawnVanilla(in update, out var birth));
            handle = birth.Handle;
            Tick();
            Assert.True(Current().Revision.Value > birth.Revision.Value);
            Assert.Null(Current().Simulation.AiStyleOverride);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var graph = Assert.IsType<ServerRuntimeComposition>(typeof(ServerRuntimeState).GetField("_runtime", flags)!.GetValue(World));
            Targeting = Assert.IsType<VanillaNpcTargetingAiStepper>(typeof(NpcAuthority).GetField("vanillaTargeting", flags)!.GetValue(graph.Npcs));
            Motion = Assert.IsType<VanillaNpcWorldMotionAiStepper>(typeof(NpcAuthority).GetField("pirateGhostMotion", flags)!.GetValue(graph.Npcs));
        }

        internal void Appearance(string name) => World.Apply(new PlayerAppearanceRuntimeCommand(Connection,
            new(session.Slot, 0, 0, 0, 0, name, 0, 0, 0, default, default, default, default, default, default, default, 0, 0, 0)));
        internal void Tick() => NonresidentBunnyNpcPhase1458.Tick(World);
        internal NpcSnapshot Current() { Assert.True(Npcs.TryGet(handle, out var current)); return current; }
        public void Dispose() => session.Dispose();
    }
}
