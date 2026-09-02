using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.HostContracts.WorldGeneration;
using TerraRuntime.World;

namespace TerraRuntime;

public enum RuntimeWorldCreationPipelineStatus : byte
{
    ReadyToPersist = 0,
    GenerationFailed = 1,
    FinalizationFailed = 2
}

/// <summary>
/// A generated world is exposed to the persistence layer only after both the pass executor and the semantic
/// finalization gate succeed. This prevents a provider that forgot spawn/dungeon/layer anchors from leaking a
/// superficially successful but structurally incomplete candidate into a .wld writer.
/// </summary>
public readonly record struct RuntimeWorldCreationPipelineResult(
    RuntimeWorldCreationPipelineStatus Status,
    RuntimeWorldGenerationWorkspace? Candidate,
    RuntimeWorldGenerationMetadataSnapshot Metadata,
    RuntimeWorldGenerationCandidateResult Generation,
    RuntimeWorldGenerationFinalizationResult? Finalization)
{
    public bool Succeeded =>
        Status == RuntimeWorldCreationPipelineStatus.ReadyToPersist &&
        Candidate is not null &&
        Generation.Succeeded &&
        Finalization is { Succeeded: true };
}

public sealed class RuntimeWorldCreationPipeline
{
    private readonly WorldGenerationCandidateRunner generation;

    public RuntimeWorldCreationPipeline(ITerraRuntimeWorldGeneratorSource generators)
    {
        ArgumentNullException.ThrowIfNull(generators);
        generation = new WorldGenerationCandidateRunner(generators);
    }

    public RuntimeWorldCreationPipelineResult CreateCandidate(
        in WorldGenerationRequest request,
        IWorldGenerationProgressSink? progress = null,
        CancellationToken cancellationToken = default)
    {
        RuntimeWorldGenerationCandidateResult generated = generation.Generate(
            in request,
            progress,
            cancellationToken);
        if (!generated.Succeeded || generated.Candidate is null)
        {
            return new RuntimeWorldCreationPipelineResult(
                RuntimeWorldCreationPipelineStatus.GenerationFailed,
                Candidate: null,
                Metadata: default,
                generated,
                Finalization: null);
        }

        RuntimeWorldGenerationValidationMode validationMode = ResolveValidationMode(request.GeneratorId);
        RuntimeWorldGenerationFinalizationResult finalized =
            RuntimeWorldGenerationFinalizer.Finalize(generated.Candidate, validationMode);
        if (!finalized.Succeeded)
        {
            // The workspace is intentionally dropped here. A caller cannot accidentally persist a candidate whose
            // generator completed all passes but omitted required semantic anchors or failed its selected validator.
            return new RuntimeWorldCreationPipelineResult(
                RuntimeWorldCreationPipelineStatus.FinalizationFailed,
                Candidate: null,
                Metadata: default,
                generated,
                finalized);
        }

        finalized = ApplyBuiltInWorldSemantics(in request, finalized);
        return new RuntimeWorldCreationPipelineResult(
            RuntimeWorldCreationPipelineStatus.ReadyToPersist,
            generated.Candidate,
            finalized.Metadata,
            generated,
            finalized);
    }

    internal static RuntimeWorldGenerationValidationMode ResolveValidationMode(WorldGeneratorId generatorId)
    {
        if (generatorId == VanillaWorldGenerationProvider1458.GeneratorId ||
            generatorId == OptimizedSurfaceDecorationWorldGenerationProvider.GeneratorId)
        {
            return RuntimeWorldGenerationValidationMode.VanillaComplete;
        }

        // Flat, skyblock and trusted-host/custom generators still receive structural validation, but canonical
        // Terraria dimensions alone must never imply that they promise the complete vanilla biome/content contract.
        return RuntimeWorldGenerationValidationMode.GenericStructural;
    }

    private static RuntimeWorldGenerationFinalizationResult ApplyBuiltInWorldSemantics(
        in WorldGenerationRequest request,
        RuntimeWorldGenerationFinalizationResult finalized)
    {
        if (request.GeneratorId != SkyblockWorldGenerationProvider.GeneratorId)
            return finalized;

        VanillaWorldSeedProfile1458 profile = finalized.Metadata.VanillaSeedProfile;
        RuntimeWorldGenerationMetadataSnapshot metadata = finalized.Metadata with
        {
            VanillaSeedProfile = new VanillaWorldSeedProfile1458(
                profile.Special | VanillaSpecialWorldSeed1458.Skyblock,
                profile.Secret)
        };
        return finalized with { Metadata = metadata };
    }
}
