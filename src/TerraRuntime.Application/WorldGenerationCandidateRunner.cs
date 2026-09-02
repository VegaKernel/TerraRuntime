using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.HostContracts.WorldGeneration;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration;

namespace TerraRuntime;

public enum RuntimeWorldGenerationCandidateStatus : byte
{
    Generated = 0,
    GeneratorNotFound = 1,
    GenerationFailed = 2
}

/// <summary>
/// Result of one selected world-generator execution. A candidate workspace is exposed only after every configured
/// pass has completed successfully; failed or cancelled generation never leaks a partially generated world into the
/// caller's publish/persistence path.
/// </summary>
public readonly record struct RuntimeWorldGenerationCandidateResult(
    RuntimeWorldGenerationCandidateStatus Status,
    RuntimeWorldGenerationWorkspace? Candidate,
    WorldGenerationExecutionResult? Execution)
{
    public bool Succeeded => Status == RuntimeWorldGenerationCandidateStatus.Generated && Candidate is not null;
}

/// <summary>
/// Composition boundary between trusted-host generator registration and TerraRuntime's isolated generation engine.
/// The selected provider is captured before execution so later registration retirement cannot switch the generator
/// halfway through a job.
/// </summary>
public sealed class WorldGenerationCandidateRunner
{
    private readonly ITerraRuntimeWorldGeneratorSource generators;

    public WorldGenerationCandidateRunner(ITerraRuntimeWorldGeneratorSource generators)
    {
        ArgumentNullException.ThrowIfNull(generators);
        this.generators = generators;
    }

    public RuntimeWorldGenerationCandidateResult Generate(
        in WorldGenerationRequest request,
        IWorldGenerationProgressSink? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!generators.TryResolveWorldGenerator(request.GeneratorId, out IWorldGenerationProvider? provider) ||
            provider is null)
        {
            return new RuntimeWorldGenerationCandidateResult(
                RuntimeWorldGenerationCandidateStatus.GeneratorNotFound,
                Candidate: null,
                Execution: null);
        }

        RuntimeWorldGenerationWorkspace candidate;
        try
        {
            candidate = new RuntimeWorldGenerationWorkspace(request.WidthTiles, request.HeightTiles);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            var failed = new WorldGenerationExecutionResult(
                WorldGenerationExecutionStatus.InvalidRequest,
                Error: exception);
            return new RuntimeWorldGenerationCandidateResult(
                RuntimeWorldGenerationCandidateStatus.GenerationFailed,
                Candidate: null,
                Execution: failed);
        }

        WorldGenerationExecutionResult execution = RuntimeWorldGenerationExecutor.Execute(
            provider,
            in request,
            candidate,
            progress,
            cancellationToken);

        return execution.Succeeded
            ? new RuntimeWorldGenerationCandidateResult(
                RuntimeWorldGenerationCandidateStatus.Generated,
                candidate,
                execution)
            : new RuntimeWorldGenerationCandidateResult(
                RuntimeWorldGenerationCandidateStatus.GenerationFailed,
                Candidate: null,
                execution);
    }
}
