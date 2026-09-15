using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class NpcSpawnContextTests
{
    private static readonly JsonElement[] Rows = ReadCases("NpcSpawnContext1458", "63196b6de175bc30bdf5eb436b510d8228d04532bb758b8e0c95161c2f89fbf5");
    private static readonly JsonElement[] FractionalRows = ReadCases("NpcSpawnFractional1458", "35a41a0e0b30de1b2ab85192b5b33c681fca538529dd60e85fb07c7d66ec1205");
    public static TheoryData<int> Cases => new(Enumerable.Range(0, Rows.Length));
    public static TheoryData<int> FractionalCases => new(Enumerable.Range(0, FractionalRows.Length));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Destroyer_and_Probe_spawns_match_original_difficulty_player_and_seed_state(int index)
        => AssertSpawn(Rows[index]);

    [Theory]
    [MemberData(nameof(FractionalCases))]
    public void Fractional_difficulty_matches_original_interpolation_and_rounding(int index)
        => AssertSpawn(FractionalRows[index], FractionalRows[index].GetProperty("requestedDifficulty").GetSingle());

    internal static void AssertSpawn(JsonElement row, float? requestedDifficulty = null)
    {
        int mode = row.GetProperty("mode").GetInt32(), players = row.GetProperty("players").GetInt32();
        bool good = row.GetProperty("good").GetBoolean();
        var context = new VanillaNpcSpawnContext(requestedDifficulty ?? mode + 1 + (good ? 1 : 0), players, good);
        Assert.Equal(row.GetProperty("difficulty").GetSingle(), context.Difficulty);
        var store = new RuntimeNpcStore();
        store.SetVanillaSpawnContextSource(() => context);
        var type = new NpcTypeId(row.GetProperty("type").GetInt32());
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(type, 1000, 1000, 0, 0, 255) { StartSlot = 10 }, out var npc));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(type, out var definition));
        Assert.True(definition.TryResolveHitbox(npc.Simulation, out var hitbox));
        Assert.Equal(row.GetProperty("slot").GetInt32(), npc.Handle.Slot);
        Assert.Equal(row.GetProperty("x").GetSingle(), npc.PositionX);
        Assert.Equal(row.GetProperty("y").GetSingle(), npc.PositionY);
        Assert.Equal(row.GetProperty("width").GetInt32(), hitbox.Width);
        Assert.Equal(row.GetProperty("height").GetInt32(), hitbox.Height);
        Assert.Equal(row.GetProperty("scale").GetSingle(), npc.Simulation.Scale);
        Assert.Equal(row.GetProperty("lifeMax").GetInt32(), npc.Simulation.LifeMax);
        Assert.Equal(npc.Simulation.LifeMax, npc.Simulation.Life);
        Assert.Equal(row.GetProperty("damage").GetInt32(), npc.Simulation.DamageOverride ?? definition.Damage);
        Assert.Equal(row.GetProperty("defense").GetInt32(), npc.Simulation.DefenseOverride ?? definition.Defense);
    }

    [Fact]
    public void Intent_captures_context_once_and_publishes_complete_scaled_spawn()
    {
        var commits = new Capture();
        var store = new RuntimeNpcStore(commitSink: commits);
        int samples = 0;
        store.SetVanillaSpawnContextSource(() => new(++samples == 1 ? 2f : 4f, 1, true));
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.Destroyer, 1000, 1000, 0, 0, 255), out var spawned));
        Assert.Equal(1, samples);
        Assert.Equal(spawned, commits.Last);
        Assert.Equal(120000, spawned.Simulation.Life);
        Assert.Equal(new NpcHitboxDimensions(76, 76), spawned.Simulation.HitboxOverride);
        Assert.Equal(1.70625f, spawned.Simulation.Scale);
    }

    [Fact]
    public void Context_is_sampled_for_each_creation_and_invalid_values_cannot_allocate()
    {
        var store = new RuntimeNpcStore();
        var context = new VanillaNpcSpawnContext(2, 1, false);
        store.SetVanillaSpawnContextSource(() => context);
        var state = new NpcStateUpdate(134, 134, 1000, 1000, 0, 0, 0, default, NpcSimulationState.Initial);
        Assert.True(store.TrySpawnVanilla(in state, out var first));
        context = new(2, 2, false);
        Assert.True(store.TrySpawnVanilla(in state, out var second));
        Assert.True(second.Simulation.LifeMax > first.Simulation.LifeMax);
        Assert.True(store.TryGet(first.Handle, out var unchanged));
        Assert.Equal(first, unchanged);
        context = new(float.NaN, 1, false);
        Assert.False(store.TrySpawnVanilla(in state, out _));
        Assert.False(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.DestroyerBody, 1000, 1000, 0, 0, 255), out _));
        Assert.Equal(2, store.ActiveCount);
    }

    private sealed class Capture : INpcStateCommitSink
    {
        public NpcSnapshot Last { get; private set; }
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot) => Last = snapshot;
    }

    [Theory]
    [InlineData(0, false)] [InlineData(0, true)]
    [InlineData(1, false)] [InlineData(1, true)]
    [InlineData(2, false)] [InlineData(2, true)]
    public void World_authority_supplies_live_context_to_head_and_same_tick_children(int mode, bool good)
    {
        var slots = new PlayerSlotPool(4);
        var identities = new ServerPlayerSlotRegistry(slots);
        var players = new ServerPlayerAuthority(new ServerPlayerStateStore(identities, slots.Capacity), identities);
        var first = new ServerPlayerId("test:spawn-first");
        var second = new ServerPlayerId("test:spawn-second");
        Assert.True(players.Create(first, 1500, 1000).IsCreated);
        Assert.True(players.Create(second, 1600, 1000).IsCreated);
        var store = new RuntimeNpcStore();
        var tiles = new WorldTileStore(new WorldDimensions(400, 400));
        Assert.True(tiles.TryAttachWorldSurface(150));
        var clock = new RuntimeWorldClock(0, false, default, 0, 1, getGoodWorld: good);
        var runtime = new ServerRuntimeState(npcs: store, serverPlayers: players, worldTiles: tiles,
            worldClock: clock, expertMode: mode >= 1, masterMode: mode == 2);
        var expected = Rows.Single(row => row.GetProperty("mode").GetInt32() == mode &&
            row.GetProperty("good").GetBoolean() == good && !row.GetProperty("hard").GetBoolean() &&
            row.GetProperty("players").GetInt32() == 2 && row.GetProperty("type").GetInt32() == 134);
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.Destroyer, 1000, 1000, 0, 0, 0)
            { StartSlot = 10 }, out var head));
        Assert.Equal(expected.GetProperty("lifeMax").GetInt32(), head.Simulation.LifeMax);
        runtime.Tick();
        Assert.True(store.TryGetActive(11, out var body));
        Assert.Equal(head.Simulation.LifeMax, body.Simulation.LifeMax);
        Assert.Equal(head.Simulation.Scale, body.Simulation.Scale);
        Assert.Equal(head.Simulation.HitboxOverride, body.Simulation.HitboxOverride);
        var bodyReference = Rows.Single(row => row.GetProperty("mode").GetInt32() == mode &&
            row.GetProperty("good").GetBoolean() == good && !row.GetProperty("hard").GetBoolean() &&
            row.GetProperty("players").GetInt32() == 2 && row.GetProperty("type").GetInt32() == 135);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.DestroyerBody, out var bodyDefinition));
        Assert.Equal(bodyReference.GetProperty("damage").GetInt32(), body.Simulation.DamageOverride ?? bodyDefinition.Damage);
        Assert.True(players.Despawn(second));
        Assert.True(store.TrySpawnIntent(new NpcAiSpawnIntent(VanillaNpcIds.Probe, 1000, 1000, 0, 0, 0), out var probe));
        var probeReference = Rows.Single(row => row.GetProperty("mode").GetInt32() == mode &&
            row.GetProperty("good").GetBoolean() == good && !row.GetProperty("hard").GetBoolean() &&
            row.GetProperty("players").GetInt32() == 1 && row.GetProperty("type").GetInt32() == 139);
        Assert.Equal(probeReference.GetProperty("lifeMax").GetInt32(), probe.Simulation.LifeMax);
        Assert.True(store.TryGet(head.Handle, out var unchanged));
        Assert.Equal(head.Simulation.LifeMax, unchanged.Simulation.LifeMax);
        Assert.True(players.Despawn(first));
    }

    private static JsonElement[] ReadCases(string name, string hash)
    {
        using var resource = typeof(NpcSpawnContextTests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var bytes = new MemoryStream(); gzip.CopyTo(bytes);
        Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using var json = JsonDocument.Parse(bytes.ToArray());
        return json.RootElement.EnumerateArray().Where(row => row.GetProperty("type").GetInt32() is 134 or 135 or 136 or 139)
            .Select(row => row.Clone()).ToArray();
    }
}
