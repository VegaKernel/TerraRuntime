using System.Buffers;
using System.IO.Compression;
using global::Multiplicity.Packets;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Protocol.Multiplicity;

public enum WorldSectionPacketEncodeResult : byte
{
    Encoded = 0,
    InvalidSection = 1,
    CompressionFailure = 2,
    FrameTooLarge = 3,
    InvalidObjectMetadata = 4
}

/// <summary>
/// Builds Terraria 1.4.5.8 packet 10 from the normalized world representation.
/// The inner block is a raw DEFLATE stream, matching vanilla's DeflateStream framing.
/// </summary>
public static class WorldSectionPacketEncoder
{
    public static WorldSectionPacketEncodeResult TryEncode(
        WorldFileData world,
        int xStart,
        int yStart,
        int width,
        int height,
        out byte[] frame)
    {
        ArgumentNullException.ThrowIfNull(world);
        frame = [];

        WorldSectionPayloadAssemblyResult payloadResult = WorldSectionPayloadAssembler.TryEncode(
            world,
            xStart,
            yStart,
            width,
            height,
            out byte[] uncompressed);
        return CompletePacket(payloadResult, uncompressed, out frame);
    }

    /// <summary>
    /// Compatibility path for callers that provide an immutable tile snapshot while still resolving object
    /// metadata from the loaded world on the caller thread.
    /// </summary>
    public static WorldSectionPacketEncodeResult TryEncode(
        WorldFileData world,
        WorldSectionTileSnapshot snapshot,
        out byte[] frame)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(snapshot);
        frame = [];

        WorldSectionPayloadAssemblyResult payloadResult = WorldSectionPayloadAssembler.TryEncode(
            world,
            snapshot,
            out byte[] uncompressed);
        return CompletePacket(payloadResult, uncompressed, out frame);
    }

    /// <summary>
    /// Worker-safe packet-10 encoder. The supplied snapshot contains every mutable input required by section
    /// payload assembly, so this overload performs tile encoding and DEFLATE without reading live world state.
    /// </summary>
    public static WorldSectionPacketEncodeResult TryEncode(
        WorldSectionPacketSnapshot snapshot,
        out byte[] frame)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        frame = [];

        WorldSectionPayloadAssemblyResult payloadResult = WorldSectionPayloadAssembler.TryEncode(
            snapshot,
            out byte[] uncompressed);
        return CompletePacket(payloadResult, uncompressed, out frame);
    }

    public static WorldSectionPacketEncodeResult TryEncodeTileOnly(
        WorldFileData world,
        int xStart,
        int yStart,
        int width,
        int height,
        out byte[] frame)
    {
        ArgumentNullException.ThrowIfNull(world);
        frame = [];

        WorldSectionPayloadEncodeResult payloadResult = WorldSectionPayloadEncoder.TryEncodeTileOnly(
            world,
            xStart,
            yStart,
            width,
            height,
            out byte[] uncompressed);
        if (payloadResult != WorldSectionPayloadEncodeResult.Encoded)
            return WorldSectionPacketEncodeResult.InvalidSection;

        return TryCompressFrame(uncompressed, out frame);
    }

    private static WorldSectionPacketEncodeResult CompletePacket(
        WorldSectionPayloadAssemblyResult payloadResult,
        byte[] uncompressed,
        out byte[] frame)
    {
        frame = [];
        if (payloadResult == WorldSectionPayloadAssemblyResult.InvalidObjectMetadata)
            return WorldSectionPacketEncodeResult.InvalidObjectMetadata;
        if (payloadResult != WorldSectionPayloadAssemblyResult.Encoded)
            return WorldSectionPacketEncodeResult.InvalidSection;

        return TryCompressFrame(uncompressed, out frame);
    }

    private static WorldSectionPacketEncodeResult TryCompressFrame(byte[] uncompressed, out byte[] frame)
    {
        frame = [];
        byte[] compressed;
        try
        {
            using var stream = new MemoryStream(Math.Min(uncompressed.Length, 64 * 1024));
            using (var deflate = new DeflateStream(
                stream,
                CompressionLevel.SmallestSize,
                leaveOpen: true))
            {
                deflate.Write(uncompressed);
            }
            compressed = stream.ToArray();
        }
        catch (InvalidDataException)
        {
            return WorldSectionPacketEncodeResult.CompressionFailure;
        }
        catch (IOException)
        {
            return WorldSectionPacketEncodeResult.CompressionFailure;
        }

        var writer = new ArrayBufferWriter<byte>(checked(compressed.Length + TerrariaFrameDecoderOptions.MinimumFrameLength));
        TerrariaFrameWriteResult frameResult = TerrariaFrameEncoder.TryWrite(
            writer,
            (byte)PacketTypes.TileSendSection,
            compressed);
        if (frameResult == TerrariaFrameWriteResult.FrameTooLarge)
            return WorldSectionPacketEncodeResult.FrameTooLarge;
        if (frameResult != TerrariaFrameWriteResult.Written)
            return WorldSectionPacketEncodeResult.CompressionFailure;

        frame = writer.WrittenSpan.ToArray();
        return WorldSectionPacketEncodeResult.Encoded;
    }
}
