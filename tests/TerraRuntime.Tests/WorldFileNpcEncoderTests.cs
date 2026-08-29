using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileNpcEncoderTests
{
    private static readonly WorldFileNpcDecodeOptions Options = new(
        MaxShimmeredTownNpcIndices: 16,
        MaxShimmerIndexExclusive: 128,
        MaxTownNpcs: 32,
        MaxPersistentNpcs: 32,
        MaxNameBytesPerTownNpc: 1024,
        MaxTotalNameBytes: 16 * 1024);

    [Fact]
    public void Roundtrips_npc_persistence_through_current_decoder()
    {
        var source = new WorldNpcPersistence(
            [1, 7],
            [
                new WorldTownNpc(17, "Guide", 100.5f, 200.25f, false, 6, 7, null, false),
                new WorldTownNpc(18, "βeta", -12.5f, 9.75f, true, 0, 0, 3, true)
            ],
            [
                new WorldPersistentNpc(42, 1.5f, 2.5f),
                new WorldPersistentNpc(43, -3.25f, 4.75f)
            ]);

        using var stream = new MemoryStream();
        Assert.Equal(
            WorldFileNpcEncodeResult.Encoded,
            WorldFileNpcEncoder.TryEncode(source, Options, stream, out long bytesWritten));
        Assert.Equal(stream.Length, bytesWritten);

        byte[] section = stream.ToArray();
        var envelope = new WorldFileEnvelope(
            WorldFileFormatPolicy.CurrentVersion,
            revision: 1,
            favoriteFlags: 0,
            sectionOffsets: [0, 0, 0, 0, 0, section.Length],
            frameImportanceCount: VanillaWorldFormat326.TileTypeCount,
            frameImportanceBits: new byte[(VanillaWorldFormat326.TileTypeCount + 7) >> 3]);

        Assert.Equal(
            WorldFileNpcDecodeResult.Decoded,
            WorldFileNpcDecoder.TryDecode(
                section,
                envelope,
                Options,
                out WorldNpcPersistence? decoded,
                out int consumed));

        Assert.Equal(section.Length, consumed);
        Assert.NotNull(decoded);
        Assert.Equal(source.ShimmeredTownNpcIndices, decoded!.ShimmeredTownNpcIndices);
        Assert.Equal(source.TownNpcs, decoded.TownNpcs);
        Assert.Equal(source.PersistentNpcs, decoded.PersistentNpcs);
    }

    [Fact]
    public void Rejects_nonfinite_positions_before_writing()
    {
        var source = new WorldNpcPersistence(
            [],
            [new WorldTownNpc(17, "Guide", float.NaN, 2f, false, 0, 0, null, false)],
            []);
        using var stream = new MemoryStream();

        Assert.Equal(
            WorldFileNpcEncodeResult.NonFinitePosition,
            WorldFileNpcEncoder.TryEncode(source, Options, stream, out long bytesWritten));
        Assert.Equal(0, bytesWritten);
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void Rejects_shimmer_indices_and_name_budgets_before_writing()
    {
        using var shimmerStream = new MemoryStream();
        var badShimmer = new WorldNpcPersistence([128], [], []);
        Assert.Equal(
            WorldFileNpcEncodeResult.InvalidShimmerIndex,
            WorldFileNpcEncoder.TryEncode(badShimmer, Options, shimmerStream, out long shimmerBytes));
        Assert.Equal(0, shimmerBytes);
        Assert.Equal(0, shimmerStream.Length);

        using var nameStream = new MemoryStream();
        WorldFileNpcDecodeOptions tinyNameBudget = Options with { MaxNameBytesPerTownNpc = 4 };
        var badName = new WorldNpcPersistence(
            [],
            [new WorldTownNpc(17, "Guide", 1f, 2f, false, 0, 0, null, false)],
            []);
        Assert.Equal(
            WorldFileNpcEncodeResult.NameBudgetExceeded,
            WorldFileNpcEncoder.TryEncode(badName, tinyNameBudget, nameStream, out long nameBytes));
        Assert.Equal(0, nameBytes);
        Assert.Equal(0, nameStream.Length);
    }
}
