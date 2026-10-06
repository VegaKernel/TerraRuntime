using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Npcs;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class NpcSourceDefaultRoster1458Tests
{
    private static JsonDocument Read()
    {
        using var stream = typeof(NpcSourceDefaultRoster1458Tests).Assembly
            .GetManifestResourceStream("NpcSourceDefaultRoster1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    [Fact]
    public void Original_inventory_distinguishes_live_defaults_from_nonphysical_and_nonlive_identities()
    {
        using var facts = Read();
        var rows = facts.RootElement.EnumerateArray().ToArray();
        Assert.Equal(761, rows.Length);
        Assert.Equal(Enumerable.Range(1, 696).Concat(Enumerable.Range(1, 65).Select(n => -n)).Order(),
            rows.Select(row => row.GetProperty("requested").GetInt32()).Order());
        Assert.Equal(new[] { 76, 146, 403, 404, 408 }, rows
            .Where(row => row.GetProperty("lifeMax").GetInt32() == 0)
            .Select(row => row.GetProperty("requested").GetInt32()));
        var placeholder = rows.Single(row => row.GetProperty("requested").GetInt32() == 664);
        Assert.Equal(20, placeholder.GetProperty("lifeMax").GetInt32());
        Assert.Equal(0, placeholder.GetProperty("width").GetInt32());
        Assert.Equal(0, placeholder.GetProperty("height").GetInt32());
    }

    [Fact]
    public void Public_roster_retains_verified_definitions_and_adds_exact_classic_metadata_without_behavior_admission()
    {
        using var facts = Read();
        int positive = 0, signed = 0, definitionOnly = 0;
        foreach (var row in facts.RootElement.EnumerateArray())
        {
            int requested = row.GetProperty("requested").GetInt32();
            var type = new NpcTypeId(row.GetProperty("type").GetInt32());
            bool found = VanillaNpcDefinitionCatalog.TryGet(type, new NpcNetId((short)requested), out var definition);
            if (row.GetProperty("lifeMax").GetInt32() == 0)
            {
                Assert.False(found);
                continue;
            }
            Assert.True(found, $"Source identity {requested} missing");
            if (requested > 0) positive++; else signed++;
            Assert.Equal(row.GetProperty("aiStyle").GetInt32(), definition.AiStyle.Value);
            if (!definition.DefinitionOnly) continue;
            definitionOnly++;
            Assert.Equal(0, definition.BaseWidth);
            Assert.Equal(0, definition.BaseHeight);
            Assert.Equal(VanillaNpcBehaviorFamily.None, definition.BehaviorFamily);
            Assert.Equal(VanillaNpcPhysicsFamily.None, definition.PhysicsFamily);
            Assert.Equal(row.GetProperty("lifeMax").GetInt32(), definition.LifeMax);
            Assert.Equal(row.GetProperty("damage").GetInt32(), definition.Damage);
            Assert.Equal(row.GetProperty("defense").GetInt32(), definition.Defense);
            Assert.Equal(row.GetProperty("scale").GetSingle(), definition.Scale);
            Assert.Equal(row.GetProperty("knockBackResist").GetSingle(), definition.KnockBackResist);
            Assert.Equal(row.GetProperty("alpha").GetInt32(), definition.AlphaAtSpawn);
            Assert.Equal(row.GetProperty("noGravity").GetBoolean(), definition.NoGravityAtSpawn);
            Assert.Equal(row.GetProperty("noTileCollide").GetBoolean(), definition.NoTileCollideAtSpawn);
            Assert.Equal(row.GetProperty("dontTakeDamage").GetBoolean(), definition.DontTakeDamageAtSpawn);
            Assert.Equal(row.GetProperty("hidden").GetBoolean(), definition.HiddenAtSpawn);

            // These are actual post-scale SetDefaults dimensions; raw pre-scale dimensions are not inferred.
            int width = row.GetProperty("width").GetInt32(), height = row.GetProperty("height").GetInt32();
            Assert.Equal(width > 0 && height > 0,
                definition.TryResolveHitbox(definition.Scale, out var body));
            Assert.Equal(width, body.Width);
            Assert.Equal(height, body.Height);
            Assert.False(definition.TryResolveHitbox(definition.Scale + .25f, out _));
            Assert.False(VanillaNpcAiCoverageCatalog.TryGet(type, out var coverage) && coverage.FullVanillaAiParity);
        }
        Assert.Equal(691, positive);
        Assert.Equal(65, signed);
        Assert.Equal(326, definitionOnly);
    }

    [Fact]
    public void Definition_only_marker_prevents_shared_ai_style_from_claiming_an_actor_and_preserves_zero_body()
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(185), out var snowFlinx));
        Assert.True(snowFlinx.DefinitionOnly);
        Assert.Equal(VanillaNpcBehaviorFamily.None, snowFlinx.BehaviorFamily);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(664), out var placeholder));
        Assert.True(placeholder.DefinitionOnly);
        Assert.False(placeholder.TryResolveHitbox(placeholder.Scale, out _));
    }

    [Fact]
    public void Unknown_and_mismatched_wire_identities_remain_rejected()
    {
        Assert.False(VanillaNpcDefinitionCatalog.TryGet(new(185), new(-15), out _));
        Assert.False(VanillaNpcDefinitionCatalog.TryGet(new(697), out _));
        Assert.False(VanillaNpcDefinitionCatalog.TryGet(new(185), new(-66), out _));
    }

    [Fact]
    public void Existing_source_metadata_owners_match_actual_flags_for_new_definition_only_rows()
    {
        using var facts = Read();
        foreach (var row in facts.RootElement.EnumerateArray())
        {
            int type = row.GetProperty("type").GetInt32();
            int requested = row.GetProperty("requested").GetInt32();
            if (!VanillaNpcDefinitionCatalog.TryGet(new(type), new((short)requested), out var definition) ||
                !definition.DefinitionOnly) continue;
            Assert.True(VanillaNpcSourceMetadata1458.TryGet(new(type), out bool boss, out _));
            Assert.Equal(row.GetProperty("boss").GetBoolean(), boss);
            Assert.Equal(row.GetProperty("friendly").GetBoolean(), VanillaNpcChaseability1458.FriendlyAtSpawn(type));
            Assert.Equal(row.GetProperty("immortal").GetBoolean(), VanillaNpcChaseability1458.ImmortalAtSpawn(type));
            Assert.Equal(row.GetProperty("chaseable").GetBoolean(), VanillaNpcChaseability1458.ChaseableAtSpawn(type));
        }
    }
}
