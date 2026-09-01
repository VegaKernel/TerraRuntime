namespace TerraRuntime.Schematics;

/// <summary>Filesystem convenience API over the bounded binary codec used by TerraRuntime, Vega and WorldEdit.</summary>
public static class SchematicFile
{
    public const string Extension = ".trschem";

    private const int IoBufferSize = 64 * 1024;

    public static SchematicDocument Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists)
            throw new FileNotFoundException("Schematic file was not found.", fullPath);
        if (info.Length > SchematicLimits.MaxFileBytes)
            throw new SchematicFormatException("Schematic file exceeds the file-size ceiling.");

        using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            IoBufferSize,
            FileOptions.SequentialScan);
        return SchematicBinary.Read(stream);
    }

    public static async ValueTask<SchematicDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists)
            throw new FileNotFoundException("Schematic file was not found.", fullPath);
        if (info.Length > SchematicLimits.MaxFileBytes)
            throw new SchematicFormatException("Schematic file exceeds the file-size ceiling.");

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            IoBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await SchematicBinary.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    public static void Save(string path, SchematicDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);
        byte[] bytes = SchematicBinary.Serialize(document);
        string fullPath = PrepareDestination(path);
        string temporaryPath = CreateTemporaryPath(fullPath);
        try
        {
            File.WriteAllBytes(temporaryPath, bytes);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    public static async ValueTask SaveAsync(string path, SchematicDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);
        byte[] bytes = SchematicBinary.Serialize(document);
        string fullPath = PrepareDestination(path);
        string temporaryPath = CreateTemporaryPath(fullPath);
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static string PrepareDestination(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        return fullPath;
    }

    private static string CreateTemporaryPath(string fullPath) =>
        $"{fullPath}.{Guid.NewGuid():N}.tmp";

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup. The original save exception remains authoritative.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup. The original save exception remains authoritative.
        }
    }
}
