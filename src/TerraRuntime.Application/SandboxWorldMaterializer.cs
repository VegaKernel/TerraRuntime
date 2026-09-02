using System.Globalization;
using System.Security.Cryptography;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.HostContracts.WorldGeneration;
using TerraRuntime.World;

namespace TerraRuntime;

internal enum SandboxWorldMaterializationStatus : byte
{
    Ready = 0,
    UnsupportedSource = 1,
    SourceReadFailed = 2,
    GenerationFailed = 3,
    CompositionFailed = 4,
    ValidationFailed = 5,
    BootstrapFailed = 6,
    Canceled = 7
}

internal readonly record struct SandboxWorldMaterializationResult(
    SandboxWorldMaterializationStatus Status,
    WorldFileData? World = null,
    PlayerBootstrapPacketSet? Bootstrap = null,
    string? Error = null)
{
    public bool Succeeded => Status == SandboxWorldMaterializationStatus.Ready && World is not null && Bootstrap is not null;
}

/// <summary>Materializes one detached source into validated world/bootstrap state without touching a live runtime.</summary>
internal sealed class SandboxWorldMaterializer
{
    private readonly RuntimeWorldCreationPipeline generation;
    private readonly WorldFileLoadLimits loadLimits;

    public SandboxWorldMaterializer(
        ITerraRuntimeWorldGeneratorSource generators,
        WorldFileLoadLimits loadLimits)
    {
        generation = new RuntimeWorldCreationPipeline(generators ?? throw new ArgumentNullException(nameof(generators)));
        loadLimits.Validate();
        this.loadLimits = loadLimits;
    }

    public SandboxWorldMaterializationResult Materialize(
        SandboxWorldSource source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return source switch
            {
                SandboxWorldSource.Generated generated => MaterializeGenerated(generated, cancellationToken),
                SandboxWorldSource.WorldFile file => MaterializeFile(file, cancellationToken),
                _ => new SandboxWorldMaterializationResult(
                    SandboxWorldMaterializationStatus.UnsupportedSource,
                    Error: $"Source '{source.GetType().Name}' is not materialized by Level 1 yet.")
            };
        }
        catch (OperationCanceledException)
        {
            return new SandboxWorldMaterializationResult(SandboxWorldMaterializationStatus.Canceled);
        }
    }

    private SandboxWorldMaterializationResult MaterializeGenerated(
        SandboxWorldSource.Generated source,
        CancellationToken cancellationToken)
    {
        WorldGenerationRequest request = source.ToRequest();
        try
        {
            request.Validate();
            long tileCount = checked((long)request.WidthTiles * request.HeightTiles);
            if (tileCount > loadLimits.MaxTileCount)
            {
                return new SandboxWorldMaterializationResult(
                    SandboxWorldMaterializationStatus.GenerationFailed,
                    Error: $"Generated world has {tileCount} tiles; limit is {loadLimits.MaxTileCount}.");
            }
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            return new SandboxWorldMaterializationResult(
                SandboxWorldMaterializationStatus.GenerationFailed,
                Error: exception.Message);
        }

        RuntimeWorldCreationPipelineResult created = generation.CreateCandidate(in request, cancellationToken: cancellationToken);
        if (cancellationToken.IsCancellationRequested)
            return new SandboxWorldMaterializationResult(SandboxWorldMaterializationStatus.Canceled);
        if (!created.Succeeded || created.Candidate is null)
        {
            return new SandboxWorldMaterializationResult(
                SandboxWorldMaterializationStatus.GenerationFailed,
                Error: $"Generation={created.Generation.Status}, finalization={created.Finalization?.Status}.");
        }

        string seedText = request.SeedText ?? request.Seed.ToString(CultureInfo.InvariantCulture);
        WorldFileHeader header = VanillaFreshWorldHeader326.Create(
            request.WorldName,
            seedText,
            request.WidthTiles,
            request.HeightTiles,
            Guid.NewGuid(),
            RandomNumberGenerator.GetInt32(1, int.MaxValue));
        long now = DateTime.UtcNow.ToBinary();
        WorldFileFreshCompose326Diagnostic composition = WorldFileFreshComposer326.TryCompose(
            header,
            created.Metadata,
            created.Candidate.TileStore,
            created.Candidate.CaptureGeneratedChests(),
            created.Candidate.CaptureGeneratedNpcs(),
            gameMode: (byte)request.Options.GameMode,
            crimson: request.Options.Evil == WorldGenerationEvil.Crimson,
            creationTimeBinary: now,
            lastPlayedBinary: now,
            out byte[] canonicalWorld);
        if (!composition.Succeeded)
        {
            return new SandboxWorldMaterializationResult(
                SandboxWorldMaterializationStatus.CompositionFailed,
                Error: $"Composition={composition.Result}, stage={composition.StageResultCode}.");
        }

        return LoadAndBootstrap(canonicalWorld);
    }

    private SandboxWorldMaterializationResult MaterializeFile(
        SandboxWorldSource.WorldFile source,
        CancellationToken cancellationToken)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytesAsync(source.AssetPath, cancellationToken).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new SandboxWorldMaterializationResult(
                SandboxWorldMaterializationStatus.SourceReadFailed,
                Error: exception.Message);
        }

        return LoadAndBootstrap(bytes);
    }

    private SandboxWorldMaterializationResult LoadAndBootstrap(byte[] canonicalWorld)
    {
        WorldFileLoadDiagnostic load = WorldFileLoader.TryLoad(canonicalWorld, loadLimits, out WorldFileData? world);
        if (!load.IsLoaded || world is null)
        {
            return new SandboxWorldMaterializationResult(
                SandboxWorldMaterializationStatus.ValidationFailed,
                Error: $"Load={load.Result}, stage={load.Stage}, code={load.StageResultCode}.");
        }

        try
        {
            return new SandboxWorldMaterializationResult(
                SandboxWorldMaterializationStatus.Ready,
                world,
                PlayerBootstrapPacketSet.Create(world));
        }
        catch (Exception exception) when (exception is InvalidOperationException or OverflowException)
        {
            return new SandboxWorldMaterializationResult(
                SandboxWorldMaterializationStatus.BootstrapFailed,
                Error: exception.Message);
        }
    }
}
