using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

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
