using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class SphereAiTests
{
    private static readonly (int Type, JsonElement Row)[] Rows =
        Read("WaterSphereAi1458", "1f4fb7768e26680b7e58b246bf5687bd71b3c47f7a5108a6be849ad04af50bea", 33)
        .Concat(Read("WaterSphereTarget1458", "6d9ba9b7be89efa9d2f80c11273401af7eaf206952218b9a3149771f9b21c680", 33))
        .Concat(Read("BurningSphereAi1458", "e16794fdfed48feffacff2c3aee7ac576f4d9efe3160deab0484bcf3bddd8621", 25))
        .Concat(Read("BurningSphereTarget1458", "b1ae195f83bc56ea3a5f9429f83d374643427ed9a8d84c94fa45fdf30619cace", 25)).ToArray();
    private static readonly (int Type, JsonElement Row)[] Traces =
        Read("WaterSphereTrace1458", "12068ec3bead6641f12258fab9daf352f44830b17fde6ad07ed119ceb9336b47", 33)
        .Concat(Read("BurningSphereTrace1458", "2048210d33ea9a8e9ae676a3e188964b5ef3725b10a3e35a287a30d1dfe61ae1", 25)).ToArray();
    public static TheoryData<int> Cases => new(Enumerable.Range(0, Rows.Length));
    public static TheoryData<int> TraceCases => new(Enumerable.Range(0, Traces.Length));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Committed_AI_matches_original_state_and_complete_RNG_continuation(int index)
    {
        var (type, row) = Rows[index];
        var before = row.GetProperty("before");
        var random = new CapturedRandom(row.GetProperty("randomBefore"));
        var (store, ai, npc, _) = Setup(type, row, before, random,
            row.GetProperty("offsetX").GetSingle(), row.GetProperty("offsetY").GetSingle());
        AssertState(before, npc);
        // Speculation must not consume a single draw, even if the proposal is requested repeatedly.
        Assert.True(ai.TryStepState(in npc, out _));
        Assert.True(ai.TryStepState(in npc, out _));
        random.AssertState(row.GetProperty("randomBefore"));
        Assert.Equal(0, random.Calls);
        Assert.Equal(1, new RuntimeNpcAiStateExecutor(store).Tick(new SphereOnly(ai)).Applied);
        Assert.True(store.TryGet(npc.Handle, out var after));
        AssertState(row.GetProperty("after"), after);
        random.AssertState(row.GetProperty("randomAfter"));
        Assert.Equal(type == 33 ? 2 : 0, random.Calls);
    }

    [Theory]
    [MemberData(nameof(TraceCases))]
    public void Consecutive_AI_calls_retain_state_and_RNG_after_boss_removal(int index)
    {
        var (type, row) = Traces[index];
        var trace = row.GetProperty("trace");
        var random = new CapturedRandom(trace[0].GetProperty("randomBefore"));
        var (store, ai, npc, boss) = Setup(type, row, trace[0].GetProperty("before"), random, 500, 50);
        var executor = new RuntimeNpcAiStateExecutor(store);
        for (int step = 0; step < trace.GetArrayLength(); step++)
        {
            if (step == 2 && boss.IsAssigned) Assert.True(store.TryDespawn(boss));
            Assert.True(store.TryGet(npc.Handle, out var before));
            AssertState(trace[step].GetProperty("before"), before);
            random.AssertState(trace[step].GetProperty("randomBefore"));
            Assert.Equal(1, executor.Tick(new SphereOnly(ai)).Applied);
            Assert.True(store.TryGet(npc.Handle, out var after));
            AssertState(trace[step].GetProperty("after"), after);
            random.AssertState(trace[step].GetProperty("randomAfter"));
        }
        Assert.Equal(type == 33 ? 10 : 0, random.Calls);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Rejected_or_superseded_transition_does_not_consume_RNG(int kind)
    {
        var row = Rows[0].Row;
        var random = new CapturedRandom(row.GetProperty("randomBefore"));
        var (store, ai, npc, _) = Setup(33, row, row.GetProperty("before"), random, 500, 50);
        var summary = new RuntimeNpcAiStateExecutor(store).Tick(new InterveningStepper(ai, store, kind));
        Assert.Equal(kind == 3 ? 1 : 0, summary.Applied);
        Assert.Equal(0, random.Calls);
        random.AssertState(row.GetProperty("randomBefore"));
        if (kind == 1 || kind == 3)
        {
            Assert.True(store.TryGet(npc.Handle, out var current));
            Assert.Equal(9f, current.Simulation.Rotation);
        }
    }

    [Fact]
    public void Rotation_is_retained_reset_and_validated_by_state_ownership()
    {
        var store = new RuntimeNpcStore();
        Assert.True(store.TrySpawnIntent(new(VanillaNpcIds.WaterSphere, 1000, 1000, 0, 0, 0), out var npc));
        Assert.Equal(0f, npc.Simulation.Rotation);
        var update = Update(npc) with { Simulation = npc.Simulation with { Rotation = 1.25f } };
        Assert.True(store.TryUpdate(npc.Handle, in update, out npc));
        update = Update(npc) with { Simulation = npc.Simulation with { Rotation = null } };
        Assert.True(store.TryUpdate(npc.Handle, in update, out npc));
        Assert.Equal(1.25f, npc.Simulation.Rotation);
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            update = Update(npc) with { Simulation = npc.Simulation with { Rotation = invalid } };
            Assert.False(store.TryUpdate(npc.Handle, in update, out _));
        }
        update = Update(npc) with { Simulation = npc.Simulation with { Rotation = 0f } };
        Assert.True(store.TryUpdate(npc.Handle, in update, out npc));
        Assert.Equal(0f, npc.Simulation.Rotation);
        update = Update(npc) with { Type = 25, NetId = 25, Simulation = npc.Simulation with { Rotation = 9f } };
        Assert.True(store.TryUpdate(npc.Handle, in update, out npc));
        Assert.Equal(0f, npc.Simulation.Rotation);
    }

    private static NpcStateUpdate Update(NpcSnapshot npc) => new(npc.Type, npc.NetId, npc.PositionX, npc.PositionY,
        npc.VelocityX, npc.VelocityY, npc.Target, npc.Ai, npc.Simulation);

    private sealed class InterveningStepper(VanillaNpcTargetingAiStepper inner, RuntimeNpcStore store, int kind)
        : INpcAiStateStepper, INpcAiStateStepperWrapper, INpcAiStatePostCommitObserver
    {
        public INpcAiStateStepper InnerStepper => inner;
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            if (!inner.TryStepState(in npc, out next)) return false;
            if (kind == 0)
            {
                Assert.True(store.TryDespawn(npc.Handle));
                var replacement = Update(npc);
                Assert.True(store.TrySpawn(npc.Handle.Slot, in replacement, out _));
            }
            else if (kind == 1) Intervene(npc);
            else if (kind == 2) next = next with { Simulation = next.Simulation with { Rotation = float.NaN } };
            return true;
        }
        public void NpcAiStateCommitted(in NpcSnapshot before, in NpcSnapshot committed)
        {
            if (kind == 3) Intervene(committed);
        }
        private void Intervene(NpcSnapshot npc)
        {
            var update = Update(npc) with { Simulation = npc.Simulation with { Rotation = 9f } };
            Assert.True(store.TryUpdate(npc.Handle, in update, out _));
        }
    }

    private static (RuntimeNpcStore Store, VanillaNpcTargetingAiStepper Ai, NpcSnapshot Npc, NpcHandle Boss)
        Setup(int type, JsonElement row, JsonElement before, CapturedRandom random, float offsetX, float offsetY)
    {
        var store = new RuntimeNpcStore();
        NpcHandle boss = default;
        if (row.GetProperty("headActive").GetBoolean())
        {
            Assert.True(store.TrySpawnIntent(new(new(type == 33 ? 35 : 113), 1000, 1000, 0, 0, 0)
                { StartSlot = 5, InitialAi = new(1, 0, 0, 0) }, out var parent));
            boss = parent.Handle;
        }
        var initial = FromOriginal(type, before);
        Assert.True(store.TrySpawn(10, in initial, out var npc));
        var ai = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper(), random: random);
        int mode = row.GetProperty("mode").GetInt32();
        ai.SetWorldConditions(false, false, goodWorld: row.GetProperty("good").GetBoolean(), expertMode: mode >= 1, masterMode: mode >= 2);
        int actorState = row.TryGetProperty("actorState", out var actor) ? actor.GetInt32() : 0;
        // Reproduce original float addition/subtraction when constructing Player.position then Player.Center.
        float playerX = (before.GetProperty("x").GetSingle() + 8 + offsetX - 10) + 10;
        float playerY = (before.GetProperty("y").GetSingle() + 8 + offsetY - 21) + 21;
        ai.SetCandidates([new(0, playerX, playerY, 0, actorState != 1, actorState == 2, actorState == 3, false)]);
        Span<NpcSnapshot> peers = stackalloc NpcSnapshot[2];
        int count = store.CopyActive(peers);
        ai.SetNpcPeers(peers[..count]);
        return (store, ai, npc, boss);
    }

    private static NpcStateUpdate FromOriginal(int type, JsonElement row) => new(type, (short)type,
        row.GetProperty("x").GetSingle(), row.GetProperty("y").GetSingle(),
        row.GetProperty("vx").GetSingle(), row.GetProperty("vy").GetSingle(),
        row.GetProperty("target").GetUInt16(), ReadAi(row.GetProperty("ai")),
        NpcSimulationState.Initial with
        {
            Life = row.GetProperty("life").GetInt32(), LifeMax = row.GetProperty("lifeMax").GetInt32(),
            DirectionX = row.GetProperty("direction").GetInt32(), DirectionY = row.GetProperty("directionY").GetInt32(),
            SpriteDirection = row.GetProperty("spriteDirection").GetInt32(), Rotation = row.GetProperty("rotation").GetSingle(),
            DamageOverride = row.GetProperty("damage").GetInt32(), DefenseOverride = row.GetProperty("defense").GetInt32(),
            Alpha = row.GetProperty("alpha").GetInt32(), Scale = row.GetProperty("scale").GetSingle(),
            NoGravity = row.GetProperty("noGravity").GetBoolean(), NoTileCollide = row.GetProperty("noTileCollide").GetBoolean(),
            DontTakeDamage = row.GetProperty("dontTakeDamage").GetBoolean(), TimeLeft = row.GetProperty("timeLeft").GetInt32(),
            LocalAi = ReadAi(row.GetProperty("localAi")), JustHit = row.GetProperty("justHit").GetBoolean()
        });

    private static NpcAiState ReadAi(JsonElement ai) => new(ai[0].GetSingle(), ai[1].GetSingle(), ai[2].GetSingle(), ai[3].GetSingle());

    private static void AssertState(JsonElement row, NpcSnapshot npc)
    {
        Assert.Equal(row.GetProperty("active").GetBoolean(), npc.IsActive);
        Assert.Equal(row.GetProperty("x").GetSingle(), npc.PositionX);
        Assert.Equal(row.GetProperty("y").GetSingle(), npc.PositionY);
        Assert.Equal(row.GetProperty("vx").GetSingle(), npc.VelocityX);
        Assert.Equal(row.GetProperty("vy").GetSingle(), npc.VelocityY);
        Assert.Equal(row.GetProperty("target").GetInt32(), npc.Target);
        Assert.Equal(ReadAi(row.GetProperty("ai")), npc.Ai);
        Assert.Equal(ReadAi(row.GetProperty("localAi")), npc.Simulation.LocalAi);
        Assert.Equal(row.GetProperty("direction").GetInt32(), npc.Simulation.DirectionX);
        Assert.Equal(row.GetProperty("directionY").GetInt32(), npc.Simulation.DirectionY);
        Assert.Equal(row.GetProperty("spriteDirection").GetInt32(), npc.Simulation.SpriteDirection);
        Assert.Equal(row.GetProperty("rotation").GetSingle(), npc.Simulation.Rotation);
        Assert.Equal(row.GetProperty("dontTakeDamage").GetBoolean(), npc.Simulation.DontTakeDamage);
        Assert.Equal(row.GetProperty("timeLeft").GetInt32(), npc.Simulation.TimeLeft);
        Assert.Equal(row.GetProperty("alpha").GetInt32(), npc.Simulation.Alpha);
        Assert.Equal(row.GetProperty("justHit").GetBoolean(), npc.Simulation.JustHit);
        Assert.Equal(row.GetProperty("life").GetInt32(), npc.Simulation.Life);
        Assert.Equal(row.GetProperty("lifeMax").GetInt32(), npc.Simulation.LifeMax);
        Assert.Equal(row.GetProperty("damage").GetInt32(), npc.Simulation.DamageOverride);
        Assert.Equal(row.GetProperty("defense").GetInt32(), npc.Simulation.DefenseOverride);
        Assert.Equal(row.GetProperty("noGravity").GetBoolean(), npc.Simulation.NoGravity);
        Assert.Equal(row.GetProperty("noTileCollide").GetBoolean(), npc.Simulation.NoTileCollide);
        Assert.Equal(row.GetProperty("scale").GetSingle(), npc.Simulation.Scale);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, out var definition));
        Assert.True(definition.TryResolveHitbox(npc.Simulation, out var hitbox));
        Assert.Equal(row.GetProperty("width").GetInt32(), hitbox.Width);
        Assert.Equal(row.GetProperty("height").GetInt32(), hitbox.Height);
    }

    private sealed class SphereOnly(INpcAiStateStepper inner) : INpcAiStateStepper, INpcAiStateStepperWrapper
    {
        public INpcAiStateStepper InnerStepper => inner;
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return npc.Type is 25 or 33 && inner.TryStepState(in npc, out next);
        }
    }

    // Test-only state injection starts at the independently captured post-NewNPC stream position.
    // This tests AI continuation, not the still-unimplemented shared NewNPC RNG consumption.
    private sealed class CapturedRandom : IVanillaNpcRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(0);
        private static readonly FieldInfo Index = typeof(VanillaUnifiedRandom1458).GetField("inext", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly FieldInfo Seeds = typeof(VanillaUnifiedRandom1458).GetField("seedArray", BindingFlags.Instance | BindingFlags.NonPublic)!;
        public int Calls { get; private set; }
        public CapturedRandom(JsonElement state)
        {
            Index.SetValue(random, state.GetProperty("index").GetUInt32());
            state.GetProperty("seed").EnumerateArray().Select(x => x.GetInt32()).ToArray().CopyTo((int[])Seeds.GetValue(random)!, 0);
        }
        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            Assert.Equal(0, inclusiveMin); Assert.Equal(5, exclusiveMax);
            Calls++;
            return random.Next(inclusiveMin, exclusiveMax);
        }
        public double NextDouble() => throw new InvalidOperationException("AI9 does not request doubles.");
        public void AssertState(JsonElement state)
        {
            Assert.Equal(state.GetProperty("index").GetUInt32(), (uint)Index.GetValue(random)!);
            Assert.Equal(state.GetProperty("seed").EnumerateArray().Select(x => x.GetInt32()), (int[])Seeds.GetValue(random)!);
        }
    }

    private static (int, JsonElement)[] Read(string name, string hash, int type)
    {
        using var resource = typeof(SphereAiTests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var bytes = new MemoryStream(); gzip.CopyTo(bytes);
        Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using var json = JsonDocument.Parse(bytes.ToArray());
        return json.RootElement.EnumerateArray().Select(row => (type, row.Clone())).ToArray();
    }
}
