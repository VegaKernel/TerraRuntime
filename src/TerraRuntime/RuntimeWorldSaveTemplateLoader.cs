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
/// Loads the compact source template required by runtime persistence without retaining or rereading canonical tile/chest
/// payloads. The opaque header remains byte-preserved, while decoded static side-table sections are normalized through
/// the version-pinned semantic encoders before the mutable world is allowed to use the template.
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
            return NormalizeTemplate(
                RuntimeWorldSaveTemplateLoadSource.RuntimeCache,
                cacheDiagnostic.Result,
                world,
                ref preserved);
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

            return NormalizeTemplate(
                RuntimeWorldSaveTemplateLoadSource.CanonicalWorld,
                cacheDiagnostic.Result,
                world,
                ref preserved);
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

    private static RuntimeWorldSaveTemplateLoadResult NormalizeTemplate(
        RuntimeWorldSaveTemplateLoadSource source,
        RuntimeWorldPreservedSectionsLoadResult cacheResult,
        WorldFileData world,
        ref WorldFilePreservedSections? preserved)
    {
        WorldFilePreservedSections template = preserved
            ?? throw new InvalidOperationException("Save-template normalization requires captured preserved sections.");
        WorldFilePreservedSectionNormalizationDiagnostic normalization =
            template.TryNormalizeSemanticSections(world, out WorldFilePreservedSections? normalized);
        if (!normalization.IsNormalized || normalized is null)
        {
            preserved = null;
            return new RuntimeWorldSaveTemplateLoadResult(
                Success: false,
                source,
                cacheResult,
                $"Semantic save-template normalization failed: result={normalization.Result}, code={normalization.StageResultCode}.");
        }

        preserved = normalized;
        return new RuntimeWorldSaveTemplateLoadResult(
            Success: true,
            source,
            cacheResult,
            Error: null);
    }
}
