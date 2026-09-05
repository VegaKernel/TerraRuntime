using System.Security.Cryptography;
using System.Text;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldGenerationPassCatalog1458Tests
{
    [Fact]
    public void Catalog_matches_pinned_resolved_registration_sequence()
    {
        string[] names = PassCatalog1458.SourceOrderBeforeSpecialSeedFiltering.ToArray();

        Assert.Equal(109, names.Length);
        Assert.Equal("Terrain", names[0]);
        Assert.Equal("Jungle", names[1]);
        Assert.Equal("Jungle", names[19]);
        Assert.Equal("Dual Dungeons Dither Snake", names[34]);
        Assert.Equal("Spawn Point", names[81]);
        Assert.Equal("Final Cleanup", names[^1]);

        byte[] bytes = Encoding.UTF8.GetBytes(string.Join('\n', names));
        string fingerprint = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        Assert.Equal(PassCatalog1458.ResolvedPassNameSequenceSha256, fingerprint);
        Assert.Equal("1654faeb1831d2c69df8e358664e9152af8ad22c2e3a0c315a772862f4064df5", fingerprint);
    }

    [Fact]
    public void Dual_dungeon_filter_matches_pinned_special_seed_contract()
    {
        string[] disabled = PassCatalog1458.DisabledForDualDungeons.ToArray();

        Assert.Equal(12, disabled.Length);
        Assert.True(PassCatalog1458.IsDisabledForDualDungeons("Generate Ice Biome"));
        Assert.True(PassCatalog1458.IsDisabledForDualDungeons("Jungle"));
        Assert.True(PassCatalog1458.IsDisabledForDualDungeons("Corruption"));
        Assert.True(PassCatalog1458.IsDisabledForDualDungeons("Shimmer"));
        Assert.False(PassCatalog1458.IsDisabledForDualDungeons("Terrain"));
        Assert.False(PassCatalog1458.IsDisabledForDualDungeons("Dungeon"));
    }
}
