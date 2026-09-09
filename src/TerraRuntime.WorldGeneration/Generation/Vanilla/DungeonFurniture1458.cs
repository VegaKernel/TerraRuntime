using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary DungeonGlobalGroundFurniture search and arrangements; placement remains shared with HellFort.</summary>
internal sealed class DungeonFurniture1458(Workspace workspace, IWorldGenerationVanillaRandom random,
    CancellationToken cancellationToken, IReadOnlyList<DungeonPitTraps1458.Pit>? pits = null)
{
    private static ReadOnlySpan<int> ClearanceX => [5, 4, 3, 4, 3, 5, 5, 5, 5, 5, 2, 3, 2];
    private static ReadOnlySpan<int> ClearanceY => [4, 3, 5, 6, 3, 3, 4, 4, 4, 3, 4, 3, 5];
    private static ReadOnlySpan<ushort> Types => [14, 18, 105, 101, 15, 79, 87, 88, 89, 90, 93, 100, 104];
    // ItemID.Sets.DerivedPlacementDetails, actual 1.4.5.8 executable; not item-use authority.
    private static ReadOnlySpan<int> BlueStyles => [10, 11, 46, 1, 13, 5, 11, 5, 6, 21, 24, 22, 30];
    private static ReadOnlySpan<int> GothicStyles => [14, 15, -1, 5, 17, 47, 47, 47, 51, 47, 47, 47, 48];
    private WorldTileStore Tiles => workspace.TileStore;
    private int Width => Tiles.Dimensions.WidthTiles;
    private int Height => Tiles.Dimensions.HeightTiles;

    public int Place(DungeonBounds1458 bounds, ushort baseWall, double surface)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (baseWall is < 7 or > 9 || bounds.Left < 10 || bounds.Right > Width - 10 || bounds.Bottom > Height - 10 ||
            bounds.Left >= bounds.Right || Math.Max(bounds.Top, (int)surface + 10) >= bounds.Bottom)
            throw new InvalidOperationException("Invalid ordinary dungeon furniture input.");
        float scale = (float)Width / 4200f;
        int target = (int)(2000f * scale), alchemy = 1 + (int)scale, bewitching = alchemy, specialAttempts = 2000, placed = 0;
        for (int attempt = 0; attempt < target; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (alchemy > 0 || bewitching > 0)
            {
                attempt--;
                if (--specialAttempts <= 0) break;
            }
            int x = random.Next(bounds.Left, bounds.Right), y = random.Next(Math.Max(bounds.Top, (int)surface + 10), bounds.Bottom);
            for (int retry = 999; !DungeonAir(x, y) && retry > 0; retry--)
            {
                x = random.Next(bounds.Left, bounds.Right); y = random.Next(Math.Max(bounds.Top, (int)surface + 10), bounds.Bottom);
            }
            if (!DungeonAir(x, y)) continue;
            while (!Solid(x, y) && y < Height - 200) y++;
            if (TryPlaceCandidate(x, y - 1, baseWall, ref alchemy, ref bewitching, attempt < target / 2)) placed++;
        }
        return placed;
    }

    internal bool TryPlaceCandidate(int x, int y, ushort baseWall, ref int alchemy, ref int bewitching, bool strictSpecialCheck)
    {
        int left = x, right = x;
        while (!At(left, y).IsActive && Solid(left, y + 1)) left--;
        while (!At(right, y).IsActive && Solid(right, y + 1)) right++;
        left++; right--;
        x = (left + right) / 2;
        if (!CanGenerate(x, y) || !DungeonAir(x, y) || !Solid(x, y + 1) || At(x, y + 1).Type == 48) return false;
        bool gothic = At(x, y).Wall is >= 94 and <= 105;
        int choice = random.Next(13);
        if (choice >= 10 && random.Next(4) != 0) choice = random.Next(13);
        while (gothic && choice == 2) choice = random.Next(13);
        int halfWidth = ClearanceX[choice], above = ClearanceY[choice];
        bool special = alchemy > 0 || bewitching > 0, forbidden = false, nearbySpecial = false;
        int padding = special ? 15 : 0;
        for (int tx = x - halfWidth - padding; tx <= x + halfWidth + padding; tx++)
        for (int ty = y - above - padding; ty <= y + padding; ty++)
        {
            if (tx < 0 || tx >= Width || ty < 0 || ty >= Height) continue;
            if (tx >= x - halfWidth && tx <= x + halfWidth && ty >= y - above && ty <= y)
            {
                if (!CanGenerate(tx, ty)) { forbidden = true; break; }
                if (At(tx, ty).IsActive) { choice = -1; break; }
            }
            if (strictSpecialCheck && special && At(tx, ty) is { IsActive: true, Type: 354 or 355 }) nearbySpecial = true;
        }
        if (forbidden) return false;
        if (right - left < halfWidth * 1.75f) choice = -1;
        if (!nearbySpecial && special)
        {
            ushort type = alchemy > 0 ? (ushort)355 : (ushort)354;
            PlaceObject(x, y, type, 0);
            if (At(x, y) is not { IsActive: true } || At(x, y).Type != type) return false;
            if (alchemy > 0) alchemy--; else bewitching--;
            return true;
        }
        if (choice < 0) return false;
        return Arrange(x, y, baseWall - 7, gothic, choice);
    }

    private bool Arrange(int x, int y, int palette, bool gothic, int choice)
    {
        int style = Style(choice), chairStyle = Style(4);
        ushort type = Types[choice];
        if (choice is 5 or 9) PlaceObject(x, y, type, style, random.Next(2) == 0);
        else if (choice == 4)
        {
            bool faceRight = random.Next(2) == 0;
            PlaceObject(x, y, type, style);
            if (faceRight) TurnChair(x, y);
        }
        else PlaceObject(x, y, type, style);
        if (!At(x, y).IsActive || At(x, y).Type != type) return false;
        if (choice is 0 or 1)
        {
            if (choice == 0) { Chair(x - 2, faceRight: true); Chair(x + 2, faceRight: false); }
            else if (random.Next(2) == 0) Chair(x - 1, faceRight: true); else Chair(x + 2, faceRight: false);
            int first = choice == 0 ? x - 1 : x, objectY = y - (choice == 0 ? 2 : 1);
            for (int tx = first; tx <= x + 1; tx++)
            {
                if (random.Next(2) != 0 || At(tx, objectY).IsActive) continue;
                int decoration = random.Next(5);
                bool leftLight = GenerationFurnitureTiles1458.IsLighted(At(tx - 1, objectY).Type);
                if (decoration <= 1 && !leftLight) PlaceObject(tx, objectY, 33, gothic ? 46 : 1 + palette);
                else if (decoration == 2 && !leftLight) PlaceObject(tx, objectY, 49, 0);
                else if (decoration == 3) PlaceObject(tx, objectY, 50, 0);
                else if (decoration == 4) PlaceObject(tx, objectY, 103, 0);
            }
        }
        // The vanilla bookcase branch does not report success, despite placing the object.
        return choice != 3;

        int Style(int selected) => gothic ? GothicStyles[selected] : BlueStyles[selected] + palette;
        void Chair(int px, bool faceRight)
        {
            if (At(px, y).IsActive) return;
            PlaceObject(px, y, 15, chairStyle);
            if (faceRight && At(px, y).IsActive) TurnChair(px, y);
        }
    }

    private void PlaceObject(int x, int y, ushort type, int style, bool faceRight = false)
    {
        var framing = new DungeonObjectPlacement1458(Tiles, random);
        if (type is 33 or 49 or 50) { framing.TableObject(x, y, type, style); return; }
        GenerationFurniturePlacement1458.Place(workspace, x, y, type, style, faceRight, crackedBricksSolid: false);
        if (type is not (79 or 90)) framing.FrameSquare(x, y);
    }

    private void TurnChair(int x, int y) { At(x, y).FrameX += 18; At(x, y - 1).FrameX += 18; }
    private bool CanGenerate(int x, int y) => x >= 5 && x < Width - 5 && y >= 5 && y < Height - 5 &&
        At(x, y).Wall != 350 && !DungeonPitTraps1458.Contains(pits, x, y);
    private bool DungeonAir(int x, int y) => !At(x, y).IsActive && DungeonGenerationTiles1458.IsDungeonWall(At(x, y).Wall);
    private bool Solid(int x, int y) => DungeonGenerationTiles1458.SolidTile(At(x, y));
    private ref WorldTile At(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            throw new InvalidOperationException("Dungeon furniture search left the generation workspace.");
        return ref Tiles.Tiles[Tiles.GetUncheckedIndex(x, y)];
    }
}
