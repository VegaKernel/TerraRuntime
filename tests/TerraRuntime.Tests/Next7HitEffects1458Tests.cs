using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class Next7HitEffects1458Tests
{
    public static IEnumerable<object[]> OriginalCases()
    {
        using var stream = typeof(Next7HitEffects1458Tests).Assembly.GetManifestResourceStream("Next7HitEffectsLinux1458.v1")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.GetProperty("cases").EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalCases))]
    public void Actual_dedicated_HitEffect_preserves_full_source_random_state(string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        int type = row.GetProperty("type").GetInt32();
        bool lethal = row.GetProperty("lethal").GetBoolean();
        var original = row.GetProperty("before").GetProperty("npc");
        var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        var npcs = new RuntimeNpcStore(); var items = new RuntimeWorldItemStore();
        var input = new NpcStateUpdate(type, checked((short)type), 640.75f, 480.25f, 0, 0, 255, default,
            NpcSimulationState.Initial with { Life = original.GetProperty("life").GetInt32(),
                LifeMax = original.GetProperty("lifeMax").GetInt32() });
        Assert.True(npcs.TrySpawn(0, in input, out var npc));
        var pipeline = new RuntimeNpcNetworkCombatPipeline(npcs, items, new EmptyPlayers(),
            new PlayerAuthority(null, null), () => 0, null, new(items), null, null, new(), false, false,
            lootRandom: random, npcSpecificLowTiles: () => false);
        GoldCritterHitEffect1458Tests.AssertRandom(row.GetProperty("before").GetProperty("rng"), random);
        string method = lethal ? "ExecuteNpcDeathHitEffects" : "ExecuteNpcNonlethalHitEffects";
        typeof(RuntimeNpcNetworkCombatPipeline).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(pipeline, lethal ? [npc] : [npc, 1]);
        GoldCritterHitEffect1458Tests.AssertRandom(row.GetProperty("after").GetProperty("rng"), random);
        Assert.True(npcs.TryGet(npc.Handle, out var retained)); Assert.Equal(npc, retained);
        Assert.Equal(0, items.ActiveCount);
    }

    private sealed class EmptyPlayers : IRuntimePlayerSlotSnapshotLookup
    {
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        { snapshot = default; return false; }
    }
}
