using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class PrimeMeleeAiTests
{
    private static readonly JsonElement[] Rows = Read("PrimeMeleeAi1458", "f7fe43547d0e8971bf7683fb9a532a71013096e99c7c1a590ecceda974989916")
        .Concat(Read("PrimeMeleeEdge1458", "09a18c9d74099cf70aa31733670c05850d54622ca2a5d6cbafefe17aa2b49953"))
        .Concat(Read("PrimeMeleeParent1458", "b39f407f06ece6977646de4c3e08ab4b5a61c54e320b3bf8bc59e1c5b78661f5")).ToArray();
    public static TheoryData<int> Cases => new(Enumerable.Range(0, Rows.Length));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Melee_arm_state_projectiles_and_rng_match_original(int index)
    {
        var row = Rows[index];
        var (npcs, projectiles, ai, arm, random) = Setup(row);
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new ArmsOnly(ai)).Applied);
        if (row.GetProperty("after").GetProperty("active").GetBoolean())
        {
            Assert.True(npcs.TryGet(arm.Handle, out var after));
            AssertNpc(row.GetProperty("after"), after);
        }
        else Assert.False(npcs.TryGet(arm.Handle, out _));
        AssertProjectiles(row, projectiles, arm.Handle);
        random.AssertState(row.GetProperty("randomAfter"));
    }

    private static (RuntimeNpcStore, RuntimeProjectileStore, VanillaNpcTargetingAiStepper, NpcSnapshot, CapturedRandom) Setup(JsonElement row, INpcStateCommitSink? sink = null)
    {
        var npcs = new RuntimeNpcStore(commitSink: sink); var projectiles = new RuntimeProjectileStore();
        Assert.True(npcs.TrySpawn(row.GetProperty("parentSlot").GetByte(), FromOriginal(row.GetProperty("parent"), NpcSimulationState.Initial), out var parent));
        Assert.True(npcs.TryUpdate(parent.Handle, FromOriginal(row.GetProperty("parent"), parent.Simulation), out parent));
        if (!row.GetProperty("parent").GetProperty("active").GetBoolean()) Assert.True(npcs.TryDespawn(parent.Handle));
        Assert.True(npcs.TrySpawn(row.GetProperty("slot").GetByte(), FromOriginal(row.GetProperty("before"), NpcSimulationState.Initial), out var arm));
        Assert.True(npcs.TryUpdate(arm.Handle, FromOriginal(row.GetProperty("before"), arm.Simulation), out arm));
        AssertNpc(row.GetProperty("before"), arm);
        var random = new CapturedRandom(row.GetProperty("randomBefore"));
        var ai = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper(), random: random);
        float difficulty = row.GetProperty("before").GetProperty("difficulty").GetSingle();
        ai.SetWorldConditions(false, false, expertMode: difficulty >= 2, masterMode: difficulty >= 3, goodWorld: row.GetProperty("good").GetBoolean());
        var target = new VanillaNpcTargetCandidate(0, 1510, 1021, 0, true, false, false, false);
        ai.SetCandidates(row.TryGetProperty("secondTarget", out var second) && second.GetBoolean()
            ? [target, new(1, 910, 921, 0, true, false, false, false)] : [target]);
        Span<NpcSnapshot> peers = stackalloc NpcSnapshot[200]; int count = npcs.CopyActive(peers); ai.SetNpcPeers(peers[..count]);
        return (npcs, projectiles, ai, arm, random);
    }

    private static NpcStateUpdate FromOriginal(JsonElement row, NpcSimulationState simulation) => new(
        row.GetProperty("type").GetInt32(), row.GetProperty("netId").GetInt16(),
        row.GetProperty("x").GetSingle(), row.GetProperty("y").GetSingle(), row.GetProperty("vx").GetSingle(), row.GetProperty("vy").GetSingle(),
        row.GetProperty("target").GetUInt16(), ReadAi(row.GetProperty("ai")), simulation with
        {
            SpawnDifficulty = row.GetProperty("difficulty").GetSingle(), Life = row.GetProperty("life").GetInt32(), LifeMax = row.GetProperty("lifeMax").GetInt32(),
            BaseDamage = row.GetProperty("baseDamage").GetInt32(), BaseDefense = row.GetProperty("baseDefense").GetInt32(),
            DamageOverride = row.GetProperty("damage").GetInt32(), DefenseOverride = row.GetProperty("defense").GetInt32(),
            DirectionX = row.GetProperty("direction").GetInt32(), DirectionY = row.GetProperty("directionY").GetInt32(),
            SpriteDirection = row.GetProperty("spriteDirection").GetInt32(), Rotation = row.GetProperty("rotation").GetSingle(),
            Alpha = row.GetProperty("alpha").GetInt32(), Scale = row.GetProperty("scale").GetSingle(),
            HitboxOverride = new(row.GetProperty("width").GetInt32(), row.GetProperty("height").GetInt32()),
            NoGravity = row.GetProperty("noGravity").GetBoolean(), NoTileCollide = row.GetProperty("noTileCollide").GetBoolean(),
            DontTakeDamage = row.GetProperty("dontTakeDamage").GetBoolean(), TimeLeft = row.GetProperty("timeLeft").GetInt32(),
            LocalAi = ReadAi(row.GetProperty("localAi")), JustHit = row.GetProperty("justHit").GetBoolean(),
            ReflectsProjectiles = row.GetProperty("reflects").GetBoolean(), KnockBackResist = row.GetProperty("knockBackResist").GetSingle()
        });

    private static void AssertNpc(JsonElement row, NpcSnapshot npc)
    {
        Assert.Equal(row.GetProperty("x").GetSingle(), npc.PositionX); Assert.Equal(row.GetProperty("y").GetSingle(), npc.PositionY);
        Assert.Equal(row.GetProperty("vx").GetSingle(), npc.VelocityX); Assert.Equal(row.GetProperty("vy").GetSingle(), npc.VelocityY);
        Assert.Equal(row.GetProperty("target").GetInt32(), npc.Target); Assert.Equal(ReadAi(row.GetProperty("ai")), npc.Ai);
        Assert.Equal(ReadAi(row.GetProperty("localAi")), npc.Simulation.LocalAi);
        Assert.Equal(row.GetProperty("direction").GetInt32(), npc.Simulation.DirectionX); Assert.Equal(row.GetProperty("directionY").GetInt32(), npc.Simulation.DirectionY);
        Assert.Equal(row.GetProperty("spriteDirection").GetInt32(), npc.Simulation.SpriteDirection); Assert.Equal(row.GetProperty("rotation").GetSingle(), npc.Simulation.Rotation);
        Assert.Equal(row.GetProperty("life").GetInt32(), npc.Simulation.Life); Assert.Equal(row.GetProperty("lifeMax").GetInt32(), npc.Simulation.LifeMax);
        Assert.Equal(row.GetProperty("damage").GetInt32(), npc.Simulation.DamageOverride ?? npc.Simulation.BaseDamage);
        Assert.Equal(row.GetProperty("defense").GetInt32(), npc.Simulation.DefenseOverride ?? npc.Simulation.BaseDefense);
        Assert.Equal(row.GetProperty("baseDamage").GetInt32(), npc.Simulation.BaseDamage); Assert.Equal(row.GetProperty("baseDefense").GetInt32(), npc.Simulation.BaseDefense);
        Assert.Equal(row.GetProperty("difficulty").GetSingle(), npc.Simulation.SpawnDifficulty); Assert.Equal(row.GetProperty("scale").GetSingle(), npc.Simulation.Scale);
        Assert.Equal(row.GetProperty("alpha").GetInt32(), npc.Simulation.Alpha); Assert.Equal(row.GetProperty("justHit").GetBoolean(), npc.Simulation.JustHit);
        Assert.Equal(row.GetProperty("noGravity").GetBoolean(), npc.Simulation.NoGravity); Assert.Equal(row.GetProperty("noTileCollide").GetBoolean(), npc.Simulation.NoTileCollide);
        Assert.Equal(row.GetProperty("dontTakeDamage").GetBoolean(), npc.Simulation.DontTakeDamage); Assert.Equal(row.GetProperty("reflects").GetBoolean(), npc.Simulation.ReflectsProjectiles);
        Assert.Equal(row.GetProperty("timeLeft").GetInt32(), npc.Simulation.TimeLeft); Assert.Equal(row.GetProperty("knockBackResist").GetSingle(), npc.Simulation.KnockBackResist);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, out var definition)); Assert.True(definition.TryResolveHitbox(npc.Simulation, out var hitbox));
        Assert.Equal(row.GetProperty("width").GetInt32(), hitbox.Width); Assert.Equal(row.GetProperty("height").GetInt32(), hitbox.Height);
    }

    private static void AssertProjectiles(JsonElement row, RuntimeProjectileStore projectiles, NpcHandle source)
    {
        var expected = row.GetProperty("projectiles"); Assert.Equal(expected.GetArrayLength(), projectiles.ActiveCount);
        foreach (var p in expected.EnumerateArray())
        {
            Assert.True(projectiles.TryGetActive(p.GetProperty("slot").GetUInt16(), out var actual));
            Assert.Equal(p.GetProperty("x").GetSingle(), actual.PositionX); Assert.Equal(p.GetProperty("y").GetSingle(), actual.PositionY);
            Assert.Equal(p.GetProperty("vx").GetSingle(), actual.VelocityX); Assert.Equal(p.GetProperty("vy").GetSingle(), actual.VelocityY);
            Assert.Equal(p.GetProperty("damage").GetInt32(), actual.Damage); Assert.Equal(p.GetProperty("knockBack").GetSingle(), actual.KnockBack);
            Assert.Equal(p.GetProperty("keySpawner").GetByte(), actual.Spawner); Assert.Equal(p.GetProperty("keyIndex").GetInt32(), actual.Handle.Slot);
            Assert.Equal(p.GetProperty("keyGeneration").GetUInt32(), actual.Handle.Generation.Value);
            Assert.Equal(new ProjectileAiState(p.GetProperty("ai")[0].GetSingle(), p.GetProperty("ai")[1].GetSingle(), p.GetProperty("ai")[2].GetSingle()), actual.Ai);
            Assert.Equal(p.GetProperty("type").GetInt32(), actual.Type.Value);
            Assert.True(projectiles.TryGetLifecycle(actual.Handle, out var lifecycle)); Assert.Equal(p.GetProperty("timeLeft").GetInt32(), lifecycle.TimeLeft);
            Assert.True(projectiles.TryGetServerNpcSource(actual.Handle, out var provenance)); Assert.Equal(source, provenance);
        }
    }

    private static NpcAiState ReadAi(JsonElement ai) => new(ai[0].GetSingle(), ai[1].GetSingle(), ai[2].GetSingle(), ai[3].GetSingle());
    private sealed class ArmsOnly(INpcAiStateStepper inner) : INpcAiStateStepper, INpcAiStateStepperWrapper
    {
        public INpcAiStateStepper InnerStepper => inner;
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        { next = default; return (npc.TypeIdentity == VanillaNpcIds.PrimeSaw || npc.TypeIdentity == VanillaNpcIds.PrimeVice) && inner.TryStepState(in npc, out next); }
    }
    private sealed class CapturedRandom : IVanillaNpcRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(0);
        private static readonly FieldInfo Index = typeof(VanillaUnifiedRandom1458).GetField("inext", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly FieldInfo Seeds = typeof(VanillaUnifiedRandom1458).GetField("seedArray", BindingFlags.Instance | BindingFlags.NonPublic)!;
        public Action? FirstDraw { get; set; }
        public Action<int>? OnDraw { get; set; }
        public int Calls { get; private set; }
        public CapturedRandom(JsonElement state)
        {
            Index.SetValue(random, state.GetProperty("index").GetUInt32());
            state.GetProperty("seed").EnumerateArray().Select(x => x.GetInt32()).ToArray().CopyTo((int[])Seeds.GetValue(random)!, 0);
        }
        public int NextInt32(int min, int max)
        {
            var callback = FirstDraw; FirstDraw = null; callback?.Invoke(); Calls++; OnDraw?.Invoke(Calls); return random.Next(min, max);
        }
        public void AssertSame(CapturedRandom other)
        {
            Assert.Equal(Index.GetValue(other.random), Index.GetValue(random));
            Assert.Equal((int[])Seeds.GetValue(other.random)!, (int[])Seeds.GetValue(random)!);
        }
        public void AssertState(JsonElement state)
        {
            Assert.Equal(state.GetProperty("index").GetUInt32(), (uint)Index.GetValue(random)!);
            Assert.Equal(state.GetProperty("seed").EnumerateArray().Select(x => x.GetInt32()), (int[])Seeds.GetValue(random)!);
        }
    }

    private static JsonElement[] Read(string name, string hash)
    {
        using var resource = typeof(PrimeMeleeAiTests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var bytes = new MemoryStream(); gzip.CopyTo(bytes);
        Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using var json = JsonDocument.Parse(bytes.ToArray());
        return json.RootElement.EnumerateArray().Select(row => row.Clone()).ToArray();
    }
}
