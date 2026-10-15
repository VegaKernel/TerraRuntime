using System.Security.Cryptography;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>Loads a detached read-only view of the exact canonical generation captured by a sandbox.
/// It never takes the writer lease or repairs, imports, prunes or changes the source world.</summary>
internal static class RuntimeWorldChestCustodyReader
{
    internal static RuntimeWorldChestCustodySnapshot Read(string worldPath, ReadOnlySpan<byte> canonicalBytes,
        WorldFileData world, bool trustInitialVanillaStock, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string directory = Path.GetFullPath(worldPath) + ".custody";
        // Any managed directory is evidence of prior authority, even if its anchor was lost.
        // An explicit vanilla import must never reset a damaged managed generation.
        bool managed;
        try
        {
            FileAttributes attributes = File.GetAttributes(directory);
            if ((attributes & FileAttributes.Directory) == 0)
                throw new InvalidDataException("Sandbox custody path is not a directory.");
            managed = true;
        }
        catch (FileNotFoundException) { managed = false; }
        catch (DirectoryNotFoundException) { managed = false; }
        if (!managed)
        {
            if (!trustInitialVanillaStock)
                throw new InvalidDataException("An explicit initial vanilla import is required for an unmanaged sandbox source.");
            return RuntimeWorldChestCustodySnapshot.FullImport(world);
        }

        RuntimeWorldChestCustodyArtifacts.ValidateAnchor(Path.Combine(directory, "managed-v1.anchor"), world);
        byte[] hash = SHA256.HashData(canonicalBytes);
        string generation = RuntimeWorldChestCustodyArtifacts.GetArtifactPath(Path.GetFullPath(worldPath),
            world.Envelope.Revision, hash);
        using var stream = new FileStream(generation, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < 1 || stream.Length > RuntimeWorldChestCustodyCodec.MaximumEncodedBytes)
            throw new InvalidDataException("Sandbox custody input exceeds its bounded byte limit.");
        byte[] bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactlyAsync(bytes, token).AsTask().GetAwaiter().GetResult();
        if (stream.ReadByte() != -1) throw new InvalidDataException("Sandbox custody input grew while reading.");
        token.ThrowIfCancellationRequested();
        // The immutable filename and codec bind the same bytes, identity and revision, not a
        // later reread of the path. Concurrent pruning can fail this clone closed, never refill it.
        return RuntimeWorldChestCustodyCodec.Decode(bytes, world, hash);
    }
}
