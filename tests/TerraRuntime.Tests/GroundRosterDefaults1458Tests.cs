using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Npcs;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class GroundRosterDefaults1458Tests
{
    private static JsonDocument Read(bool windows)
    {
        using var stream = typeof(GroundRosterDefaults1458Tests).Assembly.GetManifestResourceStream(
            windows ? "GroundRosterWindows1458" : "GroundRosterLinux1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    [Fact]
    public void Complete_official_roster_has_real_classic_definitions_without_claiming_autonomous_parity()
    {
        using var data = Read(OperatingSystem.IsWindows());
        Assert.Equal("Terraria.Main.Initialize_TileAndNPCData1",
            data.RootElement.GetProperty("FrozenInitializeCall").GetString());
        Assert.Equal(92, data.RootElement.GetProperty("catchable").GetArrayLength());
        int fighters = 0, variants = 0, town = 0, lifecycle = 0, creationOnly = 0;
        foreach (var row in data.RootElement.GetProperty("classic").EnumerateArray())
        {
            int net = row.GetProperty("requested").GetInt32(), type = row.GetProperty("type").GetInt32();
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(type), new((short)net), out var definition));
            Assert.False(definition.DefinitionOnly);
            if (definition.SupportsRuntimeLifecycle) lifecycle++; else creationOnly++;
            Assert.Equal(!definition.CreationDefaultsOnly, definition.SupportsRuntimeLifecycle);
            if (net < 0) Assert.False(definition.CreationDefaultsOnly);
            Assert.True(definition.BaseWidth > 0 && definition.BaseHeight > 0);
            Assert.Equal(row.GetProperty("width").GetInt32(), definition.Width);
            Assert.Equal(row.GetProperty("height").GetInt32(), definition.Height);
            Assert.Equal(row.GetProperty("lifeMax").GetInt32(), definition.LifeMax);
            Assert.Equal(row.GetProperty("damage").GetInt32(), definition.Damage);
            Assert.Equal(row.GetProperty("defense").GetInt32(), definition.Defense);
            Assert.Equal(row.GetProperty("scale").GetSingle(), definition.Scale);
            Assert.Equal(row.GetProperty("knockBackResist").GetSingle(), definition.KnockBackResist);
            Assert.Equal(row.GetProperty("noGravity").GetBoolean(), definition.NoGravityAtSpawn);
            Assert.Equal(row.GetProperty("noTileCollide").GetBoolean(), definition.NoTileCollideAtSpawn);
            Assert.Equal(row.GetProperty("alpha").GetInt32(), definition.AlphaAtSpawn);
            Assert.Equal(row.GetProperty("dontTakeDamage").GetBoolean(), definition.DontTakeDamageAtSpawn);
            Assert.Equal(row.GetProperty("hidden").GetBoolean(), definition.HiddenAtSpawn);
            Assert.Equal(row.GetProperty("friendly").GetBoolean(), VanillaNpcChaseability1458.FriendlyAtSpawn(type));
            Assert.Equal(row.GetProperty("chaseable").GetBoolean(), VanillaNpcChaseability1458.ChaseableAtSpawn(type));
            Assert.Equal(row.GetProperty("immortal").GetBoolean(), VanillaNpcChaseability1458.ImmortalAtSpawn(type));
            Assert.True(VanillaNpcMoneyDefaults1458.TryResolve(new(type), new((short)net), 1f, out float money));
            Assert.Equal(row.GetProperty("value").GetSingle(), money);
            Assert.False(VanillaNpcAiCoverageCatalog.TryGet(new(type), out var coverage) && coverage.FullVanillaAiParity);
            if (net < 0) variants++; else if (definition.AiStyle.Value == 3) fighters++; else town++;
        }
        Assert.Equal(191, fighters);
        Assert.Equal(27, variants);
        Assert.Equal(82, town);
        Assert.Equal(270, lifecycle);
        Assert.Equal(30, creationOnly);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(624), out var legacyGnome) &&
            legacyGnome.SupportsRuntimeLifecycle);
        Assert.False(VanillaNpcGroundRoster1458.TryGet(new(3), new(-13), out _));
        Assert.False(VanillaNpcGroundRoster1458.TryGet(new(3), new(21), out _));
        Assert.False(VanillaNpcGroundRoster1458.TryGet(new(74), new(74), out _));
    }

    [Fact]
    public void Transformation_admission_is_exactly_seven_identities_and_does_not_count_existing_endpoints()
    {
        int[] expected = [198, 230, 532, 534, 586, 593, 687];
        Assert.Equal(expected, Enumerable.Range(1, 755)
            .Where(type => VanillaNpcTransformation1458.IsNewDefinition(new(type))).ToArray());
        Assert.Equal(new[] { 230, 593, 687 }, VanillaNpcTransformation1458.GroundCritterTypes
            .ToArray().Select(type => type.Value).ToArray());
        foreach (int type in expected)
        {
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(type), out var definition));
            Assert.True(definition.SupportsRuntimeLifecycle);
            Assert.False(definition.CreationDefaultsOnly);
            Assert.Equal(type is 198 or 532 or 534 or 586 ? 3 : 7, definition.AiStyle.Value);
            Assert.True(VanillaNpcAiCoverageCatalog.TryGet(new(type), out var coverage));
            Assert.False(coverage.FullVanillaAiParity);
        }
        foreach (int type in new[] { 55, 57, 199, 441, 465, 592, 683 })
            Assert.False(VanillaNpcTransformation1458.IsNewDefinition(new(type)));
    }

    [Fact]
    public void All_original_linux_and_windows_default_contexts_match_independent_numeric_expectations()
    {
        // Original unmodified Linux executable on CoreCLR and Windows x86 executable on Framework4.8:
        // each captures 300 identities Г— 32 world-flag profiles Г— 2 player counts Г— 8 strengths,
        // after invoking original Main.Initialize_TileAndNPCData1 (including its npcCatchable table).
        // No runtime encoder/catalog was used to produce expected after-states.
        foreach (bool windows in new[] { false, true })
        {
            using var data = Read(windows);
            var types = data.RootElement.GetProperty("classic").EnumerateArray().ToDictionary(
                r => r.GetProperty("requested").GetInt32(), r => r.GetProperty("type").GetInt32());
            int count = 0;
            foreach (var row in data.RootElement.GetProperty("contexts").EnumerateArray())
            {
                int net = row[0].GetInt32(), mask = row[1].GetInt32();
                var context = new VanillaNpcSpawnContext(row[3].GetSingle(), row[2].GetInt32(), (mask & 2) != 0)
                {
                    HardMode = (mask & 1) != 0,
                    TenthAnniversaryWorld = (mask & 4) != 0,
                    RemixWorld = (mask & 8) != 0,
                    DownedPlantera = (mask & 16) != 0
                };
                Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(types[net]), new((short)net), out var definition));
                Assert.True(VanillaNpcSpawnDefaults.TryResolve(in definition, in context, windows, out var actual));
                Assert.Equal(row[4].GetInt32(), actual.Hitbox.Width);
                Assert.Equal(row[5].GetInt32(), actual.Hitbox.Height);
                Assert.Equal(row[6].GetSingle(), actual.Scale);
                Assert.Equal(row[7].GetInt32(), actual.LifeMax);
                Assert.Equal(row[8].GetInt32(), actual.Damage);
                Assert.Equal(row[9].GetInt32(), actual.Defense);
                Assert.Equal(row[10].GetSingle(), actual.KnockBackResist);
                Assert.Equal(row[11].GetSingle(), actual.VerifiedMoneyValue);
                Assert.Equal(row[12].GetInt32(), definition.AlphaAtSpawn);
                Assert.Equal(row[13].GetBoolean(), definition.NoGravityAtSpawn);
                Assert.Equal(row[14].GetBoolean(), definition.NoTileCollideAtSpawn);
                Assert.Equal(row[15].GetBoolean(), definition.DontTakeDamageAtSpawn);
                Assert.Equal(row[16].GetBoolean(), definition.HiddenAtSpawn);
                Assert.Equal(row[17].GetSingle(), actual.Difficulty);
                count++;
            }
            Assert.Equal(153600, count);
        }
    }
}
