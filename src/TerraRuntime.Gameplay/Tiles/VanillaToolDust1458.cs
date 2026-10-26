using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Tiles;

public readonly record struct ToolDustTile1458(TileTypeId Type, short FrameX, short FrameY);

/// <summary>
/// Caller-side RNG of the original tile dust helper when particle allocation is
/// suppressed. Dedicated-server Dust.NewDust returns before consuming RNG; the
/// surrounding MakeTileDust expressions still execute. This does not model a
/// visible client's dust pool, screen bounds, or visual particles.
/// </summary>
public static class VanillaToolDust1458
{
    public static bool TryGetAmount(in ToolDustTile1458 tile, bool fail, out int amount)
    {
        amount = 0;
        if (tile.Type.Value >= VanillaToolTileCatalog1458.TileCount)
            return false;
        amount = tile.Type.Value switch
        {
            125 or 138 or 172 or 287 or >= 300 and <= 308 or 354 or 355 or
            >= 373 and <= 376 or 461 or 484 or 499 or 564 or 565 or 593 or
            594 or 617 or 630 or 631 or 654 or 664 or 665 or 699 or 709 or
            >= 711 and <= 716 => 0,
            231 => fail ? 3 : 6,
            481 or 482 or 483 => 3,
            541 => 1,
            549 => 2,
            534 or 536 or 539 or 625 or 627 => fail ? 3 : 5,
            184 when tile.FrameX / 22 >= 5 => 5,
            _ => fail ? 3 : 10
        };
        return true;
    }

    public static bool TryAdvanceSuppressedParticle(in ToolDustTile1458 tile,
        Func<int, int, int> nextInteger)
    {
        ArgumentNullException.ThrowIfNull(nextInteger);
        int type = tile.Type.Value;
        if ((uint)type >= VanillaToolTileCatalog1458.TileCount)
            return false;

        // These calls select visual variants, but remain source RNG effects even
        // when Dust.NewDust itself is a no-op. Branch order follows the helper.
        if (type is >= 583 and <= 589)
            nextInteger(0, 10);
        else if (type == 633)
            nextInteger(0, 6);
        else if (type == 634)
        {
            nextInteger(0, 10);
            nextInteger(0, 12);
        }
        if (type is 641 or 370 or 171)
            nextInteger(0, 2);
        if (type == 19 && tile.FrameY / 18 == 30)
            nextInteger(0, 3);
        if (type == 254 && tile.FrameX >= 72)
            nextInteger(0, tile.FrameX < 108 ? 3 : tile.FrameX < 144 ? 2 : 4);
        if (type is 2 or 477)
            nextInteger(0, 2);
        if (type is 204 or 478 or 243 or 244)
            nextInteger(0, 2);
        if (HasFurnitureVariantRoll(type))
            nextInteger(0, 3);
        if (type is 231 or 238)
            nextInteger(0, 3);
        if (type is 109 or 492 or 58 or 76 or 77 or 684 or 37 or 32)
            nextInteger(0, 2);
        if (type == 352)
            nextInteger(0, 3);
        if (type is 23 or 24 or 661)
            nextInteger(0, 2);
        if (type == 25 || (type is 31 or 696 && tile.FrameX < 36))
            nextInteger(0, 2);
        if (type is 26 or 695 && tile.FrameX < 54)
            nextInteger(0, 2);
        if (type == 27)
            nextInteger(0, 2);
        if (type == 385)
            nextInteger(68, 71);
        if (type == 35 && tile.FrameX < 36)
            nextInteger(0, 2);
        // The subsequent visual-id override does not undo this draw.
        if (type is 34 or 42)
            nextInteger(0, 2);
        if (type == 314)
        {
            nextInteger(-2, 3);
            nextInteger(-2, 3);
            nextInteger(0, 51);
        }
        if (type == 454)
            nextInteger(0, 4);
        return true;
    }

    public static bool TryAdvanceSuppressedWallParticle(WallTypeId wall,
        Func<int, int, int> nextInteger)
    {
        ArgumentNullException.ThrowIfNull(nextInteger);
        int type = wall.Value;
        const int WallCount = 367;
        if ((uint)type >= WallCount) return false;
        if (type is 3 or 246 or 69 or 264 or 83 or 234 or 269 or 200 or 202 or
            288 or 290 or 203 or 291 or 208 or 209 or 210 or 211 or >= 296 and <= 299 or 182 or 144 or 27)
            nextInteger(0, 2);
        if (type == 186) nextInteger(68, 71);
        if (type == 93) nextInteger(88, 94);
        return true;
    }

    private static bool HasFurnitureVariantRoll(int type) => type is
        >= 358 and <= 364 or >= 275 and <= 282 or 285 or 286 or
        >= 288 and <= 297 or >= 316 and <= 318 or 298 or 299 or 309 or 310 or
        339 or 538 or 413 or 414 or 505 or 521 or 522 or 523 or 524 or 525 or
        526 or 527 or 532 or 543 or 544 or 550 or 551 or 533 or 553 or 554 or
        555 or 556 or 558 or 559 or 542 or 391 or 394 or 392 or 393 or 568 or
        569 or 570 or 582 or 580 or >= 598 and <= 612 or 619 or 620 or 629 or
        632 or 640 or 643 or 644 or 645 or 710;
}
