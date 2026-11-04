using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class LivingTreeGrower1458
{
    private bool dungeonTree;
    private byte dungeonPaint;

    /// <summary>Default DungeonUtils.GenerateDungeonTree, borrowing the living-tree geometry and canonical workspace.</summary>
    internal void GenerateDungeonTree(int x, int y, int attachmentY, bool generateRoots, int platformStyle)
    {
        if (x < 20 || y < 20 || x >= width - 20 || y >= height - 20) return;
        if (workspace is null || !workspace.HasVanillaGenerationSolidity)
            throw new NotSupportedException("Dungeon tree generation requires the canonical generation workspace.");
        cancellation.ThrowIfCancellationRequested();
        int row = y;
        while (At(x, row).IsActive || At(x, row).Wall > 0 ||
            At(x, row - 1).IsActive || At(x, row - 1).Wall > 0 ||
            At(x, row - 2).IsActive || At(x, row - 2).Wall > 0 ||
            At(x, row - 3).IsActive || At(x, row - 3).Wall > 0 ||
            At(x, row - 4).IsActive || At(x, row - 4).Wall > 0)
        {
            if (--row < 50) break;
        }
        if (row <= 50) return;

        dungeonTree = true;
        dungeonPaint = workspace.VanillaSeedProfile.Has(VanillaSpecialWorldSeed1458.NotTheBees) ? (byte)0 : (byte)28;
        try
        {
            int left = x - random.Next(2, 3), right = x + random.Next(2, 3);
            if (random.Next(5) == 0)
            {
                if (random.Next(2) == 0) left--;
                else right++;
            }
            int trunkWidth = right - left, baseLeft = left, baseRight = right;
            var anchorX = new int[MaximumBranchAnchors1458];
            var anchorY = new int[MaximumBranchAnchors1458];
            var direction = new int[MaximumBranchAnchors1458];
            var length = new int[MaximumBranchAnchors1458];
            var canopyX = new int[MaximumCanopyAnchors1458];
            var canopyY = new int[MaximumCanopyAnchors1458];
            var round = new bool[MaximumCanopyAnchors1458];
            int branches = 0, canopies = 0;
            int top = GrowTrunk(ref left, ref right, row, anchorX, anchorY, direction, length, ref branches);
            GrowBranches(anchorX, anchorY, direction, length, branches, canopyX, canopyY, round, ref canopies);
            GrowCrown((left + right) / 2, top, trunkWidth, canopyX, canopyY, round, ref canopies);
            if (generateRoots) GrowRoots(baseLeft, baseRight, row, trunkWidth);
            if (!workspace.VanillaSeedProfile.Has(VanillaSpecialWorldSeed1458.Remix))
                GrowCanopy(canopyX, canopyY, round, canopies, trunkWidth);
            var passage = new LivingTreePassage1458(store, random, worldSurface, underworldTop, cancellation, workspace: workspace);
            passage.MakeDungeonPassage(row, attachmentY, trunkWidth, ref baseLeft, ref baseRight, platformStyle);
            workspace.SetVanillaGenerationSolid(VanillaTileIds.Spikes, true);
        }
        finally
        {
            // This is operation context on the existing grower; the source's world phase remains unchanged on a fault.
            dungeonTree = false;
            dungeonPaint = 0;
        }
    }
}
