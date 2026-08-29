using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldSeedPersistenceTests
{
    [Fact]
    public void Fresh_world_metadata_roundtrips_special_and_direct_secret_seed_flags()
    {
        const string seedText =
            "getfixedboi|What a horrible night to have a curse|Purify this|Royale with cheese|" +
            "Double daring dangers|Electric Boogaloo|Calm before the storm|Hocus pocus|Jingle all the way";
        VanillaWorldSeedProfile1458 profile = VanillaWorldSeedProfile1458.Parse(seedText, fallbackSeed: 0);
        WorldFileHeader header = VanillaFreshWorldHeader326.Create(
            "Secrets",
            seedText,
            widthTiles: 128,
            heightTiles: 96,
            Guid.Parse("2f9341ea-6c04-4cd5-a01d-0ac9bfa54793"),
            worldId: 1458);
        var generation = new RuntimeWorldGenerationMetadataSnapshot(
            new WorldGenerationPoint(64, 28),
            new WorldGenerationPoint(12, 28),
            new WorldGenerationLayers(30d, 52d))
        {
            VanillaSeedProfile = profile
        };
        var source = new WorldFileFreshRuntimeMetadata326(
            generation,
            GameMode: 0,
            Crimson: true,
            CreationTimeBinary: 123,
            LastPlayedBinary: 456);

        WorldFileRuntimeMetadata metadata = EncodeAndParse(header, in source);

        Assert.True(metadata.DrunkWorld);
        Assert.True(metadata.GetGoodWorld);
        Assert.True(metadata.TenthAnniversaryWorld);
        Assert.True(metadata.DontStarveWorld);
        Assert.True(metadata.NotTheBeesWorld);
        Assert.True(metadata.RemixWorld);
        Assert.True(metadata.NoTrapsWorld);
        Assert.True(metadata.ZenithWorld);
        Assert.False(metadata.SkyblockWorld);
        Assert.True(metadata.VampireSeed);
        Assert.True(metadata.InfectedSeed);
        Assert.True(metadata.TeamBasedSpawnsSeed);
        Assert.True(metadata.DualDungeonsSeed);
        Assert.True(metadata.MoreLightningSeed);
        Assert.True(metadata.NoLightningSeed);
        Assert.True(metadata.ForceHalloweenForever);
        Assert.True(metadata.ForceXMasForever);
        Assert.True(metadata.Crimson);
    }

    [Fact]
    public void Skyblock_special_seed_roundtrips_without_enabling_unrelated_secret_flags()
    {
        VanillaWorldSeedProfile1458 profile = VanillaWorldSeedProfile1458.Parse("skyblock", fallbackSeed: 0);
        WorldFileHeader header = VanillaFreshWorldHeader326.Create(
            "Skyblock",
            "skyblock",
            widthTiles: 128,
            heightTiles: 96,
            Guid.Parse("87c35b20-6d1e-4738-9875-83e1439f453c"),
            worldId: 1459);
        var generation = new RuntimeWorldGenerationMetadataSnapshot(
            new WorldGenerationPoint(64, 30),
            new WorldGenerationPoint(16, 30),
            new WorldGenerationLayers(32d, 60d))
        {
            VanillaSeedProfile = profile
        };
        var source = new WorldFileFreshRuntimeMetadata326(
            generation,
            GameMode: 0,
            Crimson: false,
            CreationTimeBinary: 123,
            LastPlayedBinary: 456);

        WorldFileRuntimeMetadata metadata = EncodeAndParse(header, in source);

        Assert.True(metadata.SkyblockWorld);
        Assert.False(metadata.ZenithWorld);
        Assert.False(metadata.VampireSeed);
        Assert.False(metadata.DualDungeonsSeed);
    }

    private static WorldFileRuntimeMetadata EncodeAndParse(
        WorldFileHeader header,
        in WorldFileFreshRuntimeMetadata326 source)
    {
        using var section = new MemoryStream();
        Assert.Equal(
            WorldFileHeaderPrefixEncodeResult.Encoded,
            WorldFileHeaderPrefixEncoder.TryEncode(header, section, out long prefixBytes));
        Assert.Equal(
            WorldFileFreshRuntimeMetadata326EncodeResult.Encoded,
            WorldFileFreshRuntimeMetadata326Encoder.TryEncode(header, in source, section, out long metadataBytes));
        Assert.Equal(section.Length, prefixBytes + metadataBytes);

        byte[] file = section.ToArray();
        int[] pointers = new int[VanillaWorldFormat326.SectionCount];
        pointers[0] = 0;
        pointers[1] = file.Length;
        for (int i = 2; i < pointers.Length; i++)
            pointers[i] = file.Length + i - 1;
        var envelope = new WorldFileEnvelope(
            WorldFileFormatPolicy.CurrentVersion,
            revision: 1,
            favoriteFlags: 0,
            pointers,
            VanillaWorldFrameImportance326.Count,
            VanillaWorldFrameImportance326.CopyPackedBits());
        var limits = new WorldFileRuntimeMetadataLimits(
            MaxStringBytes: 4096,
            MaxTotalStringBytes: 16384,
            MaxAnglerNames: 64,
            MaxBannerEntries: 1024,
            MaxPartyNpcEntries: 64,
            MaxManifestBytes: 16384);

        WorldFileRuntimeMetadataParseResult result = WorldFileRuntimeMetadataParser.TryParse(
            file,
            envelope,
            header,
            limits,
            out WorldFileRuntimeMetadata? metadata,
            out int bytesConsumed);

        Assert.Equal(WorldFileRuntimeMetadataParseResult.Parsed, result);
        Assert.NotNull(metadata);
        Assert.Equal(file.Length, bytesConsumed);
        return metadata;
    }
}
