namespace TerraRuntime.World;

public enum WorldSectionPayloadAssemblyResult : byte
{
    Encoded = 0,
    InvalidTilePayload = 1,
    InvalidObjectMetadata = 2
}

/// <summary>
/// Composes the verified tile codec with Terraria 1.4.5.8 section-local object metadata.
/// The tile-only encoder deliberately ends with three zero Int16 counts; this assembler replaces that
/// six-byte placeholder with the full chest/sign/tile-entity tail from CompressTileBlock_Inner.
/// </summary>
public static class WorldSectionPayloadAssembler
{
    private const int EmptyObjectTailLength = sizeof(short) * 3;

    public static WorldSectionPayloadAssemblyResult TryEncode(
        WorldFileData world,
        int xStart,
        int yStart,
        int width,
        int height,
        out byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(world);
        payload = [];

        WorldSectionPayloadEncodeResult tileResult = WorldSectionPayloadEncoder.TryEncodeTileOnly(
            world,
            xStart,
            yStart,
            width,
            height,
            out byte[] tileOnly);
        if (tileResult != WorldSectionPayloadEncodeResult.Encoded)
            return WorldSectionPayloadAssemblyResult.InvalidTilePayload;

        return TryAssemble(world, xStart, yStart, width, height, tileOnly, out payload);
    }

    /// <summary>
    /// Assembles packet-10 payload data from an immutable tile snapshot plus the world's immutable
    /// section-local persistence metadata. No live tile storage is read by this overload.
    /// </summary>
    public static WorldSectionPayloadAssemblyResult TryEncode(
        WorldFileData world,
        WorldSectionTileSnapshot snapshot,
        out byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(snapshot);
        payload = [];

        WorldSectionPayloadEncodeResult tileResult = WorldSectionPayloadEncoder.TryEncodeTileOnly(
            world,
            snapshot,
            out byte[] tileOnly);
        if (tileResult != WorldSectionPayloadEncodeResult.Encoded)
            return WorldSectionPayloadAssemblyResult.InvalidTilePayload;

        WorldTileBounds bounds = snapshot.Bounds;
        return TryAssemble(
            world,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            tileOnly,
            out payload);
    }

    private static WorldSectionPayloadAssemblyResult TryAssemble(
        WorldFileData world,
        int xStart,
        int yStart,
        int width,
        int height,
        byte[] tileOnly,
        out byte[] payload)
    {
        payload = [];
        if (tileOnly.Length < EmptyObjectTailLength)
            return WorldSectionPayloadAssemblyResult.InvalidTilePayload;

        WorldSectionObjectMetadataEncodeResult metadataResult = WorldSectionObjectMetadataEncoder.TryEncode(
            world,
            xStart,
            yStart,
            width,
            height,
            out byte[] metadata);
        if (metadataResult != WorldSectionObjectMetadataEncodeResult.Encoded)
            return WorldSectionPayloadAssemblyResult.InvalidObjectMetadata;

        int tileBytes = tileOnly.Length - EmptyObjectTailLength;
        payload = GC.AllocateUninitializedArray<byte>(checked(tileBytes + metadata.Length));
        tileOnly.AsSpan(0, tileBytes).CopyTo(payload);
        metadata.CopyTo(payload, tileBytes);
        return WorldSectionPayloadAssemblyResult.Encoded;
    }
}
