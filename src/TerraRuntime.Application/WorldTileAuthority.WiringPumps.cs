using TerraRuntime.Core.Worlds;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    private bool TryTransferWiringPumps(ReadOnlySpan<(int X, int Y)> intakes, ReadOnlySpan<(int X, int Y)> outlets)
    {
        if (tiles is null || liquidMutations is null || liquidSimulator is null ||
            worldItemSpawnRandom is not SystemWorldItemSpawnRandom source ||
            intakes.Length > WorldWiringCircuit1458.PumpCellCapacity || outlets.Length > WorldWiringCircuit1458.PumpCellCapacity) return false;
        var beforeRandom = source.SourceRandom.Clone(); var random = beforeRandom.Clone();
        var overlay = new ToolWorldTileOverlay1458(tiles);
        var water = liquidMutations.CreateSourceWaterPreparation(liquidSimulator);
        var frame = new ToolWorldSourcePass1458(this, overlay, random, true,
            hardModeBaseline || progression.IsCompleted(VanillaWorldProgressionId.Hardmode), default, [], null);
        foreach (var cell in intakes)
            if (!frame.TryVerifyWiringPump(cell.X, cell.Y, 142)) return false;
        foreach (var cell in outlets)
            if (!frame.TryVerifyWiringPump(cell.X, cell.Y, 143)) return false;
        foreach (var input in intakes)
        {
            if (!overlay.TryRead(input.X, input.Y, out var intake)) return false;
            if (intake.LiquidAmount == 0) continue;
            foreach (var output in outlets)
            {
                if (!overlay.TryRead(output.X, output.Y, out var outlet)) return false;
                if (!VanillaWorldLiquidMutationService.TryPlanPumpTransfer(in intake, in outlet, out var afterInput, out var afterOutput)) continue;
                if (!overlay.TryStage(output.X, output.Y, afterOutput) || !overlay.TryStage(input.X, input.Y, afterInput) ||
                    !frame.TryFrameRetainedPumpSquare(output.X, output.Y, water)) return false;
                intake = afterInput;
                if (intake.LiquidAmount == 0)
                {
                    if (!frame.TryFrameRetainedPumpSquare(input.X, input.Y, water)) return false;
                    break;
                }
            }
            // Even a blocked/full/mixed outlet pool frames every nonempty intake.
            if (!frame.TryFrameRetainedPumpSquare(input.X, input.Y, water)) return false;
        }
        if (!source.SourceRandom.HasSameState(beforeRandom) || !overlay.IsCurrentOwned || !frame.IsCurrentOwned || !water.IsCurrent) return false;
        if (!overlay.TryAdoptUnpublished() || !water.TryAdoptUnpublished())
            throw new InvalidOperationException("Prepared pump transfer lost callback-free terrain/liquid adoption.");
        source.SourceRandom.CopyStateFrom(random);
        foreach (var cell in water.NetworkChanges)
        {
            var tile = tiles.Get(cell.X, cell.Y);
            var state = new TerrariaLiquidState((short)cell.X, (short)cell.Y, tile.LiquidAmount, (byte)tile.LiquidKind);
            replication?.RetainSourceLiquid(in state);
        }
        return true;
    }
}
