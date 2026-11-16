namespace TerraRuntime.World;

public sealed partial class VanillaWorldLiquidMutationService
{
    // Wiring.XferWater1458: empty outlets adopt the input kind; a drained
    // input loses its liquid flags. Mixed nonempty kinds never react here.
    internal static bool TryPlanPumpTransfer(in WorldTile intake, in WorldTile outlet,
        out WorldTile intakeAfter, out WorldTile outletAfter)
    {
        intakeAfter = intake; outletAfter = outlet;
        if (intake.LiquidAmount == 0 || outlet.LiquidAmount == byte.MaxValue ||
            outlet.LiquidAmount != 0 && outlet.LiquidKind != intake.LiquidKind) return false;
        int moved = Math.Min(intake.LiquidAmount, byte.MaxValue - outlet.LiquidAmount);
        intakeAfter.LiquidAmount = (byte)(intake.LiquidAmount - moved);
        outletAfter.LiquidAmount = (byte)(outlet.LiquidAmount + moved);
        outletAfter.LiquidKind = intake.LiquidKind;
        if (intakeAfter.LiquidAmount == 0) intakeAfter.LiquidKind = WorldLiquidKind.Water;
        return true;
    }
}
