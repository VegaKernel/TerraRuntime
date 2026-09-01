using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Contracts.Runtime;

/// <summary>
/// Describes the detached input used to materialize a world. Isolation is deliberately absent: the same source
/// descriptor can be admitted in-process or by a future dedicated worker.
/// </summary>
public abstract record SandboxWorldSource
{
    private SandboxWorldSource()
    {
    }

    public sealed record WorldFile(string AssetPath) : SandboxWorldSource;

    public sealed record Generated(
        WorldGeneratorId GeneratorId,
        string WorldName,
        ulong Seed,
        int WidthTiles,
        int HeightTiles,
        WorldGenerationOptions Options,
        string? SeedText = null) : SandboxWorldSource
    {
        public WorldGenerationRequest ToRequest() =>
            new(GeneratorId, WorldName, Seed, WidthTiles, HeightTiles)
            {
                Options = Options,
                SeedText = SeedText
            };
    }

    public sealed record Schematic(
        string AssetPath,
        int CanvasWidthTiles,
        int CanvasHeightTiles) : SandboxWorldSource;

    public sealed record SnapshotClone(WorldRuntimeIdentity Source) : SandboxWorldSource;
}
