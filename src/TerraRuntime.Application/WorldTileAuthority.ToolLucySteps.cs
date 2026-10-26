using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class ToolActionSteps1458
    {
        private bool TryStageLucyMessage(ToolTileHitPreparation1458 hit,
            in RuntimePlayerToolStagePose1458 pose, RuntimePlayerToolInput1458? input)
        {
            var observed = input?.LucyReport;
            if (!hit.RequiresLucyMessage) return observed is null;
            if (expectedLucy is not null) return Reject();
            // Player.ActuallyUseMiningTool -> LucyAxeMessage (1458). Gem-tree
            // membership is the exact seven-entry TileID.Sets.CountsAsGemTree.
            byte source = hit.Before.Type == 80 ? (byte)6 :
                hit.Before.Type is >= 583 and <= 589 ? (byte)5 : (byte)4;
            if (source == 6 && observed is null) return true;
            var expected = new TerrariaLucyAxeMessageState1458(source,
                observed?.Variation ?? 0, (pose.Player.ControlFlags & 64) != 0 ? 7f : -7f, -7f,
                (int)(pose.Player.PositionX + pose.Width * .5f), (int)pose.Player.PositionY);
            if (observed is { } report && (report != expected || input!.LucyObservationIndex != expectedCount))
                return Reject();
            expectedLucy = expected;
            expectedLucyIndex = expectedCount;
            // Create resets the client UI's idle cooldown before local KillTile.
            // Only a fully matched action adopts this preview. Cactus receipt
            // presence witnesses a source-possible UI branch; no420-tick history
            // or variation counter is reconstructed from tool clocks.
            localRandom.Next(7200, 14400);
            return true;
        }
    }
}
