using System.Globalization;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.HostContracts.WorldGeneration;
using TerraRuntime.World;

namespace TerraRuntime;

internal enum RuntimeWorldCreationPersistenceStatus : byte
{
    Persisted = 0,
    GeneratorNotFound = 1,
    GenerationFailed = 2,
    FinalizationFailed = 3,
    HeaderFailed = 4,
    CompositionFailed = 5,
    AlreadyExists = 6,
    PublishFailed = 7
}

internal readonly record struct RuntimeWorldCreationPersistenceResult(
    RuntimeWorldCreationPersistenceStatus Status,
    RuntimeWorldCreationResult? Creation = null,
    WorldFileFreshCompose326Diagnostic? Composition = null,
    WorldFileAtomicPublishDiagnostic? Publication = null,
    string? WorldPath = null)
{
    public bool Succeeded => Status == RuntimeWorldCreationPersistenceStatus.Persisted;
}

/// <summary>
/// Adapter-layer transaction from a selectable generator to a validated canonical .wld. Generation remains isolated
/// and deterministic; file identity/timestamps are explicit inputs and are applied only after pass execution and
/// semantic finalization have succeeded.
/// </summary>
internal sealed class RuntimeWorldCreationPersistencePipeline
{
    private readonly RuntimeWorldCreationPipeline creation;

    public RuntimeWorldCreationPersistencePipeline(ITerraRuntimeWorldGeneratorSource generators)
    {
        ArgumentNullException.ThrowIfNull(generators);
        creation = new RuntimeWorldCreationPipeline(generators);
    }

    public RuntimeWorldCreationPersistenceResult TryCreateAndPersist(
        in WorldGenerationRequest request,
        string outputPath,
        Guid uniqueId,
        int worldId,
        byte gameMode,
        bool crimson,
        long creationTimeBinary,
        long lastPlayedBinary,
        CancellationToken cancellationToken = default,
        IWorldGenerationProgressSink? progressSink = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        if (File.Exists(outputPath))
        {
            return new RuntimeWorldCreationPersistenceResult(
                RuntimeWorldCreationPersistenceStatus.AlreadyExists,
                Publication: new WorldFileAtomicPublishDiagnostic(WorldFileAtomicPublishResult.AlreadyExists));
        }

        RuntimeWorldCreationResult created = creation.TryCreate(request, cancellationToken, progressSink);
        if (!created.Succeeded || created.Candidate is null)
        {
            RuntimeWorldCreationPersistenceStatus status = created.Status switch
            {
                RuntimeWorldCreationStatus.GeneratorNotFound => RuntimeWorldCreationPersistenceStatus.GeneratorNotFound,
                RuntimeWorldCreationStatus.GenerationFailed => RuntimeWorldCreationPersistenceStatus.GenerationFailed,
                RuntimeWorldCreationStatus.FinalizationFailed => RuntimeWorldCreationPersistenceStatus.FinalizationFailed,
                _ => RuntimeWorldCreationPersistenceStatus.GenerationFailed
            };
            return new RuntimeWorldCreationPersistenceResult(status, Creation: created);
        }

        WorldFileHeader header;
        try
        {
            header = VanillaFreshWorldHeader326.Create(
                request.WorldName,
                request.Seed.ToString(CultureInfo.InvariantCulture),
                request.WidthTiles,
                request.HeightTiles,
                uniqueId,
                worldId);
        }
        catch (Exception exception) when (
            exception is ArgumentException or ArgumentOutOfRangeException or OverflowException)
        {
            return new RuntimeWorldCreationPersistenceResult(
                RuntimeWorldCreationPersistenceStatus.HeaderFailed,
                Creation: created);
        }

        WorldFileFreshCompose326Diagnostic composition = WorldFileFreshComposer326.TryCompose(
            header,
            created.Metadata,
            created.Candidate.TileStore,
            gameMode,
            crimson,
            creationTimeBinary,
            lastPlayedBinary,
            out byte[] canonicalWorld);
        if (!composition.Succeeded)
        {
            return new RuntimeWorldCreationPersistenceResult(
                RuntimeWorldCreationPersistenceStatus.CompositionFailed,
                Creation: created,
                Composition: composition);
        }

        WorldFileAtomicPublishDiagnostic publication = WorldFileAtomicPublisher.TryCreate(
            outputPath,
            canonicalWorld);
        if (!publication.IsPublished)
        {
            return new RuntimeWorldCreationPersistenceResult(
                publication.Result == WorldFileAtomicPublishResult.AlreadyExists
                    ? RuntimeWorldCreationPersistenceStatus.AlreadyExists
                    : RuntimeWorldCreationPersistenceStatus.PublishFailed,
                Creation: created,
                Composition: composition,
                Publication: publication);
        }

        return new RuntimeWorldCreationPersistenceResult(
            RuntimeWorldCreationPersistenceStatus.Persisted,
            Creation: created,
            Composition: composition,
            Publication: publication,
            WorldPath: Path.GetFullPath(outputPath));
    }
}
