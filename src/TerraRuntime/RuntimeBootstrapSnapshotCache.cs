using System.Security.Cryptography;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Disposable cache of already encoded immutable join bootstrap frames. The cache is tied to the
/// canonical world source stamp and to the current TerraRuntime module MVID, so a rebuilt server
/// binary never reuses packet bytes produced by an older encoder implementation.
/// </summary>
internal static class RuntimeBootstrapSnapshotCache
{
    private const int HeaderSize = 128;
    private const int HashSize = 32;
    private const int IoBufferSize = 64 * 1024;
    private const int MaxPayloadBytes = 32 * 1024 * 1024;
    private const int MaxPostFramesPerSection = 512;
    private const int MaxGlobalPostFrames = 2_048;

    private static ReadOnlySpan<byte> Magic => "TRBOOTPK"u8;

    private static Guid CurrentBuildId => typeof(RuntimeBootstrapSnapshotCache).Module.ModuleVersionId;

    public static string GetCachePath(string worldPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldPath);
        return Path.ChangeExtension(worldPath, ".runtime-bootstrap");
    }

    public static RuntimeBootstrapSnapshotLoadDiagnostic TryLoad(
        string cachePath,
        RuntimeWorldSourceStamp sourceStamp,
        WorldFileData world,
        out PlayerBootstrapPacketSet? packets)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cachePath);
        ArgumentNullException.ThrowIfNull(world);
        packets = null;

        if (!File.Exists(cachePath))
            return new RuntimeBootstrapSnapshotLoadDiagnostic(RuntimeBootstrapSnapshotLoadResult.NotFound);

        try
        {
            using var stream = new FileStream(
                cachePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                IoBufferSize,
                FileOptions.SequentialScan);

            Span<byte> header = stackalloc byte[HeaderSize];
            stream.ReadExactly(header);

            if (!header[..Magic.Length].SequenceEqual(Magic) ||
                BitConverter.ToInt32(header[8..12]) != HeaderSize ||
                BitConverter.ToInt32(header[12..16]) != 0)
            {
                return new RuntimeBootstrapSnapshotLoadDiagnostic(RuntimeBootstrapSnapshotLoadResult.InvalidHeader);
            }

            long sourceLength = BitConverter.ToInt64(header[16..24]);
            long sourceLastWriteTicks = BitConverter.ToInt64(header[24..32]);
            Guid buildId = new(header[32..48]);
            Guid worldUniqueId = new(header[48..64]);
            int worldId = BitConverter.ToInt32(header[64..68]);
            int formatVersion = BitConverter.ToInt32(header[68..72]);
            int width = BitConverter.ToInt32(header[72..76]);
            int height = BitConverter.ToInt32(header[76..80]);
            long payloadLength = BitConverter.ToInt64(header[80..88]);

            if (!header[120..128].SequenceEqual(stackalloc byte[8]) ||
                sourceLength <= 0 ||
                sourceLastWriteTicks < 0 ||
                payloadLength <= 0 ||
                payloadLength > MaxPayloadBytes ||
                stream.Length != checked(HeaderSize + payloadLength))
            {
                return new RuntimeBootstrapSnapshotLoadDiagnostic(RuntimeBootstrapSnapshotLoadResult.InvalidHeader);
            }

            if (buildId != CurrentBuildId)
                return new RuntimeBootstrapSnapshotLoadDiagnostic(RuntimeBootstrapSnapshotLoadResult.BuildMismatch);

            if (sourceStamp.Length != sourceLength || sourceStamp.LastWriteTimeUtcTicks != sourceLastWriteTicks)
                return new RuntimeBootstrapSnapshotLoadDiagnostic(RuntimeBootstrapSnapshotLoadResult.SourceMismatch);

            if (world.Header.UniqueId != worldUniqueId ||
                world.Header.WorldId != worldId ||
                world.Envelope.FormatVersion != formatVersion ||
                world.Header.Dimensions.WidthTiles != width ||
                world.Header.Dimensions.HeightTiles != height)
            {
                return new RuntimeBootstrapSnapshotLoadDiagnostic(RuntimeBootstrapSnapshotLoadResult.WorldMismatch);
            }

            byte[] payload = new byte[checked((int)payloadLength)];
            stream.ReadExactly(payload);

            Span<byte> actualHash = stackalloc byte[HashSize];
            SHA256.HashData(payload, actualHash);
            if (!actualHash.SequenceEqual(header[88..120]))
            {
                return new RuntimeBootstrapSnapshotLoadDiagnostic(
                    RuntimeBootstrapSnapshotLoadResult.PayloadHashMismatch);
            }

            if (!TryDecodePayload(payload, out PlayerBootstrapPacketSnapshot? snapshot) || snapshot is null)
                return new RuntimeBootstrapSnapshotLoadDiagnostic(RuntimeBootstrapSnapshotLoadResult.InvalidPayload);

            if (!PlayerBootstrapPacketSet.TryCreateFromSnapshot(world, snapshot, out packets) || packets is null)
            {
                packets = null;
                return new RuntimeBootstrapSnapshotLoadDiagnostic(RuntimeBootstrapSnapshotLoadResult.InvalidPacketSet);
            }

            return new RuntimeBootstrapSnapshotLoadDiagnostic(RuntimeBootstrapSnapshotLoadResult.Loaded);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or OverflowException or ArgumentException)
        {
            packets = null;
            return new RuntimeBootstrapSnapshotLoadDiagnostic(RuntimeBootstrapSnapshotLoadResult.IoError);
        }
    }

    public static RuntimeBootstrapSnapshotWriteDiagnostic TryWriteAtomic(
        string cachePath,
        RuntimeWorldSourceStamp sourceStamp,
        WorldFileData world,
        PlayerBootstrapPacketSet packets)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cachePath);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(packets);

        if (sourceStamp.Length <= 0 || sourceStamp.LastWriteTimeUtcTicks < 0)
            return new RuntimeBootstrapSnapshotWriteDiagnostic(RuntimeBootstrapSnapshotWriteResult.InvalidPacketSet);

        byte[] payload;
        try
        {
            payload = EncodePayload(packets.CaptureSnapshot());
        }
        catch (Exception exception) when (exception is IOException or OverflowException or InvalidDataException)
        {
            return new RuntimeBootstrapSnapshotWriteDiagnostic(RuntimeBootstrapSnapshotWriteResult.InvalidPacketSet);
        }

        if (payload.Length is <= 0 or > MaxPayloadBytes)
            return new RuntimeBootstrapSnapshotWriteDiagnostic(RuntimeBootstrapSnapshotWriteResult.InvalidPacketSet);

        Span<byte> header = stackalloc byte[HeaderSize];
        header.Clear();
        Magic.CopyTo(header);
        BitConverter.TryWriteBytes(header[8..12], HeaderSize);
        BitConverter.TryWriteBytes(header[12..16], 0);
        BitConverter.TryWriteBytes(header[16..24], sourceStamp.Length);
        BitConverter.TryWriteBytes(header[24..32], sourceStamp.LastWriteTimeUtcTicks);
        CurrentBuildId.TryWriteBytes(header[32..48]);
        world.Header.UniqueId.TryWriteBytes(header[48..64]);
        BitConverter.TryWriteBytes(header[64..68], world.Header.WorldId);
        BitConverter.TryWriteBytes(header[68..72], world.Envelope.FormatVersion);
        BitConverter.TryWriteBytes(header[72..76], world.Header.Dimensions.WidthTiles);
        BitConverter.TryWriteBytes(header[76..80], world.Header.Dimensions.HeightTiles);
        BitConverter.TryWriteBytes(header[80..88], (long)payload.Length);
        SHA256.HashData(payload, header[88..120]);

        string tempPath = cachePath + ".tmp";
        bool replaced = false;
        try
        {
            string? directory = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using (var stream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                IoBufferSize,
                FileOptions.SequentialScan))
            {
                stream.Write(header);
                stream.Write(payload);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, cachePath, overwrite: true);
            replaced = true;
            return new RuntimeBootstrapSnapshotWriteDiagnostic(RuntimeBootstrapSnapshotWriteResult.Written);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new RuntimeBootstrapSnapshotWriteDiagnostic(RuntimeBootstrapSnapshotWriteResult.IoError);
        }
        finally
        {
            if (!replaced)
                TryDelete(tempPath);
        }
    }

    private static byte[] EncodePayload(PlayerBootstrapPacketSnapshot snapshot)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        WriteFrame(writer, snapshot.WorldInfoFrame);
        WriteFrame(writer, snapshot.StatusFrame);

        writer.Write(snapshot.BaseSections.Length);
        if (snapshot.BaseSections.Length > InitialSectionBootstrapPlanner.MaximumBaseSectionCount ||
            snapshot.BaseSectionFrames.Length != snapshot.BaseSections.Length ||
            snapshot.BaseSectionPostFrames.Length != snapshot.BaseSections.Length)
        {
            throw new InvalidDataException("Invalid bootstrap base section layout.");
        }

        for (int i = 0; i < snapshot.BaseSections.Length; i++)
        {
            writer.Write(snapshot.BaseSections[i].X);
            writer.Write(snapshot.BaseSections[i].Y);
            WriteFrame(writer, snapshot.BaseSectionFrames[i]);

            ReadOnlyMemory<byte>[] postFrames = snapshot.BaseSectionPostFrames[i]
                ?? throw new InvalidDataException("Bootstrap post-section frames cannot be null.");
            if (postFrames.Length > MaxPostFramesPerSection)
                throw new InvalidDataException("Too many post-section frames.");

            writer.Write(postFrames.Length);
            foreach (ReadOnlyMemory<byte> frame in postFrames)
                WriteFrame(writer, frame);
        }

        if (snapshot.GlobalPostSectionFrames.Length > MaxGlobalPostFrames)
            throw new InvalidDataException("Too many global bootstrap frames.");

        writer.Write(snapshot.GlobalPostSectionFrames.Length);
        foreach (ReadOnlyMemory<byte> frame in snapshot.GlobalPostSectionFrames)
            WriteFrame(writer, frame);

        WriteFrame(writer, snapshot.EnterWorldFrame);
        writer.Flush();
        return stream.ToArray();
    }

    private static bool TryDecodePayload(
        byte[] payload,
        out PlayerBootstrapPacketSnapshot? snapshot)
    {
        snapshot = null;
        try
        {
            using var stream = new MemoryStream(payload, writable: false);
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

            if (!TryReadFrame(reader, stream, out ReadOnlyMemory<byte> worldInfoFrame) ||
                !TryReadFrame(reader, stream, out ReadOnlyMemory<byte> statusFrame))
            {
                return false;
            }

            if (!TryReadCount(reader, stream, InitialSectionBootstrapPlanner.MaximumBaseSectionCount, out int sectionCount))
                return false;

            var sections = new WorldSectionId[sectionCount];
            var baseFrames = new ReadOnlyMemory<byte>[sectionCount];
            var postFrames = new ReadOnlyMemory<byte>[sectionCount][];
            for (int i = 0; i < sectionCount; i++)
            {
                if (stream.Length - stream.Position < sizeof(int) * 2)
                    return false;

                sections[i] = new WorldSectionId(reader.ReadInt32(), reader.ReadInt32());
                if (!TryReadFrame(reader, stream, out baseFrames[i]) ||
                    !TryReadCount(reader, stream, MaxPostFramesPerSection, out int postCount))
                {
                    return false;
                }

                var sectionPostFrames = new ReadOnlyMemory<byte>[postCount];
                for (int frameIndex = 0; frameIndex < postCount; frameIndex++)
                {
                    if (!TryReadFrame(reader, stream, out sectionPostFrames[frameIndex]))
                        return false;
                }

                postFrames[i] = sectionPostFrames;
            }

            if (!TryReadCount(reader, stream, MaxGlobalPostFrames, out int globalCount))
                return false;

            var globalFrames = new ReadOnlyMemory<byte>[globalCount];
            for (int i = 0; i < globalCount; i++)
            {
                if (!TryReadFrame(reader, stream, out globalFrames[i]))
                    return false;
            }

            if (!TryReadFrame(reader, stream, out ReadOnlyMemory<byte> enterWorldFrame) ||
                stream.Position != stream.Length)
            {
                return false;
            }

            snapshot = new PlayerBootstrapPacketSnapshot(
                sections,
                worldInfoFrame,
                statusFrame,
                baseFrames,
                postFrames,
                globalFrames,
                enterWorldFrame);
            return true;
        }
        catch (Exception exception) when (exception is EndOfStreamException or IOException or OverflowException)
        {
            snapshot = null;
            return false;
        }
    }

    private static void WriteFrame(BinaryWriter writer, ReadOnlyMemory<byte> frame)
    {
        if (frame.Length is < TerrariaFrameDecoderOptions.MinimumFrameLength or > ushort.MaxValue)
            throw new InvalidDataException($"Invalid bootstrap frame length {frame.Length}.");

        writer.Write(frame.Length);
        writer.Write(frame.Span);
    }

    private static bool TryReadFrame(
        BinaryReader reader,
        MemoryStream stream,
        out ReadOnlyMemory<byte> frame)
    {
        frame = default;
        if (stream.Length - stream.Position < sizeof(int))
            return false;

        int length = reader.ReadInt32();
        if (length is < TerrariaFrameDecoderOptions.MinimumFrameLength or > ushort.MaxValue ||
            stream.Length - stream.Position < length)
        {
            return false;
        }

        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            return false;

        frame = bytes;
        return true;
    }

    private static bool TryReadCount(
        BinaryReader reader,
        MemoryStream stream,
        int maximum,
        out int count)
    {
        count = 0;
        if (stream.Length - stream.Position < sizeof(int))
            return false;

        count = reader.ReadInt32();
        return count >= 0 && count <= maximum;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}

internal enum RuntimeBootstrapSnapshotLoadResult : byte
{
    Loaded = 0,
    NotFound = 1,
    IoError = 2,
    InvalidHeader = 3,
    BuildMismatch = 4,
    SourceMismatch = 5,
    WorldMismatch = 6,
    PayloadHashMismatch = 7,
    InvalidPayload = 8,
    InvalidPacketSet = 9
}

internal readonly record struct RuntimeBootstrapSnapshotLoadDiagnostic(RuntimeBootstrapSnapshotLoadResult Result)
{
    public bool IsLoaded => Result == RuntimeBootstrapSnapshotLoadResult.Loaded;
}

internal enum RuntimeBootstrapSnapshotWriteResult : byte
{
    Written = 0,
    InvalidPacketSet = 1,
    IoError = 2
}

internal readonly record struct RuntimeBootstrapSnapshotWriteDiagnostic(RuntimeBootstrapSnapshotWriteResult Result)
{
    public bool IsWritten => Result == RuntimeBootstrapSnapshotWriteResult.Written;
}
