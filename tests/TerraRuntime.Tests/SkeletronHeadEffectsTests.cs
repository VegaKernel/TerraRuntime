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

public sealed partial class SkeletronHeadEffectsTests
{
    private static readonly JsonElement[] Edges = Read("SkeletronCasterEdge1458", "11421c0e385d972cab002298f0af68dbae52546eb3cc8c2194fcf464ec81fd27");
    public static TheoryData<int> EdgeCases => new(Enumerable.Range(0, Edges.Length));

    [Theory, MemberData(nameof(EdgeCases))]
    public void World_edge_queries_include_original_above_world_and_bottom_spawns(int index)
    {
        var world = new WorldTileStore(new WorldDimensions(400, 400));
        for (int x = 0; x < 400; x++) world.Set(x, 0, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        var row = Edges[index]; var (npcs, projectiles, ai, head, random, taunts) = Setup(row, world);
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs, projectiles, taunts: taunts).Tick(new HeadOnly(ai)).Applied);
        Assert.True(npcs.TryGet(head.Handle, out var after)); AssertNpc(row.GetProperty("after"), after);
        AssertChildren(row, npcs); random.AssertState(row.GetProperty("randomAfter"));
    }

    private static readonly JsonElement[] Handoffs = OperatingSystem.IsWindows()
        ? Read("SkeletronHeadTargetHandoffWindows1458", "e79ff91ca429d9c47b1ae2604ecd7f23551e0488e60e46d200cec5808bbac837")
        : Read("SkeletronHeadTargetHandoff1458", "a2e1df8b14a54eda037786906181c0e1d18c7e6cbf52f5f92e8f7ccb995298c7");
    public static TheoryData<int> HandoffCases => new(Enumerable.Range(0, Handoffs.Length));

    [Theory, MemberData(nameof(HandoffCases))]
    public void Retargeting_preserves_the_source_skull_then_hover_handoff(int index)
    {
        var row = Handoffs[index]; var (npcs, projectiles, ai, head, random, taunts) = Setup(row, Worlds[0]);
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs, projectiles, taunts: taunts).Tick(new HeadOnly(ai)).Applied);
        Assert.True(npcs.TryGet(head.Handle, out var after)); AssertNpc(row.GetProperty("after"), after);
        AssertProjectiles(row, projectiles, head.Handle); random.AssertState(row.GetProperty("randomAfter"));
    }

    private static readonly JsonElement[] Coupled = Read("SkeletronHeadCasterCoupled1458", "c6e9221a1570bb9a33d890312251bf7f5df22e735ae295c9978b81add3e624de");
    public static TheoryData<int> CoupledCases => new(Enumerable.Range(0, Coupled.Length));

    [Theory, MemberData(nameof(CoupledCases))]
    public void New_higher_caster_runs_later_in_the_same_ascending_pass(int index)
    {
        var row = Coupled[index]; var (npcs, projectiles, ai, head, random, taunts) = Setup(row, Worlds[0], 0);
        int count = row.GetProperty("casters").GetArrayLength();
        Assert.Equal(1 + count, new RuntimeNpcAiStateExecutor(npcs, projectiles, taunts: taunts).Tick(new HeadAndCaster(ai)).Applied);
        Assert.True(npcs.TryGet(head.Handle, out var after)); AssertNpc(row.GetProperty("after"), after);
        AssertChildren(row, npcs); random.AssertState(row.GetProperty("randomAfter"));
    }

    private sealed class HeadAndCaster(INpcAiStateStepper inner) : INpcAiStateStepper, INpcAiStateStepperWrapper
    {
        public INpcAiStateStepper InnerStepper => inner;
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        { next = default; return (npc.TypeIdentity == VanillaNpcIds.SkeletronHead || npc.TypeIdentity == VanillaNpcIds.DarkCaster) && inner.TryStepState(in npc, out next); }
    }

    private static readonly JsonElement[] Rows = (OperatingSystem.IsWindows()
        ? Read("SkeletronHeadEffectsWindows1458", "2767698fbae9b81515741e01abf750a299c5acda77a31b3d82586e6becc63ef9")
        : Read("SkeletronHeadEffects1458", "d463ce4b71bcc9d2673ddbf15387dfc9809602681b1cd59bcfd544c97f00f6d1"))
        .Concat(Read("SkeletronHeadFlags1458", "7512865ec249511f5a19637b788958cc4eda8f1f63b6d60ddd4acdc45da4f6b0")).ToArray();
    private static readonly JsonElement[] Traces = OperatingSystem.IsWindows()
        ? Read("SkeletronHeadTransitionWindows1458", "3af31de5ed4bc21dd678f77ad6c467cd9dbd93fcfcafd21b7e7c94bd04ff86d6")
        : Read("SkeletronHeadTransition1458", "3bbaf16f9c43aa1df58a56da5998568cc9061e422a436ccc45bc52be6d1130ce");
    private static readonly JsonElement[] Terrain = Read("SkeletronCasterTerrain1458", "f3875c62bffca75bcb5531054ec57219ff4ba6d4acc8787e6c385d9b223956d2");
    private static readonly JsonElement[] Packets = Read("SkeletronTauntPacket1458", "76d58cb1418acfca70e7234e8c34633155acc858ed09a1c1bb00e828a8522544");
    private static readonly WorldTileStore[] Worlds = Enumerable.Range(0, 7).Select(CreateWorld).ToArray();
    public static TheoryData<int> Cases => new(Enumerable.Range(0, Rows.Length));
    public static TheoryData<int> TerrainCases => new(Enumerable.Range(0, Terrain.Length));
    public static TheoryData<int> TraceCases => new(Enumerable.Range(0, Traces.Length / 5));

    [Theory, MemberData(nameof(Cases))]
    public void Head_state_effects_and_RNG_match_original(int index)
    {
        var row = Rows[index]; var (npcs, projectiles, ai, head, random, taunts) = Setup(row, Worlds[0]);
        Assert.True(ai.TryStepState(in head, out _)); Assert.True(ai.TryStepState(in head, out _));
        random.AssertState(row.GetProperty("randomBefore"));
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs, projectiles, taunts: taunts).Tick(new HeadOnly(ai)).Applied);
        Assert.True(npcs.TryGet(head.Handle, out var after)); AssertNpc(row.GetProperty("after"), after);
        AssertChildren(row, npcs); AssertProjectiles(row, projectiles, head.Handle);
        random.AssertState(row.GetProperty("randomAfter")); AssertTaunt(row, taunts, 0, 0);
    }

    [Theory, MemberData(nameof(TerrainCases))]
    public void Summoning_terrain_cap_and_full_table_match_original(int index)
    {
        var row = Terrain[index]; var (npcs, projectiles, ai, head, random, taunts) = Setup(row, Worlds[row.GetProperty("layout").GetInt32()]);
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(npcs, projectiles, taunts: taunts).Tick(new HeadOnly(ai)).Applied);
        Assert.True(npcs.TryGet(head.Handle, out var after)); AssertNpc(row.GetProperty("after"), after);
        AssertChildren(row, npcs); random.AssertState(row.GetProperty("randomAfter"));
        Assert.Empty(taunts.Variants); Assert.Equal(0, projectiles.ActiveCount);
    }

    [Theory, MemberData(nameof(TraceCases))]
    public void Five_call_transition_preserves_skull_taunt_caster_order(int group)
    {
        var row = Traces[group * 5]; var (npcs, projectiles, ai, head, random, taunts) = Setup(row, Worlds[0]);
        var executor = new RuntimeNpcAiStateExecutor(npcs, projectiles, taunts: taunts);
        for (int step = 0; step < 5; step++)
        {
            row = Traces[group * 5 + step]; Assert.Equal(step, row.GetProperty("step").GetInt32());
            Assert.True(npcs.TryGet(head.Handle, out var before)); AssertNpc(row.GetProperty("before"), before);
            random.AssertState(row.GetProperty("randomBefore"));
            int oldProjectiles = projectiles.ActiveCount, oldTaunts = taunts.Variants.Count;
            Assert.Equal(1, executor.Tick(new HeadOnly(ai)).Applied);
            Assert.True(npcs.TryGet(head.Handle, out var after)); AssertNpc(row.GetProperty("after"), after);
            AssertChildren(row, npcs); AssertProjectiles(row, projectiles, head.Handle);
            random.AssertState(row.GetProperty("randomAfter")); AssertTaunt(row, taunts, oldProjectiles, oldTaunts);
        }
    }

    [Theory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void Localized_taunt_packet_matches_original_bytes(int variant)
    {
        var row = Packets.Single(x => x.GetProperty("taunt").GetInt32() == variant);
        Assert.True(TerrariaSkeletronTauntCodec1458.TryEncode(variant, out var bytes));
        Assert.Equal(row.GetProperty("hex").GetString(), Convert.ToHexString(bytes));
    }

    private static void AssertTaunt(JsonElement row, Taunts taunts, int oldProjectiles, int oldTaunts)
    {
        var before = row.GetProperty("before"); var after = row.GetProperty("after");
        bool emitted = before.GetProperty("ai")[1].GetSingle() == 0 && before.GetProperty("ai")[3].GetSingle() == 1 &&
            after.GetProperty("ai")[1].GetSingle() == 1 && after.GetProperty("ai")[2].GetSingle() == 0;
        Assert.Equal(oldTaunts + (emitted ? 1 : 0), taunts.Variants.Count);
        if (!emitted) return;
        var random = new CapturedRandom(row.GetProperty("randomBefore"));
        if (row.GetProperty("projectiles").GetArrayLength() > oldProjectiles)
        {
            random.NextInt32(-20, 21); random.NextInt32(-20, 21);
            random.NextInt32(-50, 51); random.NextInt32(-50, 51);
        }
        Assert.Equal(random.NextInt32(2, 6), taunts.Variants[^1]);
    }

    private static (RuntimeNpcStore, RuntimeProjectileStore, VanillaNpcTargetingAiStepper, NpcSnapshot, CapturedRandom, Taunts) Setup(JsonElement row, WorldTileStore world, byte headSlot = 10)
    {
        var npcs = new RuntimeNpcStore(); var projectiles = new RuntimeProjectileStore(); var taunts = new Taunts();
        float difficulty = row.GetProperty("difficulty").GetSingle(); bool good = row.GetProperty("good").GetBoolean();
        int players = row.GetProperty("players").GetInt32();
        npcs.SetVanillaSpawnContextSource(() => new(difficulty, players, good) { SkeletronActive = true });
        Assert.True(npcs.TrySpawnIntent(new(VanillaNpcIds.SkeletronHead, 1000, 1000, 0, 0, 0)
            { StartSlot = headSlot, InitialAi = ReadAi(row.GetProperty("before").GetProperty("ai")) }, out var head));
        // This suite evaluates AI from original input state. Windows fractional creation differences are a separate gate.
        Assert.True(npcs.TryUpdate(head.Handle, FromOriginal(row.GetProperty("before"), head.Simulation), out head));
        for (int i = 0; i < row.GetProperty("hands").GetInt32(); i++)
            Assert.True(npcs.TrySpawnIntent(new(VanillaNpcIds.SkeletronHand, 1000, 1000, 0, 0, 0)
                { StartSlot = checked((byte)(headSlot + 1)), InitialAi = new(-1, headSlot, 0, 0) }, out _));
        if (row.TryGetProperty("existing", out var existing))
            for (int i = 0; i < existing.GetInt32(); i++)
                Assert.True(npcs.TrySpawnIntent(new(VanillaNpcIds.DarkCaster, 1000, 1000, 0, 0, 255) { StartSlot = 20 }, out _));
        if (row.TryGetProperty("full", out var full) && full.GetBoolean())
            for (int i = 0; i < 200; i++)
                if (!npcs.TryGetActive((byte)i, out _)) Assert.True(npcs.TrySpawn((byte)i, new(1,1,0,0,0,0,0,default,NpcSimulationState.Initial), out _));
        var random = new CapturedRandom(row.GetProperty("randomBefore")); npcs.SetVanillaSpawnRandomSource(random);
        var ai = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper(), random: random);
        ai.SetWorldConditions(false, false, expertMode: difficulty >= 2, masterMode: difficulty >= 3, goodWorld: good);
        var target = new VanillaNpcTargetCandidate(0, row.GetProperty("targetX").GetSingle(), row.GetProperty("targetY").GetSingle(), 0, true, false, false, false);
        float otherX = row.TryGetProperty("target1X", out var tx) ? tx.GetSingle() : 10;
        float otherY = row.TryGetProperty("target1Y", out var ty) ? ty.GetSingle() : 21;
        ai.SetCandidates(players == 1 ? [target] : [target, new(1, otherX, otherY, 0, true, false, false, false)]);
        ai.SetSkeletronEnvironment(new VanillaSkeletronWorldEnvironment(world));
        ai.SetProjectileEnvironment(new VanillaNpcProjectileWorldEnvironment(world));
        Span<NpcSnapshot> peers = stackalloc NpcSnapshot[256]; int count = npcs.CopyActive(peers); ai.SetNpcPeers(peers[..count]);
        return (npcs, projectiles, ai, head, random, taunts);
    }

    private static NpcStateUpdate FromOriginal(JsonElement row, NpcSimulationState simulation) => new(
        row.GetProperty("type").GetInt32(), row.GetProperty("netId").GetInt16(),
        row.GetProperty("x").GetSingle(), row.GetProperty("y").GetSingle(), row.GetProperty("vx").GetSingle(), row.GetProperty("vy").GetSingle(),
        row.GetProperty("target").GetUInt16(), ReadAi(row.GetProperty("ai")), simulation with
        {
            Life = row.GetProperty("life").GetInt32(), LifeMax = row.GetProperty("lifeMax").GetInt32(),
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

    private static void AssertChildren(JsonElement row, RuntimeNpcStore npcs)
    {
        var expected = row.GetProperty("casters"); int count = 0;
        for (int slot = 0; slot < 200; slot++) if (npcs.TryGetActive((byte)slot, out var npc) && npc.TypeIdentity == VanillaNpcIds.DarkCaster) count++;
        Assert.Equal(expected.GetArrayLength(), count);
        foreach (var caster in expected.EnumerateArray())
        {
            Assert.True(npcs.TryGetActive(caster.GetProperty("slot").GetByte(), out var npc)); AssertNpc(caster.GetProperty("state"), npc);
        }
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
            Assert.Equal(new ProjectileAiState(-1, 0, 0), actual.Ai);
            Assert.True(projectiles.TryGetLifecycle(actual.Handle, out var lifecycle)); Assert.Equal(p.GetProperty("timeLeft").GetInt32(), lifecycle.TimeLeft);
            Assert.True(projectiles.TryGetServerNpcSource(actual.Handle, out var provenance)); Assert.Equal(source, provenance);
        }
    }

    private static WorldTileStore CreateWorld(int layout)
    {
        var world = new WorldTileStore(new WorldDimensions(400,400));
        for (int x = 0; x < 400; x++) for (int y = 0; y < 400; y++)
            if (layout == 6 || (layout > 0 && y == 100))
                world.Set(x, y, new WorldTile { Type = (ushort)(layout == 2 ? 19 : 1),
                    Flags = WorldTileFlags.Active | (layout == 5 ? WorldTileFlags.Inactive : WorldTileFlags.None),
                    Shape = (byte)(layout == 3 ? 2 : layout == 4 ? 1 : 0) });
        return world;
    }
    private static NpcAiState ReadAi(JsonElement ai) => new(ai[0].GetSingle(), ai[1].GetSingle(), ai[2].GetSingle(), ai[3].GetSingle());
    private sealed class HeadOnly(INpcAiStateStepper inner) : INpcAiStateStepper, INpcAiStateStepperWrapper
    {
        public INpcAiStateStepper InnerStepper => inner;
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        { next = default; return npc.TypeIdentity == VanillaNpcIds.SkeletronHead && inner.TryStepState(in npc, out next); }
    }
    private sealed class Taunts : INpcAiTauntCommitSink
    {
        public List<int> Variants { get; } = [];
        public void SkeletronTaunt(in NpcSnapshot source, int variant) => Variants.Add(variant);
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
        using var resource = typeof(SkeletronHeadEffectsTests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var bytes = new MemoryStream(); gzip.CopyTo(bytes);
        Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using var json = JsonDocument.Parse(bytes.ToArray());
        return json.RootElement.EnumerateArray().Select(row => row.Clone()).ToArray();
    }
}
