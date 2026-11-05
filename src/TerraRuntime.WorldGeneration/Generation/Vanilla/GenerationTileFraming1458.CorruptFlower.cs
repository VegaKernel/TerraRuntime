using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    // TerrariaServer 1.4.5.8 WorldGen.PlaceTile(24), restricted to the
    // FinishNotTheBees evil-grass replacement's former jungle plant over
    // unsloped Corrupt grass 661. Wall0 and wall207 are the source-reachable
    // branches covered by the direct official microfixtures; other inputs
    // fail before mutation instead of guessing TileObjectData/flower rules.
    internal bool TryPlaceCorruptFlower24(int x, int y)
    {
        if (!Contains(x, y) || !Contains(x, y + 1))
            throw new NotSupportedException("Corrupt flower placement needs its full support footprint.");
        ref WorldTile flower = ref At(x, y);
        WorldTile support = At(x, y + 1);
        bool honeyWall15Refusal = flower.Wall == 15 && flower.LiquidAmount == 255 &&
            flower.LiquidKind == WorldLiquidKind.Honey;
        if (flower.IsActive || flower.Type != 61 ||
            (flower.Wall is not (0 or 207) && !honeyWall15Refusal) ||
            (support.Wall != 0 && (flower.Wall != 207 || support.Wall != 207)) ||
            !support.IsActive || support.Type != 661 ||
            support.IsActuated || support.IsHalfBrick || support.SourceSlope != 0)
            throw new NotSupportedException("Corrupt flower placement input is outside the qualified source branch.");

        // Source PlaceTile checks Collision.EmptyTile and liquid before
        // clearing the inactive former plant. In particular a liquid refusal
        // preserves its old type/frame and spends no RNG.
        if (!EmptyPlacementTile(x, y) || flower.LiquidAmount != 0 ||
            store.LiquidUpdates.IsCheckingLiquid(x, y))
            return false;
        HellFortGenerator1458.ClearPlacementAnchor(ref flower);
        if (random.Next(13) == 0)
        {
            flower.Flags |= WorldTileFlags.Active;
            flower.Type = 32;
            SquareTileFrame(x, y);
        }
        else if (flower.Wall == 0)
        {
            flower.Flags |= WorldTileFlags.Active;
            flower.Type = 24;
            if (random.Next(50) == 0 || random.Next(40) == 0)
                flower.FrameX = 144;
            else if (random.Next(35) == 0)
                flower.FrameX = (short)(FlowerStyles[random.Next(FlowerStyles.Length)] * 18);
            else
                flower.FrameX = (short)(random.Next(6) * 18);
        }
        return FinishPlacement(x, y);
    }

    private static readonly int[] FlowerStyles =
        [6, 7, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20];
}
