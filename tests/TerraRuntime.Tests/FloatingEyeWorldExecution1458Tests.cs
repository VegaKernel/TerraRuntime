using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Players;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;
public sealed class FloatingEyeWorldExecution1458Tests
{
    public static IEnumerable<object[]> OuterCases() => FloatingEyeAi1458Tests.Read("FloatingEyeOuter1458");
    [Theory]
    [MemberData(nameof(OuterCases))]
    public void Retained_AI_physics_frame_and_CheckActive_match_original_world_tick(JsonElement row)
    {
        float F(string key) => row.GetProperty(key).GetSingle();
        int I(string key) => row.GetProperty(key).GetInt32();
        bool B(string key) => row.GetProperty(key).GetBoolean();
        var tiles = new WorldTileStore(new WorldDimensions(600, 500));
        for (int x = 0; x < 600; x++)
            tiles.Set(x, 80, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        for (int x = 59; x <= 73; x++)
            for (int y = 59; y <= 70; y++)
            {
                int mode = I("solidMode");
                bool solid = mode == 1 && x == 67 || mode == 2 && x >= 62 && x <= 65 && y >= 62 && y <= 64 || mode == 3 && y == 65;
                if (solid)
                    tiles.Set(x, y, new WorldTile { Type = mode == 3 ? (ushort)19 : (ushort)1, Flags = WorldTileFlags.Active });
            }

        if (B("grave"))
            for (int x = 20; x < 48; x++)
                tiles.Set(x, 20, new WorldTile { Type = 85, Flags = WorldTileFlags.Active });
        var random = new SystemVanillaNpcRandom(I("seed"));
        var players = new Players
        {
            State = new(new(new PlayerSlotId(0), new(1)), new(1), 0, 0, 0, 0, 0, 0, F("px"), F("py"), 0, 0, 0, 0, 0, 0, 0, 0, 0)
            {
                HasMount = I("body") == 2
            }
        };
        var targeting = new VanillaNpcTargetingAiStepper(new Rejecting(), random: random);
        targeting.SetPlayerSnapshotLookup(players);
        targeting.SetFlyingEyeEnvironment(new VanillaFlyingEyeWorldEnvironment(tiles));
        targeting.SetWorldConditions(B("day"), false);
        targeting.SetCandidates([new(0, F("px") + I("pw") * .5f, F("py") + I("ph") * .5f, 0, true, false, false, false) { HitboxWidth = I("pw"), HitboxHeight = I("ph") }]);
        var world = new VanillaNpcWorldMotionAiStepper(targeting, tiles, 140d);
        var store = new RuntimeNpcStore();
        var initial = new NpcStateUpdate(I("type"), (short)I("type"), 1000.75f, 1000.25f, F("vx"), F("vy"), 0, new(F("clock"), F("phase"), 0, 0), NpcSimulationState.Initial with { DirectionX = F("vx") < 0f ? -1 : 1, DirectionY = F("vy") < 0f ? -1 : 1, SpriteDirection = 1, Life = I("life"), LifeMax = I("lifeMax"), Alpha = 7, Scale = F("scale"), OldVelocityX = F("vx") * 2f, OldVelocityY = F("vy") * 2f, NoTileCollide = (I("collisionBits") & 4) != 0, CollideX = (I("collisionBits") & 1) != 0, CollideY = (I("collisionBits") & 2) != 0, Wet = B("wet"), HitboxOverride = new(I("width"), I("height")), TimeLeft = 750 });
        Assert.True(store.TrySpawn(150, in initial, out var before));
        Assert.True(world.TryStepState(in before, out var proposal));
        Assert.True(store.TryUpdateUnpublished(before.Handle, in proposal, out var accepted));
        var final = world.CompleteCommittedState(in before, in accepted, new Mutations(store));
        Assert.True(final.IsActive);
        Assert.Equal(F("outX"), final.PositionX);
        Assert.Equal(F("outY"), final.PositionY);
        Assert.Equal(F("outVx"), final.VelocityX);
        Assert.Equal(F("outVy"), final.VelocityY);
        Assert.Equal(I("direction"), final.Simulation.DirectionX);
        Assert.Equal(I("directionY"), final.Simulation.DirectionY);
        Assert.Equal(I("sprite"), final.Simulation.SpriteDirection);
        Assert.Equal(F("rotation"), final.Simulation.Rotation ?? 0f);
        Assert.Equal(I("alpha"), final.Simulation.Alpha);
        Assert.Equal(B("active") ? I("timeLeft") : 0, final.Simulation.TimeLeft);
        if (!B("active"))
        {
            Assert.Equal(0, final.Simulation.Life);
            Assert.Equal(1, store.DespawnExpired());
            Assert.False(store.TryGet(final.Handle, out _));
        }
        Assert.Equal(B("noTile"), final.Simulation.NoTileCollide);
        Assert.Equal(B("outWet"), final.Simulation.Wet);
        Assert.Equal(B("collideX"), final.Simulation.CollideX);
        Assert.Equal(B("collideY"), final.Simulation.CollideY);
        Assert.Equal(row.GetProperty("ai")[0].GetSingle(), final.Ai.Ai0);
        Assert.Equal(row.GetProperty("ai")[1].GetSingle(), final.Ai.Ai1);
        Assert.Equal(I("next"), random.SourceRandom.Next());
    }

    [Theory]
    [InlineData("rng")]
    [InlineData("tile")]
    [InlineData("player-generation")]
    [InlineData("player-revision")]
    [InlineData("candidates")]
    [InlineData("world-clock")]
    [InlineData("npc-revision")]
    public void Retained_plan_rejects_changed_owned_inputs_without_consuming_planned_random(string change)
    {
        var setup = CreateRetained();
        Assert.True(setup.World.TryStepState(in setup.Before, out var proposal));
        Assert.True(setup.Store.TryUpdateUnpublished(setup.Before.Handle, in proposal, out var accepted));
        if (change == "rng")
            setup.Random.SourceRandom.Next();
        if (change == "tile")
            setup.Tiles.Set(63, 63, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        if (change == "player-generation")
            setup.Players.State = setup.Players.State with
            {
                Player = new(new(0), new(2))
            };
        if (change == "player-revision")
            setup.Players.State = setup.Players.State with
            {
                Revision = new(2)
            };
        if (change == "candidates")
            setup.Targeting.SetCandidates([new(0, 2000, 1000, 0, true, false, false, false)]);
        if (change == "world-clock")
            setup.Targeting.SetWorldConditions(true, false);
        if (change == "npc-revision")
            Assert.True(setup.Store.TryUpdateUnpublished(accepted.Handle, in proposal, out _));
        var expectedRandom = setup.Random.SourceRandom.Clone();
        var result = setup.World.CompleteCommittedState(in setup.Before, in accepted, new Mutations(setup.Store));
        Assert.False(result.IsActive);
        Assert.True(setup.Random.SourceRandom.HasSameState(expectedRandom));
        Assert.True(setup.Store.TryGet(setup.Before.Handle, out var retained));
        Assert.Equal(setup.Before.Ai, retained.Ai);
        Assert.Equal(setup.Before.PositionX, retained.PositionX);
    }

    [Fact]
    public void Real_executor_publishes_source_Pigron_phase_once_after_physics_and_random_adoption()
    {
        var setup = CreateRetained();
        var result = new RuntimeNpcAiStateExecutor(setup.Store).Tick(setup.World);
        Assert.Equal(1, result.Applied);
        Assert.True(setup.Store.TryGet(setup.Before.Handle, out var final));
        Assert.Equal(1f, final.Ai.Ai1);
        Assert.Equal(200, final.Simulation.Alpha);
        Assert.True(final.Simulation.NoTileCollide);
        Assert.Equal(749, final.Simulation.TimeLeft);
        Assert.True(setup.Targeting.RequiresForcedUpdateAfterCompletion(in setup.Before, in final));
        Assert.NotEqual(1000.75f, final.PositionX);
        var original = new SystemVanillaNpcRandom(580);
        original.SourceRandom.Next(600);
        original.SourceRandom.Next(1000);
        Assert.True(setup.Random.SourceRandom.HasSameState(original.SourceRandom));
    }

    private static (WorldTileStore Tiles, Players Players, SystemVanillaNpcRandom Random, VanillaNpcTargetingAiStepper Targeting, VanillaNpcWorldMotionAiStepper World, RuntimeNpcStore Store, NpcSnapshot Before) CreateRetained()
    {
        var tiles = new WorldTileStore(new WorldDimensions(600, 500));
        var players = new Players
        {
            State = new(new(new(0), new(1)), new(1), 0, 0, 0, 0, 0, 0, 1100, 1000, 0, 0, 0, 0, 0, 0, 0, 0, 0)
        };
        var random = new SystemVanillaNpcRandom(580);
        var targeting = new VanillaNpcTargetingAiStepper(new Rejecting(), random: random);
        targeting.SetPlayerSnapshotLookup(players);
        targeting.SetFlyingEyeEnvironment(new VanillaFlyingEyeWorldEnvironment(tiles));
        targeting.SetCandidates([new(0, 1110, 1021, 0, true, false, false, false)]);
        targeting.SetWorldConditions(false, false);
        var world = new VanillaNpcWorldMotionAiStepper(targeting, tiles, 140d);
        var store = new RuntimeNpcStore();
        var initial = new NpcStateUpdate(170, 170, 1000.75f, 1000.25f, .5f, .5f, 0, new(300, 0, 0, 0), NpcSimulationState.Initial with { Life = 200, LifeMax = 300, DirectionX = 1, DirectionY = 1, Scale = 1 });
        Assert.True(store.TrySpawn(150, in initial, out var before));
        return (tiles, players, random, targeting, world, store, before);
    }

    private sealed class Rejecting : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }

    private sealed class Players : IRuntimePlayerSlotSnapshotLookup
    {
        public PlayerStateSnapshot State;
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        {
            snapshot = State;
            return slot.Value == 0;
        }
    }

    internal sealed class Mutations(RuntimeNpcStore store) : INpcAiCommittedNpcMutationSink
    {
        public bool TryGetActive(byte slot, out NpcSnapshot npc) => store.TryGetActive(slot, out npc);
        public bool TryUpdateState(in NpcSnapshot expected, in NpcStateUpdate update, out NpcSnapshot committed)
        {
            committed = default;
            return store.TryGet(expected.Handle, out var current) && current == expected && store.TryUpdateUnpublished(expected.Handle, in update, out committed);
        }

        public bool TrySpawn(in NpcSnapshot source, in NpcAiSpawnIntent intent, out NpcSnapshot spawned) => throw new NotSupportedException();
        public bool TrySpawn(in NpcAiSpawnIntent intent, out NpcSnapshot spawned) => throw new NotSupportedException();
        public bool TryUpdateAi(in NpcSnapshot expected, NpcAiState ai, out NpcSnapshot committed) => throw new NotSupportedException();
        public int TryHeal(NpcHandle npc, int maximumAmount) => throw new NotSupportedException();
        public bool TrySpawnProjectile(in NpcSnapshot source, in NpcAiProjectileIntent intent, out ProjectileSnapshot spawned) => throw new NotSupportedException();
        public bool TryAnnounceSkeletronTaunt(in NpcSnapshot source, int variant) => throw new NotSupportedException();
        public bool TryUpdateVelocity(NpcHandle npc, float x, float y, out NpcSnapshot committed) => throw new NotSupportedException();
        public bool TryDespawn(NpcHandle npc) => throw new NotSupportedException();
        public bool TryTranslate(NpcHandle npc, float x, float y, out NpcSnapshot committed) => throw new NotSupportedException();
        public bool TryLinkFollower(NpcHandle npc, byte slot) => throw new NotSupportedException();
    }
}
