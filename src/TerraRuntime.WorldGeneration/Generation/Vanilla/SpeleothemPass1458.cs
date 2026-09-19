using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 <c>GenPassNameID.SpeleothemsAndGemTrees</c> together with the helpers
/// only it uses: <c>WorldGen.PlaceTight</c>, <c>WorldGen.PlaceUncheckedStalactite</c>,
/// <c>WorldGen.CheckStalactite</c> and the stalagmite style resolution behind it.
/// </summary>
/// <remarks>
/// <para>
/// This is another whole-map scan rather than a sampling loop. For every column from twenty to
/// <c>maxTilesX - 20</c> the pass walks the rows below <c>worldSurface</c> and, on a one-in-five draw, offers a
/// gem tree of a randomly chosen one of the seven identities, then on a second one-in-five draw offers a
/// speleothem to any open cell that is not ocean depth. A second walk over the rows ABOVE the surface offers
/// speleothems to ice and to the two evil stones, which is what puts them in a surface ice cave.
/// </para>
/// <para>
/// A speleothem's frame is chosen by what it hangs from or stands on, not by where it is: snow and ice give one
/// atlas, stone and moss another, hive, sandstone, granite and marble their own. The ceiling form has a small
/// and a tall variant; the floor form has the same pair but no snow entry, because snow grows no stalagmite.
/// Every placement is then re-validated by <c>CheckStalactite</c>, which can both restyle it - drawing one
/// value when the style it has differs from the style its support implies - and destroy it outright.
/// </para>
/// </remarks>
internal sealed class SpeleothemPass1458(
    WorldTileStore store,
    IWorldGenerationVanillaRandom random,
    double worldSurface,
    double rockLayer,
    int beachDistance,
    CancellationToken cancellation)
{
    private const ushort Speleothem = 165;
    private const ushort Stone = 1;
    private const ushort Ebonstone = 25;
    private const ushort Hive = 225;
    private const ushort Pearlstone = 117;
    private const ushort Crimstone = 203;
    private const ushort Snow = 147;
    private const ushort Ice = 161;
    private const ushort PinkIce = 163;
    private const ushort PurpleIce = 164;
    private const ushort FleshBlock = 200;
    private const ushort Marble = 367;
    private const ushort Granite = 368;
    private const ushort Sandstone = 396;
    private const ushort HardenedSand = 397;
    private const ushort Shimmer231 = 231;

    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;

    /// <summary>Source <c>WorldGen.oceanLevel</c>.</summary>
    private double OceanLevel => ((worldSurface + rockLayer) / 2.0) + 40.0;

    /// <summary>Speleothems placed, and gem trees grown.</summary>
    public long Speleothems { get; private set; }

    public long GemTrees { get; private set; }

    public void Apply()
    {
        for (int x = 20; x < width - 20; x++)
        {
            cancellation.ThrowIfCancellationRequested();

            for (int y = (int)worldSurface; y < height - 20; y++)
            {
                if (random.Next(5) == 0 && LiquidAt(x, y - 1) == 0)
                {
                    // The identity is drawn before the attempt, so the draw happens whether or not a tree grows.
                    ushort tree = random.Next(7) switch
                    {
                        0 => 583,
                        1 => 584,
                        2 => 585,
                        3 => 586,
                        4 => 587,
                        5 => 588,
                        _ => 589,
                    };

                    if (SettingsTreeGrower1458.TryGrow(
                            store, SettingsTreeGrower1458.GemTree(tree), x, y, random))
                    {
                        GemTrees++;
                    }
                }

                if (OceanDepths(x, y) || IsActive(x, y) || random.Next(5) != 0)
                    continue;

                // The two flattening steps: a sloped support directly above or below an isolated open cell is
                // squared off first, so the speleothem has something flat to attach to.
                if (IsSpeleothemStone(TypeAt(x, y - 1)) && !IsActive(x, y) && !IsActive(x, y + 1))
                    At(x, y - 1).Shape = 0;
                if (IsSpeleothemStone(TypeAt(x, y + 1)) && !IsActive(x, y) && !IsActive(x, y - 1))
                    At(x, y + 1).Shape = 0;

                PlaceTight(x, y);
            }

            for (int y = 5; y < (int)worldSurface; y++)
            {
                if (IsActive(x, y - 1) && TypeAt(x, y - 1) is Snow or Ice && random.Next(5) == 0)
                {
                    if (!IsActive(x, y) && !IsActive(x, y + 1))
                        At(x, y - 1).Shape = 0;
                    PlaceTight(x, y);
                }

                if (IsActive(x, y - 1) && TypeAt(x, y - 1) is Ebonstone or Crimstone && random.Next(5) == 0)
                {
                    if (!IsActive(x, y) && !IsActive(x, y + 1))
                        At(x, y - 1).Shape = 0;
                    PlaceTight(x, y);
                }

                if (IsActive(x, y + 1) && TypeAt(x, y + 1) is Ebonstone or Crimstone && random.Next(5) == 0)
                {
                    if (!IsActive(x, y) && !IsActive(x, y - 1))
                        At(x, y + 1).Shape = 0;
                    PlaceTight(x, y);
                }
            }
        }
    }

    /// <summary>
    /// Source <c>WorldGen.PlaceTight</c>. Shimmer and the shimmer block refuse outright; everything else draws
    /// the small/tall choice and the three-way variation before the site is even examined, so those two values
    /// leave the stream whether or not a speleothem appears.
    /// </summary>
    /// <summary>
    /// Source <c>WorldGen.PlaceTight</c>. Exposed because it is a free function in the source and the Webs And
    /// Honey pass calls the same one for the speleothems it grows inside a hive.
    /// </summary>
    internal void PlaceTight(int x, int y)
    {
        if (!Contains(x, y - 1) || !Contains(x, y + 1))
            return;

        WorldTile cell = At(x, y);
        if ((cell.LiquidAmount > 0 && cell.LiquidKind == WorldLiquidKind.Shimmer) ||
            (cell.IsActive && cell.Type == Shimmer231))
        {
            return;
        }

        PlaceUncheckedStalactite(x, y, random.Next(2) == 0, random.Next(3));
        if (IsActive(x, y) && TypeAt(x, y) == Speleothem)
            CheckStalactite(x, y);
    }

    /// <summary>
    /// Source <c>WorldGen.PlaceUncheckedStalactite</c> without its spider-cave arm, which only the spider biome
    /// passes. The substrate decides the atlas column; small hangs one cell, tall hangs two.
    /// </summary>
    private void PlaceUncheckedStalactite(int x, int y, bool preferSmall, int variation)
    {
        variation = Math.Clamp(variation, 0, 2);

        if (IsSolid(x, y - 1) && !IsActive(x, y) && !IsActive(x, y + 1))
        {
            ushort support = TypeAt(x, y - 1);
            int ceilingBase = CeilingAtlas(support);
            if (ceilingBase < 0)
                return;

            // Hive only ever grows the small form.
            if (support == Hive || preferSmall)
            {
                WriteSpeleothem(x, y, ceilingBase + variation * 18, 72, x, y - 1);
                return;
            }

            WriteSpeleothem(x, y, ceilingBase + variation * 18, 0, x, y - 1);
            WriteSpeleothem(x, y + 1, ceilingBase + variation * 18, 18, x, y - 1);
            return;
        }

        if (!IsSolid(x, y + 1) || IsActive(x, y) || IsActive(x, y - 1))
            return;

        ushort floorSupport = TypeAt(x, y + 1);
        int floorBase = FloorAtlas(floorSupport);
        if (floorBase < 0)
            return;

        if (floorSupport == Hive || preferSmall)
        {
            WriteSpeleothem(x, y, floorBase + variation * 18, 90, x, y + 1);
            return;
        }

        WriteSpeleothem(x, y - 1, floorBase + variation * 18, 36, x, y + 1);
        WriteSpeleothem(x, y, floorBase + variation * 18, 54, x, y + 1);
    }

    /// <summary>The ceiling atlas column per support, or -1 where the source writes nothing.</summary>
    private static int CeilingAtlas(ushort support) => support switch
    {
        Snow or Ice or PinkIce or PurpleIce or FleshBlock => 0,
        Stone or Pearlstone or Ebonstone or Crimstone => 54,
        Hive => 162,
        Sandstone or HardenedSand => 378,
        Granite => 432,
        Marble => 486,
        _ => IsMoss(support) ? 54 : -1,
    };

    /// <summary>
    /// The floor atlas column. Snow and ice are absent on purpose: the source writes no stalagmite for them,
    /// which is why an icy cave has hanging speleothems and a bare floor.
    /// </summary>
    private static int FloorAtlas(ushort support) => support switch
    {
        Stone or Pearlstone or Ebonstone or Crimstone => 54,
        Hive => 162,
        Sandstone or HardenedSand => 378,
        Granite => 432,
        Marble => 486,
        _ => IsMoss(support) ? 54 : -1,
    };

    private void WriteSpeleothem(int x, int y, int frameX, int frameY, int supportX, int supportY)
    {
        if (!Contains(x, y))
            return;

        WorldTile support = Contains(supportX, supportY) ? At(supportX, supportY) : default;
        ref WorldTile cell = ref At(x, y);
        cell.Type = Speleothem;
        cell.Flags |= WorldTileFlags.Active;
        cell.Shape = 0;
        cell.FrameX = checked((short)frameX);
        cell.FrameY = checked((short)frameY);
        cell.TileColor = support.TileColor;
        cell.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        cell.Flags |= support.Flags & (WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        Speleothems++;
    }

    /// <summary>
    /// Source <c>WorldGen.CheckStalactite</c> for the two single-cell forms. A speleothem whose support has
    /// gone, or whose style cannot be resolved, is removed; one whose style merely differs is restyled, and
    /// that restyle is the pass's only draw outside the scan itself.
    /// </summary>
    private void CheckStalactite(int x, int j)
    {
        if (!Contains(x, j - 1) || !Contains(x, j + 1))
            return;

        short frameY = At(x, j).FrameY;
        if (frameY == 72)
        {
            bool broken = !IsSolid(x, j - 1);
            if (!broken && !UpdateStalagmiteStyle(x, j))
                broken = true;
            if (broken)
                KillSpeleothem(x, j);
            return;
        }

        if (frameY == 90)
        {
            bool broken = !IsSolid(x, j + 1) || IsBoulder(x, j + 1);
            if (!broken && !UpdateStalagmiteStyle(x, j))
                broken = true;
            if (broken)
                KillSpeleothem(x, j);
            return;
        }

        // The tall floor form: frame rows 36 and 54 are one object standing on the cell below the pair, and the
        // source validates it as a pair - same identity, same atlas column - before restyling it.
        if (frameY >= 36)
        {
            int top = frameY == 54 ? j - 1 : j;
            if (!Contains(x, top) || !Contains(x, top + 2))
                return;

            bool broken = !IsSolid(x, top + 2) ||
                !IsActive(x, top) || !IsActive(x, top + 1) ||
                TypeAt(x, top + 1) != TypeAt(x, top) ||
                At(x, top + 1).FrameX != At(x, top).FrameX;
            if (!broken && IsBoulder(x, top + 2))
                broken = true;
            if (!broken && !UpdateStalagmiteStyle(x, top))
                broken = true;
            if (broken)
            {
                KillSpeleothem(x, top);
                KillSpeleothem(x, top + 1);
            }

            return;
        }

        // The tall ceiling form: frame rows 0 and 18, hanging from the cell above the pair.
        {
            int top = frameY == 18 ? j - 1 : j;
            if (!Contains(x, top - 1) || !Contains(x, top + 1))
                return;

            bool broken = !IsSolid(x, top - 1) ||
                !IsActive(x, top) || !IsActive(x, top + 1) ||
                TypeAt(x, top + 1) != TypeAt(x, top) ||
                At(x, top + 1).FrameX != At(x, top).FrameX;
            if (!broken && !UpdateStalagmiteStyle(x, top))
                broken = true;
            if (broken)
            {
                KillSpeleothem(x, top);
                KillSpeleothem(x, top + 1);
            }
        }
    }

    /// <summary>
    /// Source <c>WorldGen.UpdateStalagtiteStyle</c>. The one draw happens only when the style the cell carries
    /// is not the style its support implies, which is why a freshly placed speleothem usually costs nothing.
    /// </summary>
    private bool UpdateStalagmiteStyle(int x, int j)
    {
        if (!TryGetStalagmiteStyle(x, j, out int style))
            return false;
        if (!TryGetDesiredStalagmiteStyle(x, j, out int desired, out int runHeight, out int top))
            return false;
        if (style == desired)
            return true;

        int frameX = random.Next(3) * 18;
        frameX += desired switch
        {
            0 => 54,
            1 => 216,
            2 => 270,
            3 => 324,
            4 => 378,
            5 => 432,
            6 => 486,
            7 => 0,
            8 => 540,
            9 => 594,
            10 => 648,
            11 => 108,
            12 => 162,
            _ => 0,
        };

        for (int row = top; row < top + runHeight; row++)
        {
            if (Contains(x, row))
                At(x, row).FrameX = checked((short)frameX);
        }

        return true;
    }

    /// <summary>Source <c>WorldGen.GetStalagtiteStyle</c>: the atlas column read back as a style index.</summary>
    private bool TryGetStalagmiteStyle(int x, int y, out int style)
    {
        style = 0;
        if (!Contains(x, y))
            return false;

        switch (At(x, y).FrameX / 54)
        {
            case 0: style = 7; return true;
            case 1: style = 0; return true;
            case 2: style = 11; return true;
            case 3: style = 12; return true;
            case 4: style = 1; return true;
            case 5: style = 2; return true;
            case 6: style = 3; return true;
            case 7: style = 4; return true;
            case 8: style = 5; return true;
            case 9: style = 6; return true;
            case 10: style = 8; return true;
            case 11: style = 9; return true;
            case 12: style = 10; return true;
            default: return false;
        }
    }

    /// <summary>
    /// Source <c>WorldGen.GetDesiredStalagtiteStyle</c>. The frame row says which of the four forms the cell is
    /// part of, and that in turn says which neighbour is the support whose identity picks the style.
    /// </summary>
    private bool TryGetDesiredStalagmiteStyle(int x, int j, out int desired, out int runHeight, out int top)
    {
        desired = 0;
        runHeight = 1;
        top = j;
        if (!Contains(x, j))
            return false;

        WorldTile cell = At(x, j);
        int form;
        ushort support;
        if (cell.FrameY == 72)
        {
            form = 0;
            support = TypeAtRaw(x, top - 1);
        }
        else if (cell.FrameY == 90)
        {
            form = 1;
            support = TypeAtRaw(x, top + 1);
        }
        else if (cell.FrameY >= 36)
        {
            if (cell.FrameY == 54)
                top--;
            runHeight = 2;
            form = 4;
            support = TypeAtRaw(x, top + 2);
        }
        else
        {
            if (cell.FrameY == 18)
                top--;
            runHeight = 2;
            form = 3;
            support = TypeAtRaw(x, top - 1);
        }

        if (support == Stone || IsMoss(support))
        {
            desired = 0;
            if (form == 3 && cell.Wall == 62)
                desired = 11;
            return true;
        }

        switch (support)
        {
            case FleshBlock: desired = 10; return true;
            case PurpleIce: desired = 8; return true;
            case PinkIce: desired = 9; return true;
            case Pearlstone or 402 or 403: desired = 1; return true;
            case Ebonstone or 398 or 400: desired = 2; return true;
            case Crimstone or 399 or 401: desired = 3; return true;
            case Sandstone or HardenedSand: desired = 4; return true;
            case Marble: desired = 6; return true;
            case Granite: desired = 5; return true;
            case Snow or Ice: desired = 7; return true;
        }

        if (form is 0 or 1 && support == Hive)
        {
            desired = 12;
            return true;
        }

        return false;
    }

    private void KillSpeleothem(int x, int y)
    {
        if (!Contains(x, y) || !IsActive(x, y) || TypeAt(x, y) != Speleothem)
            return;

        // The dust a broken tile makes is paid for out of the shared stream. A speleothem's costs nothing -
        // its dust identity is not one of the randomised ones - but the cost is asked for rather than assumed.
        GenerationKillTileDust1458.Consume(random, At(x, y).Type);
        ref WorldTile cell = ref At(x, y);
        cell.Flags &= ~WorldTileFlags.Active;
        cell.Shape = 0;
        cell.FrameX = -1;
        cell.FrameY = -1;
        cell.TileColor = 0;
        cell.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        cell.Type = 0;
        Speleothems--;
    }

    /// <summary>Source <c>WorldGen.oceanDepths</c>.</summary>
    private bool OceanDepths(int x, int y)
    {
        if (y > OceanLevel)
            return false;
        return x < beachDistance || x > width - beachDistance;
    }

    /// <summary>The substrates the scan squares off before offering a speleothem.</summary>
    private static bool IsSpeleothemStone(ushort type) =>
        type is Stone or Snow or Ice or Ebonstone or Crimstone || IsMoss(type);

    /// <summary>Source <c>Main.tileMoss</c>.</summary>
    private static bool IsMoss(ushort type) =>
        type is 179 or 180 or 181 or 182 or 183 or 381 or 534 or 536 or 539 or 625 or 627;

    /// <summary>Source <c>InvalidTileForPilesOrSpeleothems</c>, the boulder set.</summary>
    private bool IsBoulder(int x, int y) =>
        Contains(x, y) && At(x, y).IsActive && At(x, y).Type is 138 or 664;

    private bool IsSolid(int x, int y)
    {
        if (!Contains(x, y))
            return true;

        WorldTile tile = At(x, y);
        return tile.IsActive && VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
            !tile.IsActuated && tile.Shape < 2 && tile.Shape != 1;
    }

    private bool IsActive(int x, int y) => Contains(x, y) && At(x, y).IsActive;

    private ushort TypeAt(int x, int y) => Contains(x, y) && At(x, y).IsActive ? At(x, y).Type : (ushort)0;

    /// <summary>The style resolver reads the raw identity, active or not, exactly as the source does.</summary>
    private ushort TypeAtRaw(int x, int y) => Contains(x, y) ? At(x, y).Type : (ushort)0;

    private int LiquidAt(int x, int y) => Contains(x, y) ? At(x, y).LiquidAmount : 0;

    private bool Contains(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height;

    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
