using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>Effective TileObjectData.CheckWaterDeath(Tile)/CheckLavaDeath(Tile), not the raw Main tables.</summary>
internal static class VanillaTileObjectLiquidDeath1458
{
    // Audited against all 389 initialized TileObjectData entries in official 1.4.5.8. Entries absent
    // from that table and ApplyNaturalObjectRules entries use Main's flags. The three custom style
    // delegates (165/185/233) also resolve only to global-check entries. Store the differences, not
    // a second copy of the entire object-placement catalog. Nothing here grants destruction authority.
    public static bool TryGet(in WorldTile tile, out bool waterDeath, out bool lavaDeath)
    {
        waterDeath = lavaDeath = false;
        if (tile.Type >= VanillaTileIds.Count) return false;
        waterDeath = tile.Type is not (372 or 405 or 646) &&
            VanillaLiquidInteractionFacts1458.IsWaterDeath(tile.TileType);
        lavaDeath = tile.Type switch
        {
            88 or 149 or 354 or 355 or 456 or 471 or 486 or 487 or 488 or 489 or 560 or 704 or 705 => false,
            4 or 20 or 82 or 83 or 84 or 358 or 359 or 360 or 361 or 362 or 363 or 364 or
            376 or 377 or 391 or 392 or 393 or 394 or 412 or 414 or 440 or 444 or 455 or 462 or 466 or
            505 or 506 or 521 or 522 or 523 or 524 or 525 or 526 or 527 or 531 or 542 or 543 or
            592 or 593 or 594 or 598 or 647 or 648 or 649 or 650 or 651 or 652 or 653 or 665 or
            693 or 694 or 696 or 706 or 713 or 714 or 715 or 716 or 733 or 751 or 752 or 753 => true,
            _ => VanillaLiquidInteractionFacts1458.IsLavaDeath(tile.TileType)
        };

        // Only these types vary their effective liquid flags with the frame-selected style.
        // Full coordinate sizes include the final row's padding (e.g. table 38, chair 40).
        (int width, int height, bool horizontal, int wrap, int multiplier, int skip) = tile.Type switch
        {
            4 => (22, 22, true, 6, 6, 1),
            10 => (18, 54, false, 36, 1, 3),
            11 => (36, 54, false, 36, 1, 2),
            14 or 469 => (54, 38, true, 1, 1, 1),
            15 or 497 => (18, 40, true, 2, 2, 1),
            18 => (36, 20, true, 1, 1, 1),
            19 => (18, 18, true, 27, 27, 1),
            33 => (18, 22, false, 1, 1, 1),
            34 => (54, 54, false, 37, 1, 2),
            79 or 90 => (72, 36, true, 2, 2, 1),
            82 => (18, 22, true, 1, 1, 1),
            87 or 89 => (54, 36, true, 1, 1, 1),
            93 => (18, 54, false, 1, 1, 2),
            100 => (36, 36, false, 1, 1, 2),
            101 => (54, 72, true, 1, 1, 1),
            104 => (36, 90, true, 1, 1, 1),
            172 => (36, 38, false, 1, 1, 1),
            215 => (54, 36, true, 16, 1, 1),
            548 => (54, 108, true, 1, 1, 1),
            _ => default
        };
        // Lantern 42's subtile 32/48 looks lava-proof, but its inherited alternate 0 is not.
        // With multiplier 1, GetTileData(Tile) always picks alternate 0. Unlike the type/style
        // overload, its effective result is therefore constant. Alternates on 128/269 have
        // Style=1 and can never match remainder 0. Other alternates preserve their subtile flags.
        if (width == 0) return true;
        if (tile.FrameX < 0 || tile.FrameY < 0) return false;
        int column = tile.FrameX / width, row = tile.FrameY / height;
        int style = skip > 1
            ? horizontal ? row / skip * wrap + column : column / skip * wrap + row
            : (horizontal ? row * wrap + column : column * wrap + row) / multiplier;
        bool immune = tile.Type switch
        {
            4 => style is 8 or 11 or 17,
            10 or 11 => style is 19 or 48,
            14 => style == 13,
            15 => style is 16 or 47,
            18 => style is 14 or 43,
            19 => style is 13 or 43 or 47,
            33 => style is 25 or 41,
            34 => style is 32 or 48,
            79 => style is 8 or 42,
            82 => style == 5,
            87 => style is 15 or 42,
            89 => style is 10 or 46,
            90 => style is 25 or 42,
            93 => style is 23 or 42,
            100 => style is 25 or 42,
            101 => style is 4 or 43,
            104 => style is 17 or 43,
            172 => style is 13 or 43,
            469 => style == 11,
            497 => style is 14 or 42,
            548 => style is 7 or 8,
            _ => false
        };
        if (immune) lavaDeath = false;
        if ((tile.TileType == VanillaTileIds.Torches && immune) || (tile.TileType == VanillaTileIds.Lamps && style == 40) ||
            (tile.TileType == VanillaTileIds.Campfire && style is 1 or 4 or 9 or 17 or 20 or 25)) waterDeath = false;
        return true;
    }
}
