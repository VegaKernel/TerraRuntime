using TerraRuntime.World;

namespace TerraRuntime;

internal enum RuntimeWorldSaveTemplateLoadSource : byte
{
    RuntimeCache = 0,
    CanonicalWorld = 1
}

internal readonly record struct RuntimeWorldSaveTemplateLoadResult(
    bool Success,
    RuntimeWorldSaveTemplateLoadSource Source,
    RuntimeWorldPreservedSectionsLoadResult CacheResult,
    string? Error);

/// <summary>
/// Loads the small opaque source template required by tile/chest persistence without retaining or rereading the
/// canonical tile/chest payloads. Warm startup prefers the world image already embedded in the runtime cache and
/// falls back to sparse reads from the canonical .wld only when the cache template cannot be trusted.
/// </summary>
internal static class RuntimeWorldSaveTemplateLoader
{
    public static RuntimeWorldSaveTemplateLoadResult TryLoad(
        string worldPath,
        string runtimeCachePath,
        RuntimeWorldSourceStamp sourceStamp,
        WorldFileData world,
        out WorldFilePreservedSections? preserved)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeCachePath);
        ArgumentNullException.ThrowIfNull(world);
        preserved = null;

        RuntimeWorldPreservedSectionsLoadDiagnostic cacheDiagnostic =
            RuntimeWorldSnapshotPreservedSections.TryLoad(
                runtimeCachePath,
                sourceStamp,
                world,
                out preserved);
        if (cacheDiagnostic.IsLoaded && preserved is not null)
        {
            return new RuntimeWorldSaveTemplateLoadResult(
                Success: true,
                RuntimeWorldSaveTemplateLoadSource.RuntimeCache,
                cacheDiagnostic.Result,
                Error: null);
        }

        preserved = null;
        try
        {
            using var stream = new FileStream(
                worldPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.RandomAccess);
            if (!WorldFilePreservedSections.TryCapture(stream, world.Envelope, out preserved) || preserved is null)
            {
                preserved = null;
                return new RuntimeWorldSaveTemplateLoadResult(
                    Success: false,
                    RuntimeWorldSaveTemplateLoadSource.CanonicalWorld,
                    cacheDiagnostic.Result,
                    "Canonical world does not contain a valid preserved save template.");
            }

            return new RuntimeWorldSaveTemplateLoadResult(
                Success: true,
                RuntimeWorldSaveTemplateLoadSource.CanonicalWorld,
                cacheDiagnostic.Result,
                Error: null);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException or ObjectDisposedException)
        {
            preserved = null;
            return new RuntimeWorldSaveTemplateLoadResult(
                Success: false,
                RuntimeWorldSaveTemplateLoadSource.CanonicalWorld,
                cacheDiagnostic.Result,
                exception.Message);
        }
    }
}
