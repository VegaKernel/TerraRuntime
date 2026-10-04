using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class BigMimicWorldExecution1458Tests
{
    public static IEnumerable<object[]> OuterCases() => BigMimicAi1458Tests.Read("BigMimicOuter1458");

    [Theory]
    [MemberData(nameof(OuterCases))]
    public void Retained_whole_AI_then_dry_physics_matches_original_UpdateNPC(JsonElement row)
    {
        float F(string key) => row.GetProperty(key).GetSingle();
        int I(string key) => row.GetProperty(key).GetInt32();
        bool B(string key) => row.GetProperty(key).GetBoolean();
        var tiles = new WorldTileStore(new WorldDimensions(600, 500));
        for (int x = 0; x < 600; x++)
            tiles.Set(x, 80, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        var random = new SystemVanillaNpcRandom(I("seed"));
        var targeting = new VanillaNpcTargetingAiStepper(new Rejecting(), random: random);
        var world = new VanillaNpcWorldMotionAiStepper(targeting, tiles, 140d);
        targeting.SetWorldConditions(true, false, expertMode: B("expert"));
        targeting.SetCandidates([new(0, 1014.75f + F("dx"), 1022.25f + F("dy"), 0, true, false, false, false)]);
        targeting.SetBigMimicEffects(new RuntimeBigMimicEffects1458(new RuntimeProjectileStore(),
            new RuntimeWorldItemStore(), new Players()), false);
        var store = new RuntimeNpcStore();
        var initial = new NpcStateUpdate(I("type"), (short)I("type"), 1000.75f, 1000.25f, F("vx"), F("vy"), 0,
            new(F("phase"), F("clock"), F("repeat"), F("hops")), NpcSimulationState.Initial with
            {
                DirectionX = 1, DirectionY = 1, SpriteDirection = 1,
                Life = 3500, LifeMax = 3500, DamageOverride = B("expert") ? 180 : 90,
                DefenseOverride = 34, Alpha = 3, SpawnDifficulty = B("expert") ? 2f : 1f,
                NoGravity = true, NoTileCollide = true, DontTakeDamage = true, ReflectsProjectiles = true
            });
        Assert.True(store.TrySpawn(150, in initial, out var before));
        bool finite = row.GetProperty("outVx").ValueKind != JsonValueKind.String &&
            row.GetProperty("outVy").ValueKind != JsonValueKind.String;
        Assert.Equal(finite, world.TryStepState(in before, out var proposal));
        if (!finite)
        {
            Assert.Equal(I("seed") == 1 ? 534011718 : new SystemVanillaNpcRandom(I("seed")).SourceRandom.Next(), random.SourceRandom.Next());
            Assert.True(store.TryGet(before.Handle, out var unchanged));
            Assert.Equal(before, unchanged);
            return;
        }
        Assert.True(store.TryUpdateUnpublished(before.Handle, in proposal, out var accepted));
        var completed = world.CompleteCommittedState(in before, in accepted, new Mutations(store));
        Assert.True(completed.IsActive);
        Assert.Equal(F("outX"), completed.PositionX);
        Assert.Equal(F("outY"), completed.PositionY);
        Assert.Equal(F("outVx"), completed.VelocityX);
        Assert.Equal(F("outVy"), completed.VelocityY);
        var ai = row.GetProperty("ai");
        Assert.Equal(new NpcAiState(ai[0].GetSingle(), ai[1].GetSingle(), ai[2].GetSingle(), ai[3].GetSingle()), completed.Ai);
        Assert.Equal(I("direction"), completed.Simulation.DirectionX);
        Assert.Equal(I("directionY"), completed.Simulation.DirectionY);
        Assert.Equal(I("sprite"), completed.Simulation.SpriteDirection);
        Assert.Equal(I("life"), completed.Simulation.Life);
        Assert.Equal(I("damage"), completed.Simulation.DamageOverride);
        Assert.Equal(I("defense"), completed.Simulation.DefenseOverride);
        Assert.Equal(I("alpha"), completed.Simulation.Alpha);
        Assert.Equal(F("knockback"), completed.Simulation.KnockBackResist);
        Assert.Equal(B("noGravity"), completed.Simulation.NoGravity);
        Assert.Equal(B("noTile"), completed.Simulation.NoTileCollide);
        Assert.Equal(B("dontTakeDamage"), completed.Simulation.DontTakeDamage);
        Assert.Equal(B("reflects"), completed.Simulation.ReflectsProjectiles);
        Assert.Equal(I("next"), random.SourceRandom.Next());
    }

    private sealed class Rejecting : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) { next = default; return false; }
    }

    private sealed class Players : IRuntimePlayerSlotSnapshotLookup
    {
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot) { snapshot = default; return false; }
    }

    internal sealed class Mutations(RuntimeNpcStore store) : INpcAiCommittedNpcMutationSink
    {
        public bool TryGetActive(byte slot, out NpcSnapshot npc) => store.TryGetActive(slot, out npc);
        public bool TryUpdateState(in NpcSnapshot expected, in NpcStateUpdate update, out NpcSnapshot committed)
        {
            committed = default;
            return store.TryGet(expected.Handle, out var current) && current == expected &&
                store.TryUpdateUnpublished(expected.Handle, in update, out committed);
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
