namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    private bool breakingFossil;

    private void ShatterNeighbouringFossils(int x, int y, bool failed)
    {
        if (breakingFossil) return;
        // The registered generation callers keep their destruction sites inside this footprint.
        // An unknown edge caller must not silently truncate the source's neighbour traversal.
        if (!Contains(x - 1, y - 1) || !Contains(x + 1, y + 2))
            throw new NotSupportedException("Fossil shattering requires its complete interior footprint.");
        breakingFossil = true;
        for (int column = x - 1; column <= x + 1; column++)
            for (int row = y - 1; row <= y + 1; row++)
            {
                int chance = !SolidTile(column, row + 1) || column == x && row == y - 1 && !failed ? 4 : 15;
                if ((column != x || row != y) && At(column, row).IsActive &&
                    At(column, row).Type == 404 && random.Next(chance) == 0)
                    KillTile(column, row);
            }
        breakingFossil = false;
    }
}
