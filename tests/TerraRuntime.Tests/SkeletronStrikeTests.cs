using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class SkeletronStrikeTests
{
    private static readonly JsonElement[] Rows = Read("SkeletronStrike1458", "51b82b95742659540f7f41b4536e591e8640e1d2f5c89b31a391c0f341751c47");
    public static TheoryData<int> Cases => new(Enumerable.Range(0, Rows.Length));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Authoritative_strike_matches_original(int index)
    {
        CheckStrike(Rows[index], 1000000);
    }

    private static void CheckStrike(JsonElement row, int lifeBefore)
    {
        int type = row.GetProperty("type").GetInt32();
        int marker = row.GetProperty("marker").GetInt32();
        var store = new RuntimeNpcStore();
        var state = new NpcStateUpdate(type, (short)type, 1000, 1000, 0, 0, 0,
            new(0, 0, 0, marker == 1 ? 1 : marker == 3 ? 2 : 0),
            NpcSimulationState.Initial with { Life = lifeBefore, LifeMax = lifeBefore,
                DefenseOverride = row.GetProperty("defense").GetInt32(),
                LocalAi = new(0, 0, 0, marker == 2 ? 1 : marker == 3 ? 2 : 0) });
        Assert.True(store.TrySpawn(10, in state, out var npc));
        var request = new NpcDamageRequest(npc.Handle,
            DamageSource.FromPlayerItem(new(new(0), new(1))), row.GetProperty("damage").GetInt32(),
            Critical: row.GetProperty("critical").GetBoolean(), HitDirection: 1);
        Assert.True(new RuntimeNpcDamageExecutor(store).TryApply(in request, out var result));
        Assert.Equal(row.GetProperty("resolved").GetInt32(), result.ResolvedDamage);
        Assert.Equal(row.GetProperty("life").GetInt32(), result.LifeAfter);
        Assert.True(store.TryGet(npc.Handle, out var after));
        Assert.Equal(result.LifeAfter, after.Simulation.Life);
        Assert.Equal(row.GetProperty("justHit").GetBoolean(), after.Simulation.JustHit);
        Assert.Equal(row.GetProperty("active").GetBoolean(), after.IsActive);
        Assert.Equal(2ul, after.Revision.Value);
    }

    private static readonly JsonElement[] HighRows = Read("SkeletronHighStrike1458", "4a228423de50068fcb154536c861fcb5321bc4997452a05c3046db268a30f5aa");
    public static TheoryData<int> HighCases => new(Enumerable.Range(0, HighRows.Length));

    [Theory]
    [MemberData(nameof(HighCases))]
    public void Large_authoritative_strike_matches_original_double_defense_math(int index) =>
        CheckStrike(HighRows[index], 500000000);

    private static readonly JsonElement[] Markers = Read("SkeletronRedHatMarker1458", "b08d7a43af9c5955cfa656e36cbb82a65bf974d16d172402acecaac4915eb91b");
    public static TheoryData<int> MarkerCases => new(Enumerable.Range(0, Markers.Length));

    [Theory]
    [MemberData(nameof(MarkerCases))]
    public void Shared_marker_matches_original_predicate_for_all_four_affected_types(int index)
    {
        var row = Markers[index];
        Assert.Equal(row.GetProperty("enabled").GetBoolean(), VanillaSkeletronCombat.HasRedHatAdjustments(
            new NpcTypeId(row.GetProperty("type").GetInt32()), new(0, 0, 0, row.GetProperty("a").GetSingle()),
            new(0, 0, 0, row.GetProperty("l").GetSingle())));
    }

    private static JsonElement[] Read(string name, string hash)
    {
        using var resource = typeof(SkeletronStrikeTests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var bytes = new MemoryStream(); gzip.CopyTo(bytes);
        Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using var json = JsonDocument.Parse(bytes.ToArray());
        return json.RootElement.EnumerateArray().Select(row => row.Clone()).ToArray();
    }
}
