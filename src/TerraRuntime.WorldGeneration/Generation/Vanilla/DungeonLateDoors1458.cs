using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary style-zero LateDualDungeonFeatures door restyling, after banners.</summary>
internal sealed class DungeonLateDoors1458(WorldTileStore tiles, IWorldGenerationVanillaRandom random,
    CancellationToken cancellationToken)
{
    public int Apply(DungeonBounds1458 progressionBounds)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int width = tiles.Dimensions.WidthTiles, height = tiles.Dimensions.HeightTiles;
        if (progressionBounds.Left < 10 || progressionBounds.Top < 10 || progressionBounds.Right > width - 10 ||
            progressionBounds.Bottom > height - 10) throw new InvalidOperationException("Invalid ordinary Dungeon late-feature bounds.");
        int changed = 0;
        var framing = new DungeonObjectPlacement1458(tiles, random);
        for (int x = progressionBounds.Left; x <= progressionBounds.Right; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int bottom = progressionBounds.Top; bottom <= progressionBounds.Bottom; bottom++)
            {
                WorldTile door = At(x, bottom);
                if (!door.IsActive || door.Type != 10 || At(x, bottom + 1) is { IsActive: true, Type: 10 } || door.Wall is < 94 or > 105) continue;
                int style = door.FrameY / 54 + door.FrameX / 54 * 36;
                if (style is not (0 or 13 or 16 or 17 or 18 or 53) || door.FrameY < 0 || door.FrameY % 54 != 36)
                    throw new InvalidOperationException("Unverified ordinary Dungeon door destruction style or fragment.");
                for (int row = 0; row < 3; row++)
                {
                    WorldTile part = At(x, bottom - 2 + row);
                    if (!part.IsActive || part.Type != 10 || part.FrameX < 0 || part.FrameX % 18 != 0 ||
                        part.FrameY != style % 36 * 54 + row * 18 || part.FrameX / 54 != style / 36)
                        throw new InvalidOperationException("Incoherent door during late Dungeon generation.");
                }
                // KillTile(bottom) -> CheckDoorClosed -> remaining top/middle. Item.NewItem refuses
                // generation-time drops; these styles have no random dust/secondary tile mutation.
                Clear(x, bottom); Clear(x, bottom - 2); Clear(x, bottom - 1);
                framing.FrameSquare(x, bottom - 2); framing.FrameSquare(x, bottom - 1); framing.FrameSquare(x, bottom);
                DungeonGenerationTiles1458.PlaceEntranceDoor(tiles, random, x, bottom, style: 53);
                changed++;
            }
        }
        return changed;
    }

    private void Clear(int x, int y)
    {
        ref WorldTile cell = ref At(x, y);
        cell.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.Inactive | WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        cell.Type = 0; cell.Shape = 0; cell.TileColor = 0; cell.FrameX = cell.FrameY = -1;
    }
    private ref WorldTile At(int x, int y) => ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
}
