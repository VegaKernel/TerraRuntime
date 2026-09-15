using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SkeletronSkullSpawnTests
{
    private static readonly JsonElement[] Linux = Read("SkeletronSkullLinux1458", "0ea728500d5fc4440326c66b6fa6870013eef890ffa813a1bacb18a5e4e2f722");
    private static readonly JsonElement[] Windows = Read("SkeletronSkullWindows1458", "68869bca7d468e7615dd528ec7919dbea3f3e5e7899c72fb07912a35934f4b7a");
    public static TheoryData<int> ArithmeticCases => new(Enumerable.Range(0, Linux.Length + Windows.Length));
    public static TheoryData<int> SpawnCases => new(Enumerable.Range(0, Linux.Length));

    [Theory, MemberData(nameof(ArithmeticCases))]
    public void Aim_matches_both_original_platforms_bit_for_bit(int index)
    {
        bool windows = index >= Linux.Length;
        var row = windows ? Windows[index - Linux.Length] : Linux[index];
        var before = row.GetProperty("before"); var expected = row.GetProperty("projectiles")[0];
        var random = new CapturedRandom(row.GetProperty("randomBefore"));
        float x = row.GetProperty("targetX").GetSingle() - (before.GetProperty("x").GetSingle() + before.GetProperty("width").GetInt32() * .5f) + random.NextInt32(-20, 21);
        float y = row.GetProperty("targetY").GetSingle() - (before.GetProperty("y").GetSingle() + before.GetProperty("height").GetInt32() * .5f) + random.NextInt32(-20, 21);
        VanillaSkeletronSkull.Aim(ref x, ref y, row.GetProperty("hands").GetInt32() == 0 ? 5f : 3f,
            random.NextInt32(-50, 51), random.NextInt32(-50, 51), windows);
        x += before.GetProperty("vx").GetSingle(); y += before.GetProperty("vy").GetSingle();
        Assert.Equal(expected.GetProperty("vx").GetSingle(), x);
        Assert.Equal(expected.GetProperty("vy").GetSingle(), y);
        random.AssertState(row.GetProperty("randomAfter"));
    }

    [Theory, MemberData(nameof(SpawnCases))]
    public void Accepted_head_spawns_original_projectile_and_preserves_RNG(int index)
    {
        var row = (OperatingSystem.IsWindows() ? Windows : Linux)[index];
        var (npcs, projectiles, ai, head, random) = Setup(row);
        Assert.True(ai.TryStepState(in head, out var proposal));
        Span<NpcAiProjectileIntent> intents = stackalloc NpcAiProjectileIntent[1];
        Assert.Equal(0, ai.PlanProjectileSpawns(in head, in proposal, intents));
        Assert.Equal(0, ai.PlanProjectileSpawns(in head, in proposal, intents));
        random.AssertState(row.GetProperty("randomBefore"));
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new HeadOnly(ai)).Applied);
        AssertProjectile(row, projectiles, head.Handle);
        random.AssertState(row.GetProperty("randomAfter"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Stale_planning_does_not_draw_or_spawn(bool replace)
    {
        var row = Linux[0]; var (npcs, projectiles, ai, head, random) = Setup(row);
        var wrapper = new HeadOnly(ai) { DuringPlanning = () => Supersede(npcs, head, replace) };
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(wrapper).Rejected);
        Assert.Equal(0, projectiles.ActiveCount); random.AssertState(row.GetProperty("randomBefore"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Collision_query_reentry_does_not_draw_or_spawn(bool replace)
    {
        var row = Linux[0]; var (npcs, projectiles, ai, head, random) = Setup(row);
        ai.SetProjectileEnvironment(new HitEnvironment(() => Supersede(npcs, head, replace)));
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new HeadOnly(ai)).Applied);
        Assert.Equal(0, projectiles.ActiveCount); random.AssertState(row.GetProperty("randomBefore"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RNG_reentry_cannot_attach_a_projectile_to_a_newer_source(bool replace)
    {
        var row = Linux[0]; var (npcs, projectiles, ai, head, random) = Setup(row);
        random.FirstDraw = () => Supersede(npcs, head, replace);
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new HeadOnly(ai)).Applied);
        Assert.Equal(0, projectiles.ActiveCount); random.AssertState(row.GetProperty("randomAfter"));
    }

    [Fact]
    public void Skull_uses_pre_motion_position_and_velocity()
    {
        var row = (OperatingSystem.IsWindows() ? Windows : Linux)[0];
        var (npcs, projectiles, ai, head, _) = Setup(row);
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new HeadOnly(ai) { Move = true }).Applied);
        Assert.True(npcs.TryGet(head.Handle, out var after)); Assert.Equal(head.PositionX + 80, after.PositionX);
        AssertProjectile(row, projectiles, head.Handle);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, true)]
    public void Blocked_sight_and_daytime_do_not_draw(bool day, bool sight)
    {
        var row = Linux[0]; var (npcs, projectiles, ai, _, random) = Setup(row);
        ai.SetWorldConditions(day, false, expertMode: true);
        ai.SetProjectileEnvironment(new HitEnvironment(result: sight));
        new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new HeadOnly(ai));
        Assert.Equal(0, projectiles.ActiveCount); random.AssertState(row.GetProperty("randomBefore"));
    }

    [Fact]
    public void Two_hand_life_gate_uses_original_double_precision()
    {
        var row = Linux[0]; var (npcs, projectiles, ai, head, random) = Setup(row);
        for (int i = 0; i < 2; i++)
            Assert.True(npcs.TrySpawnIntent(new(VanillaNpcIds.SkeletronHand, 1000, 1000, 0, 0, 0)
                { StartSlot = 11, InitialAi = new(-1, 10, 0, 0) }, out _));
        Assert.True(npcs.TryUpdate(head.Handle, new(head.Type, head.NetId, head.PositionX, head.PositionY,
            head.VelocityX, head.VelocityY, head.Target, head.Ai,
            head.Simulation with { Life = 12582912, LifeMax = 16777217 }), out _));
        new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new HeadOnly(ai));
        Assert.Equal(1, projectiles.ActiveCount); random.AssertState(row.GetProperty("randomAfter"));
    }

    [Fact]
    public void Target_distance_retreat_suppresses_the_shot_before_cadence_is_evaluated()
    {
        var row = Linux[0]; var (npcs, projectiles, ai, head, random) = Setup(row);
        ai.SetCandidates([new(0, 4000, 1000, 0, true, false, false, false)]);
        new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new HeadOnly(ai));
        Assert.True(npcs.TryGet(head.Handle, out var after)); Assert.Equal(3f, after.Ai.Ai1);
        Assert.Equal(0, projectiles.ActiveCount); random.AssertState(row.GetProperty("randomBefore"));
    }

    [Fact]
    public void Refreshed_target_is_used_by_the_accepted_shot()
    {
        var row = (OperatingSystem.IsWindows() ? Windows : Linux)[0];
        var (npcs, projectiles, ai, head, _) = Setup(row);
        Assert.True(npcs.TryUpdate(head.Handle, new(head.Type, head.NetId, head.PositionX, head.PositionY,
            head.VelocityX, head.VelocityY, 255, head.Ai, head.Simulation), out _));
        new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new HeadOnly(ai));
        AssertProjectile(row, projectiles, head.Handle);
    }

    [Fact]
    public void Failed_projectile_allocation_keeps_the_accepted_draws()
    {
        var row = Linux[0]; var (npcs, _, ai, _, random) = Setup(row);
        var full = new RuntimeProjectileStore(capacity: 1);
        Assert.True(RuntimeNpcProjectileIntentApplier.TryApply(full,
            new(VanillaProjectileIds.SkeletronSkull, 10, 20, 0, 0, 17, 0), out var occupant));
        new RuntimeNpcAiStateExecutor(npcs, full).Tick(new HeadOnly(ai));
        Assert.True(full.TryGet(occupant.Handle, out var after)); Assert.Equal(occupant, after);
        random.AssertState(row.GetProperty("randomAfter"));
    }

    private static void Supersede(RuntimeNpcStore store, NpcSnapshot head, bool replace)
    {
        Assert.True(store.TryGetActive(head.Handle.Slot, out var current));
        var update = new NpcStateUpdate(current.Type, current.NetId, 777, current.PositionY, 0, 0, current.Target, current.Ai, current.Simulation);
        if (replace)
        {
            Assert.True(store.TryDespawn(current.Handle));
            Assert.True(store.TrySpawn(current.Handle.Slot, in update, out _));
        }
        else Assert.True(store.TryUpdate(current.Handle, in update, out _));
    }

    private static (RuntimeNpcStore, RuntimeProjectileStore, VanillaNpcTargetingAiStepper, NpcSnapshot, CapturedRandom) Setup(JsonElement row)
    {
        var npcs = new RuntimeNpcStore(); var projectiles = new RuntimeProjectileStore();
        var before = row.GetProperty("before"); float difficulty = row.GetProperty("difficulty").GetSingle();
        bool good = row.GetProperty("good").GetBoolean();
        npcs.SetVanillaSpawnContextSource(() => new(difficulty, 1, good));
        Assert.True(npcs.TrySpawnIntent(new(VanillaNpcIds.SkeletronHead, 1000, 1000,
            before.GetProperty("vx").GetSingle(), before.GetProperty("vy").GetSingle(), 0)
            { StartSlot = 10, InitialAi = new(1, 0, 0, 0) }, out var head));
        for (int i = 0; i < row.GetProperty("hands").GetInt32(); i++)
            Assert.True(npcs.TrySpawnIntent(new(VanillaNpcIds.SkeletronHand, 1000, 1000, 0, 0, 0)
                { StartSlot = 11, InitialAi = new(-1, 10, 0, 0) }, out _));
        var random = new CapturedRandom(row.GetProperty("randomBefore"));
        var ai = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper(), random: random);
        ai.SetWorldConditions(false, false, expertMode: true, masterMode: difficulty >= 3, goodWorld: good);
        ai.SetCandidates([new(0, row.GetProperty("targetX").GetSingle(), row.GetProperty("targetY").GetSingle(), 0, true, false, false, false)]);
        ai.SetProjectileEnvironment(new HitEnvironment());
        return (npcs, projectiles, ai, head, random);
    }

    private static void AssertProjectile(JsonElement row, RuntimeProjectileStore store, NpcHandle source)
    {
        Assert.Equal(1, store.ActiveCount); Assert.True(store.TryGetActive(0, out var actual));
        var expected = row.GetProperty("projectiles")[0];
        Assert.Equal(expected.GetProperty("type").GetInt32(), actual.Type.Value);
        Assert.Equal(expected.GetProperty("x").GetSingle(), actual.PositionX);
        Assert.Equal(expected.GetProperty("y").GetSingle(), actual.PositionY);
        Assert.Equal(expected.GetProperty("vx").GetSingle(), actual.VelocityX);
        Assert.Equal(expected.GetProperty("vy").GetSingle(), actual.VelocityY);
        Assert.Equal(expected.GetProperty("damage").GetInt32(), actual.Damage);
        Assert.Equal(expected.GetProperty("knockBack").GetSingle(), actual.KnockBack);
        Assert.Equal(expected.GetProperty("keySpawner").GetByte(), actual.Spawner);
        Assert.Equal(expected.GetProperty("keyIndex").GetInt32(), actual.Handle.Slot);
        Assert.Equal(expected.GetProperty("keyGeneration").GetUInt32(), actual.Handle.Generation.Value);
        Assert.Equal(new ProjectileAiState(-1, 0, 0), actual.Ai);
        Assert.True(store.TryGetLifecycle(actual.Handle, out var lifecycle));
        Assert.Equal(expected.GetProperty("timeLeft").GetInt32(), lifecycle.TimeLeft);
        Assert.True(store.TryGetServerNpcSource(actual.Handle, out var provenance)); Assert.Equal(source, provenance);
    }

    private sealed class HeadOnly(VanillaNpcTargetingAiStepper ai) : INpcAiStateStepper, INpcAiPeerSnapshotConsumer,
        INpcAiProjectileIntentPlanner, INpcAiStatePostCommitEffect
    {
        public Action? DuringPlanning { get; init; }
        public bool Move { get; init; }
        public void SetNpcPeers(ReadOnlySpan<NpcSnapshot> peers) => ai.SetNpcPeers(peers);
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            if (npc.TypeIdentity != VanillaNpcIds.SkeletronHead || !ai.TryStepState(in npc, out next)) return false;
            if (Move) next = next with { PositionX = npc.PositionX + 80, PositionY = npc.PositionY + 90, VelocityX = 30, VelocityY = 40 };
            return true;
        }
        public int PlanProjectileSpawns(in NpcSnapshot source, in NpcStateUpdate proposed, Span<NpcAiProjectileIntent> destination)
        {
            int count = ai.PlanProjectileSpawns(in source, in proposed, destination); DuringPlanning?.Invoke(); return count;
        }
        public void ApplyCommittedEffect(in NpcSnapshot before, in NpcSnapshot committed, INpcAiCommittedNpcMutationSink mutations) =>
            ai.ApplyCommittedEffect(in before, in committed, mutations);
    }

    private sealed class HitEnvironment(Action? callback = null, bool result = true) : IVanillaNpcProjectileEnvironment
    {
        public bool CanHit(float x, float y, int width, int height, float tx, float ty, int tw, int th)
        { callback?.Invoke(); return result; }
    }

    private sealed class CapturedRandom : IVanillaNpcRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(0);
        private static readonly FieldInfo Index = typeof(VanillaUnifiedRandom1458).GetField("inext", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly FieldInfo Seeds = typeof(VanillaUnifiedRandom1458).GetField("seedArray", BindingFlags.Instance | BindingFlags.NonPublic)!;
        public Action? FirstDraw { get; set; }
        public CapturedRandom(JsonElement state)
        {
            Index.SetValue(random, state.GetProperty("index").GetUInt32());
            state.GetProperty("seed").EnumerateArray().Select(x => x.GetInt32()).ToArray().CopyTo((int[])Seeds.GetValue(random)!, 0);
        }
        public int NextInt32(int min, int max)
        {
            var callback = FirstDraw; FirstDraw = null; callback?.Invoke(); return random.Next(min, max);
        }
        public void AssertState(JsonElement state)
        {
            Assert.Equal(state.GetProperty("index").GetUInt32(), (uint)Index.GetValue(random)!);
            Assert.Equal(state.GetProperty("seed").EnumerateArray().Select(x => x.GetInt32()), (int[])Seeds.GetValue(random)!);
        }
    }

    private static JsonElement[] Read(string name, string hash)
    {
        using var resource = typeof(SkeletronSkullSpawnTests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var bytes = new MemoryStream(); gzip.CopyTo(bytes);
        Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using var json = JsonDocument.Parse(bytes.ToArray());
        return json.RootElement.EnumerateArray().Select(row => row.Clone()).ToArray();
    }
}
