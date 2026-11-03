using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    private sealed class FramingAbortException : Exception { }

    private void ConsumeTileDust(int x, int y, in WorldTile target)
    {
        // KillTile_MakeTileDust reads a tree's ground before spending dust or removing the
        // tile. Its unbounded source scan can fail in an air column (including Skyblock).
        // Model that observed abort explicitly rather than silently clearing the tree.
        if (target.Type is 5 or 323 && x > 5 && x < width - 5)
        {
            int column = x;
            if (target.Type == 5)
            {
                if (target.FrameX == 66 && target.FrameY <= 45) column++;
                if (target.FrameX == 88 && target.FrameY is >= 66 and <= 110) column--;
                if (target.FrameX == 22 && target.FrameY is >= 132 and <= 176) column--;
                // Two separate source tests both match this root band.
                if (target.FrameX == 44 && target.FrameY is >= 132 and <= 176) column += 2;
                if (target.FrameX == 44 && target.FrameY >= 198) column++;
                if (target.FrameX == 66 && target.FrameY >= 198) column--;
            }
            int row = y;
            while (row < height && Contains(column, row))
            {
                WorldTile ground = At(column, row);
                if (ground.IsActive && IsFramingSolid(ground.TileType))
                    break;
                row++;
            }
            if (row >= height || !Contains(column, row))
                throw new FramingAbortException();
        }
        if(target.Type==254&&target.FrameX>=72)
        {
            int choices=target.FrameX<108?3:target.FrameX<144?2:4;
            // Each of the ten pumpkin dust particles chooses between its two source dust identities.
            // The dedicated generation context discards the particle after this appearance decision.
            for(int particle=0;particle<10;particle++)random.Next(choices);
        }
        GenerationKillTileDust1458.Consume(random, target.Type, target.FrameX);
    }
}
