using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

public enum RuntimeWorldGenerationFinalizationStatus : byte
{
    Finalized = 0,
    MissingSpawn = 1,
    MissingDungeon = 2,
    MissingLayers = 3
}

/// <summary>
/// Immutable semantic metadata captured only after a generated candidate has supplied every world anchor required by
/// the persistence/runtime publication path. Raw .wld header fields deliberately do not cross this boundary.
/// </summary>
public readonly record struct RuntimeWorldGenerationMetadataSnapshot(
    WorldGenerationPoint Spawn,
    WorldGenerationPoint Dungeon,
    WorldGenerationLayers Layers)
{
    internal VanillaWorldSeedProfile1458 VanillaSeedProfile { get; init; }
}

public readonly record struct RuntimeWorldGenerationFinalizationResult(
    RuntimeWorldGenerationFinalizationStatus Status,
    RuntimeWorldGenerationMetadataSnapshot Metadata = default)
{
    public bool Succeeded => Status == RuntimeWorldGenerationFinalizationStatus.Finalized;
}

/// <summary>
/// Fail-closed finalization gate between pass execution and persistence/publication. Custom generators must supply
/// the semantic anchors needed by a complete world; TerraRuntime will not invent format-facing defaults silently.
/// </summary>
public static class RuntimeWorldGenerationFinalizer
{
    public static RuntimeWorldGenerationFinalizationResult Finalize(RuntimeWorldGenerationWorkspace candidate)
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

        var metadata = new RuntimeWorldGenerationMetadataSnapshot(spawn, dungeon, layers)
        {
            VanillaSeedProfile = candidate.VanillaSeedProfile
        };
        return new RuntimeWorldGenerationFinalizationResult(
            RuntimeWorldGenerationFinalizationStatus.Finalized,
            metadata);
    }
}
