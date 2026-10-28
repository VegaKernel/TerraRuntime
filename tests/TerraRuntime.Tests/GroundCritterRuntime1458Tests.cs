using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GroundCritterRuntime1458Tests
{
    private static readonly int[] NewTypes = [148,149,299,300,361,366,367,445,447,538,539,610,616,617,625,639,640,641,642,643,644,645];
    private static readonly int[] Squirrels = [299,538,539,639,640,641,642,643,644,645];

    [Fact]
    public void All_twenty_two_new_critter_definitions_advance_through_the_actual_owned_npc_phase()
    {
        foreach (int type in NewTypes)
        {
            using var f = new Fixture(type);
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(type), out var definition));
            var before = f.Npc;
            f.Tick();
            var after = f.Current();
            Assert.True(after.Revision.Value > before.Revision.Value, $"type{type}: refused composed phase");
            Assert.NotEqual(before.PositionX, after.PositionX);
            Assert.True(definition.SupportsRuntimeLifecycle, $"type{type}: creation-only definition");
            Assert.Equal(type == 625 ? 0 : 90, before.Simulation.CatchableNpcTempImmunityCounter);
            Assert.True(float.IsFinite(after.PositionX) && float.IsFinite(after.PositionY));
            Assert.True(float.IsFinite(after.VelocityX) && float.IsFinite(after.VelocityY));
            Assert.Equal(5, after.Simulation.Life);
            Assert.Equal(5, after.Simulation.LifeMax);
            Assert.Null(after.Simulation.AiStyleOverride);
            Assert.Equal(type == 625 ? 0 : 89, after.Simulation.CatchableNpcTempImmunityCounter);
            Assert.Equal(type != 625, after.Simulation.Friendly);
            Assert.NotNull(after.Simulation.FrameIndex);
            Assert.True(double.IsFinite(after.Simulation.FrameCounter));
        }
    }

    [Fact]
    public void Real_Cenx_name_turns_squirrels_and_the_next_owned_ai3_phase_continues_the_same_generation()
    {
        foreach (int type in Squirrels)
        {
            using var f = new Fixture(type, "Cenx");
            var before = f.Npc;
            Assert.Null(before.Simulation.AiStyleOverride);
            f.Tick();
            var turned = f.Current();
            Assert.True(turned.Revision.Value > before.Revision.Value);
            Assert.Equal(before.Handle, turned.Handle);
            Assert.Equal(3, turned.Simulation.AiStyleOverride?.Value);
            Assert.Equal(25, turned.Simulation.Life);
            Assert.Equal(25, turned.Simulation.LifeMax);
            Assert.Equal(5, turned.Simulation.BaseLifeMax);
            Assert.Equal((before.Simulation.DefenseOverride ?? before.Simulation.BaseDefense ?? 0) + 6,
                turned.Simulation.DefenseOverride ?? turned.Simulation.BaseDefense);
            Assert.Equal(20, turned.Simulation.DamageOverride);
            Assert.Equal(true, turned.Simulation.Friendly);
            Assert.Equal(89, turned.Simulation.CatchableNpcTempImmunityCounter);
            f.Tick();
            var continued = f.Current();
            Assert.Equal(turned.Handle, continued.Handle);
            Assert.True(continued.Revision.Value > turned.Revision.Value, $"type{type}: AI3 continuation retired");
            Assert.Equal(3, continued.Simulation.AiStyleOverride?.Value);
            Assert.Equal(25, continued.Simulation.LifeMax);
            Assert.Equal(20, continued.Simulation.DamageOverride);
            Assert.Equal(88, continued.Simulation.CatchableNpcTempImmunityCounter);
            Assert.True(float.IsFinite(continued.Ai.Ai3));
            Assert.True(float.IsFinite(continued.PositionX) && float.IsFinite(continued.PositionY));
            Assert.NotEqual(turned.PositionX, continued.PositionX);
        }
    }

    [Fact]
    public void A_real_player_without_an_appearance_report_owns_the_neutral_empty_constructor_name()
    {
        foreach (int type in Squirrels)
        {
            using var f = new Fixture(type, name: null);
            using var namedNeutral = new Fixture(type, "Critter witness");
            var before = f.Npc;
            f.Tick();
            namedNeutral.Tick();
            var after = f.Current();
            Assert.True(after.Revision.Value > before.Revision.Value);
            Assert.NotEqual(before.PositionX, after.PositionX);
            Assert.Null(after.Simulation.AiStyleOverride);
            Assert.Equal(5, after.Simulation.LifeMax);
            Assert.Equal(89, after.Simulation.CatchableNpcTempImmunityCounter);
            Assert.Equal(namedNeutral.Current(), after);
            Assert.True(namedNeutral.Random.HasSameState(f.Random));
        }
        foreach (int type in new[] { 148, 361, 625 })
        {
            using var f = new Fixture(type, name: null);
            f.Tick();
            Assert.True(f.Current().Revision.Value > f.Npc.Revision.Value);
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly WorldTileStore Tiles = new(new WorldDimensions(200,200));
        internal readonly RuntimeNpcStore Npcs = new();
        internal readonly VanillaUnifiedRandom1458 Random = new(1458);
        internal readonly ServerRuntimeState World;
        internal readonly NpcSnapshot Npc;
        private readonly PlayerJoinSession session;

        internal Fixture(int type, string? name = "Critter witness")
        {
            Assert.True(Tiles.TryAttachWorldSurface(80d));
            for (int x = 0; x < 200; x++)
                Tiles.Set(x, 100, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            World = new(npcs: Npcs, worldTiles: Tiles,
                worldClock: new RuntimeWorldClock(0, true, (VanillaMoonPhase)0, 0, 0),
                naturalSpawnRandom: new SystemVanillaNpcRandom(Random),
                worldItemSpawnRandom: new SystemWorldItemSpawnRandom(Random));
            var slots = new PlayerSlotPool(1);
            Assert.True(slots.TryAcquireConnection(out var lease));
            session = new(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(9120), session.Handle);
            World.Apply(new PlayerSpawnRuntimeCommand(connection, session,
                new(session.Slot, 81, 100, 0, 0, 0, 0, 0)));
            if (name is not null)
                World.Apply(new PlayerAppearanceRuntimeCommand(connection,
                    new(session.Slot, 0, 0, 0, 0, name, 0, 0, 0,
                        default, default, default, default, default, default, default, 0, 0, 0)));
            World.Apply(new PlayerMovementRuntimeCommand(connection,
                new(session.Slot, 0, 0, 0, 0, 0, 1300f, 1558f, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(type), out var definition));
            Assert.True(definition.TryResolveHitbox(NpcSimulationState.Initial with { Scale = definition.Scale }, out var body));
            float y = 1600f - body.Height;
            var update = new NpcStateUpdate(type, (short)type, 1023f, y, .9f, 0, 0, new(1, 60, 0, 0),
                NpcSimulationState.Initial with { DirectionX = 1, DirectionY = 1, SpriteDirection = 1,
                    OldPositionX = 1022f, OldPositionY = y, OldVelocityX = .9f,
                    HomeTileX = 64, HomeTileY = 100, Homeless = false, FrameIndex = 0, FrameCounter = 0 });
            Assert.True(Npcs.TrySpawnVanilla(in update, out Npc));
        }

        internal void Tick() => NonresidentBunnyNpcPhase1458.Tick(World);
        internal NpcSnapshot Current() { Assert.True(Npcs.TryGet(Npc.Handle, out var npc)); return npc; }
        public void Dispose() => session.Dispose();
    }
}
