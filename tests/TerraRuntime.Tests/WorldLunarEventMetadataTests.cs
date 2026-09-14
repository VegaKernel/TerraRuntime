using System.IO.Compression;
using System.Security.Cryptography;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldLunarEventMetadataTests
{
    public static TheoryData<int> FlagMasks => new(Enumerable.Range(0, 32));

    [Theory]
    [MemberData(nameof(FlagMasks))]
    public void Official_saved_lunar_flags_survive_metadata_and_prepared_cache(int mask)
    {
        byte[] file = OfficialHeader(mask);
        int[] pointers = new int[VanillaWorldFormat326.SectionCount];
        for (int i = 1; i < pointers.Length; i++) pointers[i] = file.Length + i - 1;
        var envelope = new WorldFileEnvelope(WorldFileFormatPolicy.CurrentVersion, 1, 0, pointers,
            VanillaWorldFrameImportance326.Count, VanillaWorldFrameImportance326.CopyPackedBits());
        Assert.Equal(WorldFileHeaderParseResult.Parsed, WorldFileHeaderParser.TryParse(file, envelope, out var header));
        Assert.NotNull(header);
        var limits = new WorldFileRuntimeMetadataLimits(4096, 16384, 64, 1024, 64, 16384);
        Assert.Equal(WorldFileRuntimeMetadataParseResult.Parsed,
            WorldFileRuntimeMetadataParser.TryParse(file, envelope, header, limits, out var metadata, out int consumed));
        Assert.NotNull(metadata);
        Assert.Equal(file.Length, consumed);
        AssertFlags(metadata, mask);
        Assert.False(metadata.PartyManual);
        Assert.False(metadata.PartyGenuine);

        var tiles = new WorldTileStore(header.Dimensions);
        var world = new WorldFileData(envelope, header, metadata, tiles, [], [], new WorldNpcPersistence([], [], []),
            [], [], [], new WorldBestiaryData([], [], []), new WorldCreativePowersData(false, 0, false, false, 0, false));
        byte[] prepared = RuntimeWorldPreparedStateCodec.Encode(world);
        Assert.True(RuntimeWorldPreparedStateCodec.TryDecode(prepared, tiles, out var cached));
        Assert.NotNull(cached);
        AssertFlags(cached.RuntimeMetadata, mask);
        Assert.False(cached.RuntimeMetadata.PartyManual);
        Assert.False(cached.RuntimeMetadata.PartyGenuine);
    }

    private static void AssertFlags(WorldFileRuntimeMetadata state, int mask)
    {
        Assert.Equal((mask & 1) != 0, state.TowerActiveSolar);
        Assert.Equal((mask & 2) != 0, state.TowerActiveVortex);
        Assert.Equal((mask & 4) != 0, state.TowerActiveNebula);
        Assert.Equal((mask & 8) != 0, state.TowerActiveStardust);
        Assert.Equal((mask & 16) != 0, state.LunarApocalypseIsUp);
    }

    private static byte[] OfficialHeader(int mask)
    {
        // Unmodified TerrariaServer 1.4.5.8 IO.WorldFile.SaveWorldHeader emitted these 32 headers.
        // The fixture world/seed are synthetic; each record is int32 length + original header bytes.
        // It contains no tiles, assets, game source or copied encoder. The neighboring party flags stay false.
        using Stream resource = typeof(WorldLunarEventMetadataTests).Assembly.GetManifestResourceStream("LunarEventHeaders1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var bytes = new MemoryStream();
        gzip.CopyTo(bytes);
        Assert.Equal("7FE5C487851B53011ADE41771E9E98F09951D8B578A0E8CFD7F91204840B2E86",
            Convert.ToHexString(SHA256.HashData(bytes.ToArray())));
        bytes.Position = 0;
        using var reader = new BinaryReader(bytes);
        for (int i = 0; i < 32; i++)
        {
            int length = reader.ReadInt32();
            Assert.InRange(length, 1, 4096);
            byte[] header = reader.ReadBytes(length);
            Assert.Equal(length, header.Length);
            if (i == mask) return header;
        }
        throw new InvalidOperationException("Missing official header fixture.");
    }
}
