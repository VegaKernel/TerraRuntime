using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Flat;

/// <summary>Minimal deterministic built-in world generator used as the smallest runtime-owned baseline.</summary>
public sealed class FlatProvider : IWorldGenerationProvider
{
    public static readonly WorldGeneratorId GeneratorId = new("terraruntime:flat");
    private static readonly WorldGenerationPassId TerrainPassId = new("terraruntime:flat/terrain");
    private static readonly WorldGenerationPassId MetadataPassId = new("terraruntime:flat/metadata");

    public WorldGeneratorId Id => GeneratorId;

    public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        request.Validate();

        builder.Add(
            new WorldGenerationPassDescriptor(TerrainPassId),
            FlatTerrainPass.Instance);
        builder.Add(
            new WorldGenerationPassDescriptor(
                MetadataPassId,
                requiredAfter: [TerrainPassId]),
            FlatMetadataPass.Instance);
    }

    private sealed class FlatTerrainPass : IWorldGenerationPass
    {
        public static FlatTerrainPass Instance { get; } = new();

        private FlatTerrainPass()
        {
        }

        public void Execute(IWorldGenerationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            int width = context.Workspace.WidthTiles;
            int height = context.Workspace.HeightTiles;
            int surface = CalculateSurface(height);
            int rockLayer = CalculateRockLayer(height, surface);
            int progressStride = Math.Max(1, width / 100);

            for (int x = 0; x < width; x++)
            {
                if ((x & 63) == 0)
                    context.CancellationToken.ThrowIfCancellationRequested();

                for (int y = surface; y < height; y++)
                {
                    ushort type = y < rockLayer ? (ushort)0 : (ushort)1;
                    var tile = new WorldGenerationTile(
                        Type: type,
                        Wall: 0,
                        FrameX: 0,
                        FrameY: 0,
                        Flags: WorldGenerationTileFlags.Active,
                        LiquidAmount: 0,
                        TileColor: 0,
                        WallColor: 0,
                        Shape: 0,
                        LiquidKind: WorldGenerationLiquidKind.Water);
                    if (!context.Workspace.TrySetTile(x, y, in tile))
                    {
                        throw new InvalidOperationException(
                            $"Flat generator could not write tile ({x}, {y}) inside a {width}x{height} workspace.");
                    }
                }

                if (x % progressStride == 0 || x == width - 1)
                    context.ReportProgress((x + 1d) / width, "Building flat terrain");
            }
        }
    }

    private sealed class FlatMetadataPass : IWorldGenerationPass
    {
        public static FlatMetadataPass Instance { get; } = new();

        private FlatMetadataPass()
        {
        }

        public void Execute(IWorldGenerationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            IWorldGenerationMetadataWorkspace metadata = context.Metadata ??
                throw new InvalidOperationException("The runtime candidate workspace does not expose world metadata.");

            int width = context.Workspace.WidthTiles;
            int height = context.Workspace.HeightTiles;
            int surface = CalculateSurface(height);
            int rockLayer = CalculateRockLayer(height, surface);
            int spawnY = Math.Max(0, surface - 1);
            int dungeonX = width > 1 ? Math.Max(0, width / 8) : 0;

            if (!metadata.TrySetSpawn(width / 2, spawnY))
                throw new InvalidOperationException("Flat generator could not set a valid spawn point.");
            if (!metadata.TrySetDungeon(dungeonX, spawnY))
                throw new InvalidOperationException("Flat generator could not set valid dungeon anchor.");
            if (!metadata.TrySetLayers(surface, rockLayer))
                throw new InvalidOperationException("Flat generator could not set valid world layers.");

            context.ReportProgress(1d, "Finalizing flat world anchors");
        }
    }

    private static int CalculateSurface(int height)
    {
        if (height <= 2)
            return 1;

        return Math.Clamp((int)Math.Floor(height * 0.40d), 1, height - 2);
    }

    private static int CalculateRockLayer(int height, int surface)
    {
        if (height <= 2)
            return Math.Min(height - 1, surface + 1);

        int proposed = (int)Math.Floor(height * 0.65d);
        return Math.Clamp(proposed, surface + 1, height - 1);
    }
}
