using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Runtime;

public enum FinalizationStatus : byte
{
    Finalized = 0,
    MissingSpawn = 1,
    MissingDungeon = 2,
    MissingLayers = 3,
    ValidationFailed = 4
}

/// <summary>
/// Selects the semantic validation contract applied after a generator has completed all passes but before its
/// workspace can cross the persistence/publication boundary. Automatic preserves the historical direct-finalizer
/// behavior; runtime-owned startup selection must choose an explicit profile so custom generators are not mistaken
/// for vanilla merely because they use a canonical Terraria world size.
/// </summary>
public enum ValidationMode : byte
{
    Automatic = 0,
    GenericStructural = 1,
    VanillaComplete = 2
}

public readonly record struct FinalizationResult(
    FinalizationStatus Status,
    RuntimeWorldGenerationMetadataSnapshot Metadata = default,
    WorldValidationResult? Validation = null)
{
    public bool Succeeded => Status == FinalizationStatus.Finalized;
}

/// <summary>
/// Fail-closed finalization gate between pass execution and persistence/publication. Custom generators must supply
/// the semantic anchors needed by a complete world; TerraRuntime will not invent format-facing defaults silently.
/// </summary>
public static class Finalizer
{
    public static FinalizationResult Finalize(
        Workspace candidate,
        ValidationMode validationMode = ValidationMode.Automatic)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (!candidate.TryGetSpawn(out WorldGenerationPoint spawn))
        {
            return new FinalizationResult(
                FinalizationStatus.MissingSpawn);
        }

        if (!candidate.TryGetDungeon(out WorldGenerationPoint dungeon))
        {
            return new FinalizationResult(
                FinalizationStatus.MissingDungeon);
        }

        if (!candidate.TryGetLayers(out WorldGenerationLayers layers))
        {
            return new FinalizationResult(
                FinalizationStatus.MissingLayers);
        }

        var metadata = new RuntimeWorldGenerationMetadataSnapshot(
            spawn,
            dungeon,
            layers,
            candidate.VanillaSeedProfile)
        {
            VanillaBootstrapState = candidate.VanillaBootstrapState
        };

        WorldValidationResult validation = validationMode switch
        {
            ValidationMode.GenericStructural =>
                StructuralValidator.Validate(candidate, metadata),
            ValidationMode.Automatic or
            ValidationMode.VanillaComplete =>
                Validator1458.Validate(candidate, metadata),
            _ => throw new ArgumentOutOfRangeException(nameof(validationMode), validationMode, "Unknown world-generation validation mode.")
        };
        if (!validation.IsValid)
        {
            return new FinalizationResult(
                FinalizationStatus.ValidationFailed,
                metadata,
                validation);
        }

        return new FinalizationResult(
            FinalizationStatus.Finalized,
            metadata,
            validation);
    }
}
