using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class OrdinaryMeleeRuntime1458Tests
{
    private static readonly int[] Types = [44,104,140,181,196,197,464,470,489,524,525,526,527,528,529,536,691];

    [Fact]
    public void Explicit_melee_cohort_advances_through_actual_npc_and_world_composition()
    {
        foreach (int type in Types)
        foreach (bool wholeWorld in new[] { false, true })
        {
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(type), out var definition));
            Assert.True(definition.SupportsRuntimeLifecycle, $"type{type}: creation-only definition");
            Assert.Equal(VanillaNpcAiStyles.Fighter, definition.AiStyle);
            using var f = new Fixture(type);
            var before = f.Npc;
            if (wholeWorld) f.World.Tick(); else NonresidentBunnyNpcPhase1458.Tick(f.World);
            Assert.True(f.Npcs.TryGet(before.Handle, out var after));
            Assert.True(after.Revision.Value > before.Revision.Value, $"type{type}: no composed phase");
            Assert.NotEqual(before.PositionX, after.PositionX);
            Assert.Equal((ushort)0, after.Target);
            Assert.NotNull(after.Simulation.FrameIndex);
            Assert.True(double.IsFinite(after.Simulation.FrameCounter));
            Assert.False(after.Simulation.JustHit);
            Assert.Equal(before.Simulation.Life, after.Simulation.Life);
        }
    }

    [Fact]
    public void Missing_context_and_unowned_motion_refuse_before_npc_or_shared_random_adoption()
    {
        foreach (int type in Types)
        {
            using var f = new Fixture(type, knownClock: false);
            AssertInert(f);
        }
        foreach (string unsupported in new[] { "door", "shimmer", "lava", "confused", "town-frame" })
        {
            using var f = new Fixture(unsupported == "town-frame" ? 140 : 524);
            if (unsupported == "door")
                f.Tiles.Set(42, 99, new WorldTile { Type = 10, Flags = WorldTileFlags.Active });
            else
            {
                var simulation = unsupported switch {
                    "shimmer" => f.Npc.Simulation with { ShimmerTransparency = .25f },
                    "lava" => f.Npc.Simulation with { Wet = true, LiquidContact = NpcLiquidContactKind.Lava },
                    "confused" => f.Npc.Simulation with { Confused = true },
                    _ => f.Npc.Simulation };
                var ai = unsupported == "town-frame" ? f.Npc.Ai with { Ai0 = 23f } : f.Npc.Ai;
                var changed = new NpcStateUpdate(f.Npc.Type, f.Npc.NetId, f.Npc.PositionX, f.Npc.PositionY,
                    f.Npc.VelocityX, f.Npc.VelocityY, f.Npc.Target, ai, simulation);
                Assert.True(f.Npcs.TryUpdate(f.Npc.Handle, in changed, out _));
            }
            AssertInert(f);
        }

        static void AssertInert(Fixture f)
        {
            Assert.True(f.Npcs.TryGet(f.Npc.Handle, out var before));
            var checkpoint = f.Random.Clone();
            NonresidentBunnyNpcPhase1458.Tick(f.World);
            Assert.True(f.Npcs.TryGet(before.Handle, out var after));
            Assert.Equal(before, after);
            Assert.True(checkpoint.HasSameState(f.Random));
        }
    }

    [Fact]
    public void A_changed_spawn_context_cannot_replace_unrelated_shared_random_draws()
    {
        using var f = new Fixture(524);
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var field = typeof(RuntimeNpcStore).GetField("_spawnContext", flags)!;
        var original = (Func<VanillaNpcSpawnContext>)field.GetValue(f.Npcs)!;
        var expected = f.Random.Clone(); expected.Next(0, 100);
        bool changed = false;
        field.SetValue(f.Npcs, (Func<VanillaNpcSpawnContext>)(() => {
            var context = original();
            if (!changed) { changed = true; f.Random.Next(0, 100); }
            return context;
        }));
        NonresidentBunnyNpcPhase1458.Tick(f.World);
        Assert.True(changed);
        Assert.True(expected.HasSameState(f.Random));
        Assert.True(f.Npcs.TryGet(f.Npc.Handle, out var after));
        Assert.Equal(f.Npc, after);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly WorldTileStore Tiles = new(new WorldDimensions(100,200));
        internal readonly RuntimeNpcStore Npcs = new();
        internal readonly VanillaUnifiedRandom1458 Random = new(1458);
        internal readonly ServerRuntimeState World;
        internal readonly NpcSnapshot Npc;
        private readonly PlayerJoinSession session;

        internal Fixture(int type, bool knownClock = true)
        {
            Assert.True(Tiles.TryAttachWorldSurface(80d));
            for (int x = 0; x < 100; x++) Tiles.Set(x,100,new WorldTile { Type=1,Flags=WorldTileFlags.Active });
            World = new(npcs:Npcs,worldTiles:Tiles,
                worldClock:knownClock ? new RuntimeWorldClock(0,false,(VanillaMoonPhase)0,0,0) : null,
                naturalSpawnRandom:new SystemVanillaNpcRandom(Random),
                worldItemSpawnRandom:new SystemWorldItemSpawnRandom(Random));
            var slots = new PlayerSlotPool(1);
            Assert.True(slots.TryAcquireConnection(out var lease));
            session = new(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(9080),session.Handle);
            World.Apply(new PlayerSpawnRuntimeCommand(connection,session,new(session.Slot,40,100,0,0,0,0,0)));
            World.Apply(new PlayerMovementRuntimeCommand(connection,
                new(session.Slot,0,0,0,0,0,760f,1558f,false,0,0,false,0,false,0,0,0,0,false,0,0)));
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(type),out var definition));
            Assert.True(definition.TryResolveHitbox(NpcSimulationState.Initial with { Scale=definition.Scale },out var body));
            float y = 1600f-body.Height;
            var creation = new NpcStateUpdate(type,(short)type,639f,y,.9f,0f,255,default,
                NpcSimulationState.Initial with { DirectionX=1,DirectionY=1,SpriteDirection=1,
                    OldPositionX=638f,OldPositionY=y,OldVelocityX=.9f });
            Assert.True(Npcs.TrySpawnVanilla(in creation,out Npc));
        }
        public void Dispose() => session.Dispose();
    }
}
