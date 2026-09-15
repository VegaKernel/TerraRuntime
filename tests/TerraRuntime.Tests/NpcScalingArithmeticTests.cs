using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class NpcScalingArithmeticTests
{
    private static readonly JsonElement[] Linux = Read("Linux", "3e43fbdc78552a0d955e2c57a32d64e61b8513b98b9f528974145777c617b17f");
    private static readonly JsonElement[] Windows = Read("Windows", "64c8f0150a4fc7e41422794e3ec42d15c8a3388471d2c8f58e3a20a8596d7267");
    public static TheoryData<int> Cases => new(Enumerable.Range(0, Linux.Length + Windows.Length));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Creation_profiles_match_original_platform_arithmetic(int index)
    {
        bool windows = index >= Linux.Length;
        var row = windows ? Windows[index - Linux.Length] : Linux[index];
        var context = new VanillaNpcSpawnContext(row.GetProperty("difficulty").GetSingle(),
            row.GetProperty("players").GetInt32(), row.GetProperty("good").GetBoolean())
        {
            HardMode = row.GetProperty("hard").GetBoolean(),
            DownedPlantera = row.GetProperty("plant").GetBoolean(),
            SkeletronActive = row.GetProperty("headActive").GetBoolean()
        };
        // NewNPC's random type substitution is already reflected in actualType; this gate
        // isolates SetDefaults/ScaleStats arithmetic from the separately verified RNG prefix.
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(row.GetProperty("actualType").GetInt32()), out var definition));
        Assert.True(VanillaNpcSpawnDefaults.TryResolve(in definition, in context, windows, out var actual));
        Assert.Equal(row.GetProperty("lifeMax").GetInt32(), actual.LifeMax);
        Assert.Equal(row.GetProperty("damage").GetInt32(), actual.Damage);
        Assert.Equal(row.GetProperty("defense").GetInt32(), actual.Defense);
        Assert.Equal(row.GetProperty("width").GetInt32(), actual.Hitbox.Width);
        Assert.Equal(row.GetProperty("height").GetInt32(), actual.Hitbox.Height);
        Assert.Equal(row.GetProperty("scale").GetSingle(), actual.Scale);
        if (actual.KnockBackResist is float knockback)
            Assert.Equal(row.GetProperty("knockback").GetSingle(), knockback);
        if (windows == OperatingSystem.IsWindows())
        {
            Assert.True(VanillaNpcSpawnDefaults.TryResolve(in definition, in context, out var host));
            Assert.Equal(actual, host);
        }
    }

    private static JsonElement[] Read(string platform, string hash)
    {
        using var stream = typeof(NpcScalingArithmeticTests).Assembly.GetManifestResourceStream("NpcScaling" + platform + "1458")!;
        using var zip = new GZipStream(stream, CompressionMode.Decompress);
        using var bytes = new MemoryStream(); zip.CopyTo(bytes);
        Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant());
        using var document = JsonDocument.Parse(bytes.ToArray());
        return document.RootElement.EnumerateArray().Select(x => x.Clone()).ToArray();
    }
}
