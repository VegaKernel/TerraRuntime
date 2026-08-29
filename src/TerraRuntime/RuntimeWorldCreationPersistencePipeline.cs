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
    PublishFailed = 7,
    GenerationBudgetExceeded = 8
}

internal readonly record struct RuntimeWorldCreationPersistenceResult(
    RuntimeWorldCreationPersistenceStatus Status,
    RuntimeWorldCreationPipelineResult? Creation = null,
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
    private readonly long maxTileCount;

    public RuntimeWorldCreationPersistencePipeline(
        ITerraRuntimeWorldGeneratorSource generators,
        long maxTileCount)
    {
        ArgumentNullException.ThrowIfNull(generators);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTileCount, 1);
        creation = new RuntimeWorldCreationPipeline(generators);
        this.maxTileCount = maxTileCount;
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

        long tileCount;
        try
        {
            tileCount = checked((long)request.WidthTiles * request.HeightTiles);
        }
        catch (OverflowException)
        {
            return new RuntimeWorldCreationPersistenceResult(
                RuntimeWorldCreationPersistenceStatus.GenerationBudgetExceeded);
        }

        if (tileCount <= 0 || tileCount > maxTileCount)
        {
            return new RuntimeWorldCreationPersistenceResult(
                RuntimeWorldCreationPersistenceStatus.GenerationBudgetExceeded);
        }

        if (File.Exists(outputPath))
        {
            return new RuntimeWorldCreationPersistenceResult(
                RuntimeWorldCreationPersistenceStatus.AlreadyExists,
                Publication: new WorldFileAtomicPublishDiagnostic(WorldFileAtomicPublishResult.AlreadyExists));
        }

        RuntimeWorldCreationPipelineResult created = creation.CreateCandidate(
            in request,
            progressSink,
            cancellationToken);
        if (!created.Succeeded || created.Candidate is null)
        {
            RuntimeWorldCreationPersistenceStatus status = created.Generation.Status switch
            {
                RuntimeWorldGenerationCandidateStatus.GeneratorNotFound => RuntimeWorldCreationPersistenceStatus.GeneratorNotFound,
                _ when created.Status == RuntimeWorldCreationPipelineStatus.FinalizationFailed =>
                    RuntimeWorldCreationPersistenceStatus.FinalizationFailed,
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
