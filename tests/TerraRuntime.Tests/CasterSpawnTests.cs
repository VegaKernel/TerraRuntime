using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class CasterSpawnTests
{
    private static readonly JsonElement[] Rows = Read();
    public static TheoryData<int> Cases => new(Enumerable.Range(0, Rows.Length));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Creation_matches_original_difficulty_world_progression_and_head_presence(int index)
    {
        var row = Rows[index];
        bool good = row.GetProperty("good").GetBoolean();
        float difficulty = row.TryGetProperty("requestedDifficulty", out var requested)
            ? requested.GetSingle() : row.GetProperty("mode").GetInt32() + 1 + (good ? 1 : 0);
        var context = new VanillaNpcSpawnContext(difficulty, row.GetProperty("players").GetInt32(), good)
        {
            HardMode = row.GetProperty("hard").GetBoolean(),
            DownedPlantera = row.GetProperty("plant").GetBoolean(),
            SkeletronActive = row.GetProperty("headActive").GetBoolean()
        };
        var capture = new Capture();
        var store = new RuntimeNpcStore(commitSink: capture);
        int samples = 0;
        store.SetVanillaSpawnContextSource(() => { samples++; return context; });
        var type = new NpcTypeId(row.GetProperty("type").GetInt32());
        Assert.True(store.TrySpawnIntent(new(type, 1000, 1000, 0, 0, 255) { StartSlot = 10 }, out var npc));
        Assert.Equal(1, samples);
        Assert.Equal(npc, capture.Last);
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
        Assert.Equal(row.GetProperty("defLifeMax").GetInt32(), npc.Simulation.LifeMax);
        Assert.Equal(row.GetProperty("damage").GetInt32(), npc.Simulation.DamageOverride ?? definition.Damage);
        Assert.Equal(row.GetProperty("defense").GetInt32(), npc.Simulation.DefenseOverride ?? definition.Defense);
        Assert.Equal(row.GetProperty("defDamage").GetInt32(), npc.Simulation.BaseDamage);
        Assert.Equal(row.GetProperty("defDefense").GetInt32(), npc.Simulation.BaseDefense);
        Assert.Equal(row.GetProperty("difficulty").GetSingle(), npc.Simulation.SpawnDifficulty);
        Assert.Equal(row.GetProperty("knockback").GetSingle(), npc.Simulation.KnockBackResist);
        Assert.Equal(row.GetProperty("alpha").GetInt32(), npc.Simulation.Alpha);
        Assert.Equal(row.GetProperty("aiStyle").GetInt32(), definition.AiStyle.Value);
        Assert.Equal(row.GetProperty("noGravity").GetBoolean(), npc.Simulation.NoGravity);
        Assert.Equal(row.GetProperty("noTileCollide").GetBoolean(), npc.Simulation.NoTileCollide);
        Assert.Equal(type == VanillaNpcIds.WaterSphere ? VanillaNpcBehaviorFamily.BurningSphere : VanillaNpcBehaviorFamily.None,
            definition.BehaviorFamily);
    }

    [Fact]
    public void Authority_resamples_progression_and_active_head_without_retroactively_rescaling_children()
    {
        var store = new RuntimeNpcStore();
        var progression = new RuntimeWorldProgressionMutations();
        _ = new ServerRuntimeState(npcs: store, worldProgression: progression,
            worldClock: new RuntimeWorldClock(0, false, default, 0, 1, getGoodWorld: true), expertMode: true);
        var intent = new NpcAiSpawnIntent(VanillaNpcIds.WaterSphere, 1000, 1000, 0, 0, 255) { StartSlot = 10 };
        Assert.True(store.TrySpawnIntent(in intent, out var ordinary));
        Assert.Equal(60, ordinary.Simulation.BaseDamage);
        Assert.True(progression.MarkCompleted(VanillaWorldProgressionId.Hardmode));
        Assert.True(store.TrySpawnIntent(in intent, out var hard));
        Assert.Equal(216, hard.Simulation.BaseDamage);
        Assert.True(progression.MarkCompleted(VanillaWorldProgressionId.Plantera));
        Assert.True(store.TrySpawnIntent(in intent, out var plant));
        Assert.Equal(270, plant.Simulation.BaseDamage);
        Assert.True(store.TrySpawnIntent(new(VanillaNpcIds.SkeletronHead, 1000, 1000, 0, 0, 255) { StartSlot = 5 }, out var head));
        Assert.True(store.TrySpawnIntent(in intent, out var duringHead));
        Assert.Equal(60, duringHead.Simulation.BaseDamage);
        // AnyNPCs observes active storage even when life has reached zero before removal.
        Assert.True(store.TryUpdate(head.Handle, Update(head) with { Simulation = head.Simulation with { Life = 0 } }, out _));
        Assert.True(store.TrySpawnIntent(in intent, out var pendingRemoval));
        Assert.Equal(60, pendingRemoval.Simulation.BaseDamage);
        Assert.True(store.TryDespawn(head.Handle));
        Assert.True(store.TrySpawnIntent(in intent, out var afterHead));
        Assert.Equal(270, afterHead.Simulation.BaseDamage);
        Assert.True(store.TryGet(duringHead.Handle, out var unchanged));
        Assert.Equal(duringHead, unchanged);
    }

    [Fact]
    public void Loaded_world_facts_also_supply_hardmode_and_plantera()
    {
        var store = new RuntimeNpcStore();
        var facts = default(RuntimeTownCommerceWorldFacts1458) with { HardMode = true, DownedPlantera = true };
        _ = new ServerRuntimeState(npcs: store, townCommerceWorldFacts: facts, expertMode: true);
        Assert.True(store.TrySpawnIntent(new(VanillaNpcIds.WaterSphere, 1000, 1000, 0, 0, 255), out var sphere));
        Assert.Equal(180, sphere.Simulation.BaseDamage);
        Assert.Equal(1, sphere.Simulation.LifeMax);
    }

    [Fact]
    public void Resistance_is_preserved_replaced_validated_and_consumed_by_combat()
    {
        var store = new RuntimeNpcStore();
        store.SetVanillaSpawnContextSource(() => new(3, 1, false));
        Assert.True(store.TrySpawnIntent(new(VanillaNpcIds.DarkCaster, 1000, 1000, 0, 0, 255), out var npc));
        Assert.Equal(.48000002f, npc.Simulation.KnockBackResist);
        var omitted = Update(npc) with { Simulation = NpcSimulationState.Initial };
        Assert.True(store.TryUpdate(npc.Handle, in omitted, out npc));
        Assert.Equal(.48000002f, npc.Simulation.KnockBackResist);
        var zero = Update(npc) with { Simulation = npc.Simulation with { KnockBackResist = 0f } };
        Assert.True(store.TryUpdate(npc.Handle, in zero, out npc));
        var request = new NpcDamageRequest(npc.Handle, DamageSource.FromPlayerItem(new(new(0), new(1))),
            10, KnockBack: 10f, HitDirection: 1);
        Assert.True(new RuntimeNpcDamageExecutor(store).TryApply(in request, out _));
        Assert.True(store.TryGet(npc.Handle, out npc));
        Assert.Equal(0f, npc.VelocityX);
        Assert.Equal(0f, npc.VelocityY);
        foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity })
        {
            var bad = Update(npc) with { Simulation = npc.Simulation with { KnockBackResist = invalid } };
            Assert.False(store.TryUpdate(npc.Handle, in bad, out _));
        }
        var changed = Update(npc) with { Type = 1, NetId = 1 };
        Assert.True(store.TryUpdate(npc.Handle, in changed, out var slime));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.BlueSlime, out var definition));
        Assert.Equal(definition.KnockBackResist, slime.Simulation.KnockBackResist);
        Assert.True(store.TryDespawn(slime.Handle));
        Assert.True(store.TrySpawnIntent(new(VanillaNpcIds.DarkCaster, 1000, 1000, 0, 0, 255), out var fresh));
        Assert.Equal(.48000002f, fresh.Simulation.KnockBackResist);
    }

    private static NpcStateUpdate Update(NpcSnapshot npc) => new(npc.Type, npc.NetId, npc.PositionX, npc.PositionY,
        npc.VelocityX, npc.VelocityY, npc.Target, npc.Ai, npc.Simulation);

    private sealed class Capture : INpcStateCommitSink
    {
        public NpcSnapshot Last { get; private set; }
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot) => Last = snapshot;
    }

    private static JsonElement[] Read()
    {
        using var resource = typeof(CasterSpawnTests).Assembly.GetManifestResourceStream("CasterSpawn1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var bytes = new MemoryStream(); gzip.CopyTo(bytes);
        Assert.Equal("312c0fd9130430fca01c6c191025e52146635f72755fa7bfd86f54bf5ecceba0", Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using var json = JsonDocument.Parse(bytes.ToArray());
        return json.RootElement.EnumerateArray().Select(row => row.Clone()).ToArray();
    }
}
