using System.Buffers.Binary;
using System.Text;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileCoreLoaderTests
{
    private const int EnvelopeEnd = 167;
    private const int HeaderEnd = 240;

    [Fact]
    public void Loads_verified_header_and_complete_tile_store_atomically()
    {
        byte[] file = CreateCurrentCoreWorld([0x40, 0x02, 0x40, 0x02]);

        WorldFileCoreLoadDiagnostic diagnostic = WorldFileCoreLoader.TryLoad(file, maxTileCount: 6, out WorldFileCore? world);

        Assert.Equal(WorldFileCoreLoadResult.Loaded, diagnostic.Result);
        Assert.Equal(WorldFileEnvelopeParseResult.Parsed, diagnostic.EnvelopeResult);
        Assert.Equal(WorldFileHeaderParseResult.Parsed, diagnostic.HeaderResult);
        Assert.Equal(WorldFileTileDecodeResult.Decoded, diagnostic.TileResult);
        Assert.NotNull(world);
        Assert.Equal("core", world.Header.Name);
        Assert.Equal(2, world.Header.Dimensions.WidthTiles);
        Assert.Equal(3, world.Header.Dimensions.HeightTiles);
        Assert.Equal(6, world.Tiles.Count);
        Assert.False(world.Tiles.Get(0, 0).IsActive);
        Assert.False(world.Tiles.Get(1, 2).IsActive);
    }

    [Fact]
    public void Rejects_tile_budget_before_allocating_authoritative_store()
    {
        byte[] file = CreateCurrentCoreWorld([0x40, 0x02, 0x40, 0x02]);

        WorldFileCoreLoadDiagnostic diagnostic = WorldFileCoreLoader.TryLoad(file, maxTileCount: 5, out WorldFileCore? world);

        Assert.Equal(WorldFileCoreLoadResult.TileBudgetExceeded, diagnostic.Result);
        Assert.Equal(WorldFileEnvelopeParseResult.Parsed, diagnostic.EnvelopeResult);
        Assert.Equal(WorldFileHeaderParseResult.Parsed, diagnostic.HeaderResult);
        Assert.Null(diagnostic.TileResult);
        Assert.Null(world);
    }

    [Fact]
    public void Never_publishes_partially_decoded_tile_state()
    {
        byte[] file = CreateCurrentCoreWorld([0x40, 0x02, 0x42, 0x02]);

        WorldFileCoreLoadDiagnostic diagnostic = WorldFileCoreLoader.TryLoad(file, maxTileCount: 6, out WorldFileCore? world);

        Assert.Equal(WorldFileCoreLoadResult.InvalidTiles, diagnostic.Result);
        Assert.Equal(WorldFileTileDecodeResult.Truncated, diagnostic.TileResult);
        Assert.Null(world);
    }

    [Fact]
    public void Reports_envelope_failure_without_claiming_later_stages_ran()
    {
        byte[] file = CreateCurrentCoreWorld([0x40, 0x02, 0x40, 0x02]);
        file[4] = (byte)'x';

        WorldFileCoreLoadDiagnostic diagnostic = WorldFileCoreLoader.TryLoad(file, maxTileCount: 6, out WorldFileCore? world);

        Assert.Equal(WorldFileCoreLoadResult.InvalidEnvelope, diagnostic.Result);
        Assert.Equal(WorldFileEnvelopeParseResult.BadMagic, diagnostic.EnvelopeResult);
        Assert.Null(diagnostic.HeaderResult);
        Assert.Null(diagnostic.TileResult);
        Assert.Null(world);
    }

    private static byte[] CreateCurrentCoreWorld(byte[] tileBytes)
    {
        int tileEnd = HeaderEnd + tileBytes.Length;
        int[] pointers =
        [
            EnvelopeEnd,
            HeaderEnd,
            tileEnd,
            tileEnd + 8,
            tileEnd + 16,
            tileEnd + 24,
            tileEnd + 32,
            tileEnd + 40,
            tileEnd + 48,
            tileEnd + 56,
            tileEnd + 64
        ];
        var file = new byte[pointers[^1] + 1];

        int offset = 0;
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(offset), WorldFileFormatPolicy.CurrentVersion);
        offset += sizeof(int);
        "relogic"u8.CopyTo(file.AsSpan(offset));
        offset += 7;
        file[offset++] = 2;
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(offset), 1);
        offset += sizeof(uint);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(offset), 0);
        offset += sizeof(ulong);
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(offset), VanillaWorldFormat326.SectionCount);
        offset += sizeof(short);
        foreach (int pointer in pointers)
        {
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(offset), pointer);
            offset += sizeof(int);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(offset), VanillaWorldFormat326.TileTypeCount);
        offset += sizeof(ushort);
        offset += (VanillaWorldFormat326.TileTypeCount + 7) >> 3;
        Assert.Equal(EnvelopeEnd, offset);

        using (var stream = new MemoryStream(file, writable: true))
        {
            stream.Position = EnvelopeEnd;
            using var writer = new BinaryWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            writer.Write("core");
            writer.Write("seed");
            writer.Write(1UL);
            writer.Write(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff").ToByteArray());
            writer.Write(7);
            writer.Write(0);
            writer.Write(32);
            writer.Write(0);
            writer.Write(48);
            writer.Write(3);
            writer.Write(2);
            writer.Flush();
            Assert.True(stream.Position <= HeaderEnd);
        }

        tileBytes.CopyTo(file, HeaderEnd);
        return file;
    }
}
