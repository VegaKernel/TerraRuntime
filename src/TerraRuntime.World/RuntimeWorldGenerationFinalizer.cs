using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

public enum RuntimeWorldGenerationFinalizationStatus : byte
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
public enum RuntimeWorldGenerationValidationMode : byte
{
    Automatic = 0,
    GenericStructural = 1,
    VanillaComplete = 2
}

/// <summary>
/// Immutable semantic metadata captured only after a generated candidate has supplied every world anchor required by
/// the persistence/runtime publication path. Raw .wld header fields deliberately do not cross this boundary.
/// </summary>
public readonly record struct RuntimeWorldGenerationMetadataSnapshot(
    WorldGenerationPoint Spawn,
    WorldGenerationPoint Dungeon,
    WorldGenerationLayers Layers,
    VanillaWorldSeedProfile1458 VanillaSeedProfile = default)
{
    /// <summary>
    /// Runtime-internal Terraria 1.4.5.8 fresh-world state captured by the source-backed Reset bootstrap. Generic
    /// custom generators leave this null and retain the conservative canonical fresh-world defaults.
    /// </summary>
    internal VanillaWorldGenerationBootstrapState1458? VanillaBootstrapState { get; init; }
}

public readonly record struct RuntimeWorldGenerationFinalizationResult(
    RuntimeWorldGenerationFinalizationStatus Status,
    RuntimeWorldGenerationMetadataSnapshot Metadata = default,
    VanillaWorldValidationResult? Validation = null)
{
    public bool Succeeded => Status == RuntimeWorldGenerationFinalizationStatus.Finalized;
}

/// <summary>
/// Fail-closed finalization gate between pass execution and persistence/publication. Custom generators must supply
/// the semantic anchors needed by a complete world; TerraRuntime will not invent format-facing defaults silently.
/// </summary>
public static class RuntimeWorldGenerationFinalizer
{
    public static RuntimeWorldGenerationFinalizationResult Finalize(
        RuntimeWorldGenerationWorkspace candidate,
        RuntimeWorldGenerationValidationMode validationMode = RuntimeWorldGenerationValidationMode.Automatic)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (!candidate.TryGetSpawn(out WorldGenerationPoint spawn))
        {
            return new RuntimeWorldGenerationFinalizationResult(
                RuntimeWorldGenerationFinalizationStatus.MissingSpawn);
        }

        if (!candidate.TryGetDungeon(out WorldGenerationPoint dungeon))
        {
            return new RuntimeWorldGenerationFinalizationResult(
                RuntimeWorldGenerationFinalizationStatus.MissingDungeon);
        }

        if (!candidate.TryGetLayers(out WorldGenerationLayers layers))
        {
            return new RuntimeWorldGenerationFinalizationResult(
                RuntimeWorldGenerationFinalizationStatus.MissingLayers);
        }

        var metadata = new RuntimeWorldGenerationMetadataSnapshot(
            spawn,
            dungeon,
            layers,
            candidate.VanillaSeedProfile)
        {
            VanillaBootstrapState = candidate.VanillaBootstrapState
        };

        VanillaWorldValidationResult validation = validationMode switch
        {
            RuntimeWorldGenerationValidationMode.GenericStructural =>
                RuntimeWorldGenerationStructuralValidator.Validate(candidate, metadata),
            RuntimeWorldGenerationValidationMode.Automatic or
            RuntimeWorldGenerationValidationMode.VanillaComplete =>
                VanillaWorldGenerationValidator1458.Validate(candidate, metadata),
            _ => throw new ArgumentOutOfRangeException(nameof(validationMode), validationMode, "Unknown world-generation validation mode.")
        };
        if (!validation.IsValid)
        {
            return new RuntimeWorldGenerationFinalizationResult(
                RuntimeWorldGenerationFinalizationStatus.ValidationFailed,
                metadata,
                validation);
        }

        return new RuntimeWorldGenerationFinalizationResult(
            RuntimeWorldGenerationFinalizationStatus.Finalized,
            metadata,
            validation);
    }
}
