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

public sealed class NewNpcRandomTests
{
    private static readonly JsonElement[] Rows = Read("NewNpcRng1458", "13a18afc7bb6d9a39d6ff76830aafb3496c295683cfd4a70758f5d62cc8c7d58");
    private static readonly JsonElement[] Coupled = Read("NewNpcCoupledRng1458", "2336a8850674a9935a5856b47e5c7bec5a3ec9d9775d42a6380580de7709e8c6");
    public static TheoryData<int> CoupledCases => new(Enumerable.Range(0, Coupled.Length));
    public static TheoryData<int, bool> Cases
    {
        get
        {
            var result = new TheoryData<int, bool>();
            for (int i = 0; i < Rows.Length; i++) { result.Add(i, false); result.Add(i, true); }
            return result;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Vanilla_creation_matches_original_substitution_stats_and_RNG_even_when_full(int index, bool intentPath)
    {
        var row = Rows[index];
        var random = new RandomSource(row.GetProperty("seed").GetInt32());
        var store = new RuntimeNpcStore();
        bool full = row.GetProperty("full").GetBoolean(), good = row.GetProperty("good").GetBoolean();
        if (full)
            for (int i = 0; i < 200; i++)
            {
                var filler = new NpcStateUpdate(1, 1, 0, 0, 0, 0, 0, default, NpcSimulationState.Initial);
                Assert.True(store.TrySpawn((byte)i, in filler, out _));
            }
        store.SetVanillaSpawnRandomSource(random);
        int samples = 0;
        store.SetVanillaSpawnContextSource(() =>
        {
            samples++;
            return new(row.GetProperty("mode").GetInt32() + 1 + (good ? 1 : 0), row.GetProperty("players").GetInt32(), good)
                { HardMode = row.GetProperty("hard").GetBoolean(), DownedPlantera = row.GetProperty("plant").GetBoolean() };
        });
        random.AssertState(row.GetProperty("randomBefore"));
        var requested = new NpcTypeId(row.GetProperty("requestedType").GetInt32());
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(requested, out var requestedDefinition));
        var update = new NpcStateUpdate(requested.Value, (short)requested.Value,
            1000 - requestedDefinition.Width * .5f, 1000 - requestedDefinition.Height, 0, 0, 255, default,
            NpcSimulationState.Initial with { TimeLeft = VanillaNpcDefinitionCatalog.NewNpcTimeLeft });
        NpcSnapshot npc;
        bool success = intentPath
            ? store.TrySpawnIntent(new(requested, 1000, 1000, 0, 0, 255), out npc)
            : store.TrySpawnVanilla(in update, out npc);
        Assert.Equal(!full, success);
        Assert.Equal(1, samples);
        Assert.Equal(good ? 1 : 0, random.Calls);
        random.AssertState(row.GetProperty("randomAfter"));
        Assert.Equal(full ? 200 : 1, store.ActiveCount);
        if (full) return;
        var after = row.GetProperty("after");
        Assert.Equal(row.GetProperty("slot").GetInt32(), npc.Handle.Slot);
        Assert.Equal(row.GetProperty("type").GetInt32(), npc.Type);
        Assert.Equal(row.GetProperty("netId").GetInt32(), npc.NetId);
        Assert.Equal(after.GetProperty("x").GetSingle(), npc.PositionX);
        Assert.Equal(after.GetProperty("y").GetSingle(), npc.PositionY);
        Assert.Equal(after.GetProperty("lifeMax").GetInt32(), npc.Simulation.LifeMax);
        Assert.Equal(after.GetProperty("life").GetInt32(), npc.Simulation.Life);
        Assert.Equal(row.GetProperty("defLifeMax").GetInt32(), npc.Simulation.LifeMax);
        Assert.Equal(row.GetProperty("defDamage").GetInt32(), npc.Simulation.BaseDamage);
        Assert.Equal(row.GetProperty("defDefense").GetInt32(), npc.Simulation.BaseDefense);
        Assert.Equal(row.GetProperty("difficulty").GetSingle(), npc.Simulation.SpawnDifficulty);
        Assert.Equal(row.GetProperty("knockback").GetSingle(), npc.Simulation.KnockBackResist);
        Assert.Equal(row.GetProperty("friendly").GetBoolean(), npc.Simulation.Friendly);
        Assert.Equal(row.GetProperty("chaseable").GetBoolean(), npc.Simulation.Chaseable);
        Assert.Equal(row.GetProperty("immortal").GetBoolean(), npc.Simulation.Immortal);
        Assert.Equal(after.GetProperty("alpha").GetInt32(), npc.Simulation.Alpha);
        Assert.Equal(after.GetProperty("timeLeft").GetInt32(), npc.Simulation.TimeLeft);
        Assert.Equal(after.GetProperty("directionY").GetInt32(), npc.Simulation.DirectionY);
        Assert.Equal(after.GetProperty("noGravity").GetBoolean(), npc.Simulation.NoGravity);
        Assert.Equal(after.GetProperty("noTileCollide").GetBoolean(), npc.Simulation.NoTileCollide);
        Assert.Equal(after.GetProperty("scale").GetSingle(), npc.Simulation.Scale);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, out var definition));
        Assert.True(definition.TryResolveHitbox(npc.Simulation, out var hitbox));
        Assert.Equal(after.GetProperty("width").GetInt32(), hitbox.Width);
        Assert.Equal(after.GetProperty("height").GetInt32(), hitbox.Height);
        Assert.Equal(after.GetProperty("damage").GetInt32(), npc.Simulation.DamageOverride ?? definition.Damage);
        Assert.Equal(after.GetProperty("defense").GetInt32(), npc.Simulation.DefenseOverride ?? definition.Defense);
    }

    [Theory]
    [MemberData(nameof(CoupledCases))]
    public void World_composition_shares_creation_failure_and_AI_stream(int index)
    {
        var row = Coupled[index];
        var random = new RandomSource(row.GetProperty("seed").GetInt32());
        var store = new RuntimeNpcStore();
        var runtime = new ServerRuntimeState(npcs: store, naturalSpawnRandom: random,
            worldClock: new RuntimeWorldClock(0, false, default, 0, 1, getGoodWorld: row.GetProperty("good").GetBoolean()));
        random.AssertState(row.GetProperty("randomBefore"));
        Assert.True(store.TrySpawnIntent(new(VanillaNpcIds.WaterSphere, 1000, 1000, 0, 0, 0) { StartSlot = 10 }, out _));
        random.AssertState(row.GetProperty("afterCreation"));
        for (int i = 0; i < 200; i++)
        {
            if (i == 10) continue;
            var filler = new NpcStateUpdate(1, 1, 0, 0, 0, 0, 0, default, NpcSimulationState.Initial);
            Assert.True(store.TrySpawn((byte)i, in filler, out _));
        }
        Assert.False(store.TrySpawnIntent(new(VanillaNpcIds.DarkCaster, 1000, 1000, 0, 0, 255), out _));
        random.AssertState(row.GetProperty("afterFailure"));
        // No tile world admits ground motion here; the sphere is the only AI that draws from this stream.
        runtime.Tick();
        random.AssertState(row.GetProperty("afterAi"));
        Assert.Equal(row.GetProperty("good").GetBoolean() ? 4 : 2, random.Calls);
    }

    [Fact]
    public void Explicit_storage_and_invalid_context_do_not_consume_creation_RNG()
    {
        var store = new RuntimeNpcStore();
        var random = new RandomSource(0);
        store.SetVanillaSpawnRandomSource(random);
        store.SetVanillaSpawnContextSource(() => new(2, 1, true));
        var update = new NpcStateUpdate(46, 46, 0, 0, 0, 0, 0, default, NpcSimulationState.Initial);
        Assert.True(store.TrySpawn(0, in update, out var exact));
        Assert.Equal(46, exact.Type);
        Assert.Equal(0, random.Calls);
        store.SetVanillaSpawnContextSource(() => new(float.NaN, 1, true));
        Assert.False(store.TrySpawnVanilla(in update, out _));
        Assert.False(store.TrySpawnIntent(new(VanillaNpcIds.Bunny, 1000, 1000, 0, 0, 0), out _));
        Assert.Equal(0, random.Calls);
    }

    private sealed class RandomSource(int seed) : IVanillaNpcRandom
    {
        private readonly SystemVanillaNpcRandom source = new(seed);
        public int Calls { get; private set; }
        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            Assert.Equal(0, inclusiveMin); Assert.True(exclusiveMax is 3 or 5);
            Calls++;
            return source.NextInt32(inclusiveMin, exclusiveMax);
        }
        public void AssertState(JsonElement state)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var random = (VanillaUnifiedRandom1458)typeof(SystemVanillaNpcRandom).GetField("_random", flags)!.GetValue(source)!;
            Assert.Equal(state.GetProperty("index").GetUInt32(), (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(random)!);
            Assert.Equal(state.GetProperty("seed").EnumerateArray().Select(x => x.GetInt32()),
                (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(random)!);
        }
    }

    private static JsonElement[] Read(string name, string hash)
    {
        if (OperatingSystem.IsWindows())
        {
            (name, hash) = name switch
            {
                "NewNpcRng1458" => ("NewNpcRngWindows1458", "11d0ee3132d0101523d121aa2a144ff339924e45989d9b00b2504a86787358bb"),
                _ => (name, hash)
            };
        }
        using var resource = typeof(NewNpcRandomTests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var bytes = new MemoryStream(); gzip.CopyTo(bytes);
        Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using var json = JsonDocument.Parse(bytes.ToArray());
        return json.RootElement.EnumerateArray().Select(x => x.Clone()).ToArray();
    }
}
