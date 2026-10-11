using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class VoidStoragePhaseRole1458Tests
{
    [Fact]
    public void Original_void_flags_writers_and_equipment_storage_role_match()
    {
        int roleRows = 0;
        foreach (string capture in new[] { "LinuxFirst", "LinuxRepeat", "WindowsFirst", "WindowsRepeat" })
        {
            using JsonDocument metadata = Read("VoidStorageMetadata" + capture + "1458");
            JsonElement root = metadata.RootElement;
            var writers = root.GetProperty("writers").EnumerateArray()
                .ToDictionary(row => row.GetProperty("id").GetInt32());
            JsonElement works = root.GetProperty("works");
            Assert.Equal(6196, works.GetArrayLength());
            int positiveNeutral = 0;
            int activeWriters = 0;
            for (int id = 0; id < works.GetArrayLength(); id++)
            {
                bool information = false;
                bool mechanical = false;
                if (writers.TryGetValue(id, out JsonElement writer))
                {
                    information = HasEffect(writer.GetProperty("info"));
                    mechanical = HasEffect(writer.GetProperty("mechanical"));
                }
                bool sourceWorks = works[id].GetBoolean();
                bool sourceNeutral = !sourceWorks || (!information && !mechanical);
                Assert.True(VanillaVoidStorageCatalog1458.TryGet(new(id), out var actual));
                Assert.Equal(new VanillaVoidStorageFacts1458(sourceWorks, information, mechanical), actual);
                Assert.Equal(sourceNeutral, actual.IsPhaseNeutral);
                Assert.Equal(sourceNeutral, VanillaVoidStorageCatalog1458.IsPhaseNeutral(new(id)));
                if (id > 0 && sourceNeutral) positiveNeutral++;
                if (!sourceNeutral) activeWriters++;
            }
            Assert.Equal(6153, positiveNeutral);
            Assert.Equal(42, activeWriters);

            using JsonDocument roles = Read("VoidStorageRole" + capture + "1458");
            foreach (JsonElement row in roles.RootElement.EnumerateArray())
            {
                JsonElement before = row.GetProperty("before");
                JsonElement after = row.GetProperty("after");
                string mode = row.GetProperty("mode").GetString()!;
                int id = row.GetProperty("id").GetInt32();
                Assert.True(mode is "open-bank4" or "closed-bank4" or "bag-cursor" or "bag-bank4" or
                    "closed-bag-main" or "other-bank1" or "other-bank2" or "other-bank3" or
                    "prefixed-stored" or "bag-stack0" or "bank-stack0" or "favorite-stored" or
                    "local-open" or "all-writers", "Every original role mode must have an explicit expectation.");
                Assert.Equal(row.GetProperty("beforeRandom").GetRawText(), row.GetProperty("afterRandom").GetRawText());
                Assert.Equal(row.GetProperty("beforeNext").GetInt32(), row.GetProperty("afterNext").GetInt32());
                Assert.Empty(row.GetProperty("frames").EnumerateArray());
                foreach (string field in new[] { "armor", "loadouts", "banks", "position", "velocity", "combat" })
                    Assert.Equal(before.GetProperty(field).GetRawText(), after.GetProperty(field).GetRawText());
                JsonElement beforeState = before.GetProperty("state");
                JsonElement afterState = after.GetProperty("state");
                // UpdateEquips is isolated after ResetEffects; this is not a complete player update.
                foreach (string field in new[] { "inventory", "buffType", "buffTime", "itemTime", "itemTimeMax", "itemAnimation", "itemAnimationMax" })
                    Assert.Equal(beforeState.GetProperty(field).GetRawText(), afterState.GetProperty(field).GetRawText());
                Assert.Equal(59, afterState.GetProperty("inventory").GetArrayLength());
                Assert.Equal(44, afterState.GetProperty("buffType").GetArrayLength());
                Assert.Equal(4, after.GetProperty("banks").GetArrayLength());
                foreach (JsonElement bank in after.GetProperty("banks").EnumerateArray())
                    Assert.Equal(40, bank.GetArrayLength());

                if (mode is "open-bank4" or "closed-bank4")
                {
                    Assert.True(VanillaVoidStorageCatalog1458.TryGet(new(id), out var facts));
                    int[] expected = Integers(before.GetProperty("effects"));
                    if (mode == "open-bank4" && facts.WorksInVoidBag && writers.TryGetValue(id, out JsonElement writer))
                    {
                        int[] info = Integers(writer.GetProperty("info"));
                        int[] mechanical = Integers(writer.GetProperty("mechanical"));
                        for (int field = 0; field < expected.Length; field++)
                            expected[field] = Math.Max(expected[field], Math.Max(info[field], mechanical[field]));
                    }
                    Assert.Equal(expected, Integers(after.GetProperty("effects")));
                    if (facts.IsPhaseNeutral || mode == "closed-bank4")
                        Assert.Equal(before.GetProperty("effects").GetRawText(), after.GetProperty("effects").GetRawText());
                }
                else if (mode is "bag-cursor" or "bag-bank4" or "closed-bag-main" or "other-bank1" or "other-bank2" or "other-bank3" or "prefixed-stored")
                    Assert.Equal(before.GetProperty("effects").GetRawText(), after.GetProperty("effects").GetRawText());
                else if (mode is "bag-stack0" or "bank-stack0" or "favorite-stored" or "local-open")
                {
                    string[] fields = root.GetProperty("fields").EnumerateArray().Select(value => value.GetString()!).ToArray();
                    Assert.Equal(1, after.GetProperty("effects")[Array.IndexOf(fields, "oldStyleParkour")].GetInt32());
                }
                else if (mode == "all-writers")
                {
                    // Read actual final forty slots: the configured insertion of44 IDs
                    // overwrites four slots. Do not union an abstract list of44 writers.
                    int[] expected = Integers(before.GetProperty("effects"));
                    bool open = beforeState.GetProperty("inventory").EnumerateArray().Take(58)
                        .Any(item => item.GetProperty("type").GetInt32() == 4131);
                    if (open)
                    {
                        foreach (JsonElement item in before.GetProperty("banks")[3].EnumerateArray())
                        {
                            int storedType = item.GetProperty("type").GetInt32();
                            if (!works[storedType].GetBoolean() || !writers.TryGetValue(storedType, out JsonElement writer))
                                continue;
                            int[] info = Integers(writer.GetProperty("info"));
                            int[] mechanical = Integers(writer.GetProperty("mechanical"));
                            for (int field = 0; field < expected.Length; field++)
                                expected[field] = Math.Max(expected[field], Math.Max(info[field], mechanical[field]));
                        }
                    }
                    Assert.Equal(expected, Integers(after.GetProperty("effects")));
                }
                roleRows++;
            }
        }
        Assert.Equal(272, roleRows);
        Assert.True(VanillaVoidStorageCatalog1458.IsPhaseNeutral(new(0)));
        Assert.True(VanillaVoidStorageCatalog1458.IsPhaseNeutral(new(4346)));
        Assert.True(VanillaVoidStorageCatalog1458.IsPhaseNeutral(new(5095)));
        Assert.False(VanillaVoidStorageCatalog1458.IsPhaseNeutral(new(6190)));
        Assert.False(VanillaVoidStorageCatalog1458.TryGet(new(65535), out _));
        Assert.False(VanillaVoidStorageCatalog1458.TryGet(new(6196), out _));
        Assert.True(VanillaVoidStorageCatalog1458.OpensVoidStorage(new(4131)));
        Assert.False(VanillaVoidStorageCatalog1458.OpensVoidStorage(new(5325)));
    }

    private static bool HasEffect(JsonElement values) => values.EnumerateArray().Any(value => value.GetInt32() != 0);
    private static int[] Integers(JsonElement values) => values.EnumerateArray().Select(value => value.GetInt32()).ToArray();

    private static JsonDocument Read(string logicalName)
    {
        using Stream stream = typeof(VoidStoragePhaseRole1458Tests).Assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException("Missing numeric source fixture " + logicalName);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
}
