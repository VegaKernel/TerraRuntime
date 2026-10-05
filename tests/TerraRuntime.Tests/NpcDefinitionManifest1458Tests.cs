using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class NpcDefinitionManifest1458Tests
{
    // Independent classic-world SetDefaults calls on the official 1.4.5.8 dedicated assembly.
    // The capture contains source facts only, never runtime admission decisions.
    private static JsonDocument Read()
    {
        using var stream = typeof(NpcDefinitionManifest1458Tests).Assembly
            .GetManifestResourceStream("NpcDefinitionManifest1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    [Fact]
    public void Source_inventory_covers_the_complete_positive_and_signed_identity_ranges()
    {
        using var facts = Read();
        var rows = facts.RootElement.EnumerateArray().ToArray();
        Assert.Equal(761, rows.Length);
        Assert.Equal(Enumerable.Range(1, 696).Concat(Enumerable.Range(1, 65).Select(n => -n)).Order(),
            rows.Select(row => row.GetProperty("requested").GetInt32()).Order());
        Assert.Equal(new[] { 76, 146, 403, 404, 408 }, rows
            .Where(row => row.GetProperty("requested").GetInt32() > 0 && row.GetProperty("lifeMax").GetInt32() == 0)
            .Select(row => row.GetProperty("requested").GetInt32()));
        Assert.Equal(127, rows.Single(row => row.GetProperty("requested").GetInt32() == 696)
            .GetProperty("aiStyle").GetInt32());
    }

    [Fact]
    public void Every_admitted_positive_identity_has_the_original_source_ai_style()
    {
        using var facts = Read();
        foreach (var row in facts.RootElement.EnumerateArray())
        {
            int requested = row.GetProperty("requested").GetInt32();
            if (requested <= 0 || !VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(requested), out var definition))
                continue;
            Assert.Equal(requested, row.GetProperty("type").GetInt32());
            Assert.Equal(row.GetProperty("aiStyle").GetInt32(), definition.AiStyle.Value);
            Assert.True(row.GetProperty("lifeMax").GetInt32() > 0);
        }
    }

    [Fact]
    public void Every_admitted_signed_identity_keeps_original_classic_variant_stats()
    {
        using var facts = Read();
        foreach (var row in facts.RootElement.EnumerateArray())
        {
            int requested = row.GetProperty("requested").GetInt32();
            if (requested >= 0) continue;
            var type = new NpcTypeId(row.GetProperty("type").GetInt32());
            if (!VanillaNpcDefinitionCatalog.TryGet(type, new NpcNetId((short)requested), out var definition))
                continue;
            Assert.Equal(requested, row.GetProperty("netId").GetInt32());
            Assert.Equal(row.GetProperty("aiStyle").GetInt32(), definition.AiStyle.Value);
            Assert.Equal(row.GetProperty("lifeMax").GetInt32(), definition.LifeMax);
            Assert.Equal(row.GetProperty("damage").GetInt32(), definition.Damage);
            Assert.Equal(row.GetProperty("defense").GetInt32(), definition.Defense);
            Assert.Equal(row.GetProperty("scale").GetSingle(), definition.Scale, 5);
            Assert.Equal(row.GetProperty("knockBackResist").GetSingle(), definition.KnockBackResist, 5);
        }
    }
}
