using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary DungeonGlobalTraps and the dart-trap branch of WorldGen.placeTrap (1.4.5.8).</summary>
internal sealed class DungeonTraps1458(WorldTileStore tiles, IWorldGenerationVanillaRandom random,
    WorldGenerationPoint shimmer, CancellationToken cancellationToken)
{
    private int Width => tiles.Dimensions.WidthTiles;
    private int Height => tiles.Dimensions.HeightTiles;

    public int Place(DungeonBounds1458 bounds, double surface)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bounds.Left < 10 || bounds.Right > Width - 10 || bounds.Left >= bounds.Right || surface < 10 ||
            surface >= bounds.Bottom || bounds.Bottom > Height - 10)
            throw new InvalidOperationException("Invalid ordinary dungeon trap input.");
        int target = (int)(8.4f * ((float)Width / 4200f)), attempts = 0, completed = 0, placed = 0;
        while (completed < target)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempts++;
            int x = random.Next(bounds.Left, bounds.Right), y = random.Next((int)surface, bounds.Bottom);
            if (DungeonGenerationTiles1458.IsDungeonWall(At(x, y).Wall) && TryPlace(x, y))
            {
                attempts = 1000; placed++;
            }
            // A success schedules another attempt; the source uses >1000, not >=1000.
            if (attempts > 1000) { completed++; attempts = 0; }
        }
        return placed;
    }

    internal bool TryPlace(int x, int y)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (At(x, y).Wall == 350 || Math.Sqrt(Math.Pow(x - (double)shimmer.X, 2) + Math.Pow(y - (double)shimmer.Y, 2)) < 100) return false;
        int floor = y;
        bool deep = false;
        while (!Solid(x, floor))
        {
            if (++floor > Height - 10 || At(x, floor).Wall == 350) return false;
            if (floor >= Height - 300) deep = true;
        }
        int plateY = floor - 1;
        for (int tx = Math.Max(0, x - 20); tx <= Math.Min(Width - 1, x + 20); tx++)
        for (int ty = Math.Max(0, plateY - 20); ty <= Math.Min(Height - 1, plateY + 20); ty++)
            if (At(tx, ty) is { IsActive: true, Type: 70 }) return false;
        WorldTile plate = At(x, plateY);
        if (plate.Wall is 87 or 350 || plate.LiquidAmount > 0 && plate.LiquidKind == WorldLiquidKind.Lava ||
            deep || x < 3 || x >= Width - 3 || plateY < 3 || plateY >= Height - 3) return false;
        for (int tx = x - 1; tx <= x + 1; tx++)
        for (int ty = plateY - 2; ty <= plateY; ty++)
            if (At(tx, ty) is { IsActive: true, IsActuated: false }) return false;
        WorldTile support = At(x, floor);
        if (support.Type is 48 or 232 || GenerationObjectSupport1458.IsBoulder(support.Type) || support.Wall == 350) return false;
        int trapY = plateY - random.Next(3), left = x, right = x;
        while (!Solid(left, trapY) && !Cracked(left, trapY))
            if (--left < 0 || At(left, trapY).Wall == 350) return false;
        while (!Solid(right, trapY) && !Cracked(right, trapY))
            if (++right >= Width || At(right, trapY).Wall == 350) return false;
        bool allowLeft = Anchor(left, x - left), allowRight = Anchor(right, right - x);
        if (!allowLeft && !allowRight) return false;
        int trapX = allowLeft && allowRight ? (random.Next(2) == 0 ? right : left) : allowLeft ? left : right;
        if (At(trapX, trapY).Type == 190 || At(trapX, trapY).Wall == 350) return false;
        int plateStyle = plate.Wall > 0 ? 2 : random.Next(2, 4);
        PrepareAnchor(x, plateY);
        if (!At(x, plateY).IsActive)
        {
            At(x, plateY).Flags |= WorldTileFlags.Active; At(x, plateY).Type = 135;
            At(x, plateY).FrameY = (short)(plateStyle * 18);
        }
        new DungeonObjectPlacement1458(tiles, random).FrameSquare(x, plateY);
        KillAnchor(trapX, trapY);
        PrepareAnchor(trapX, trapY);
        At(trapX, trapY).Flags |= WorldTileFlags.Active; At(trapX, trapY).Type = 137; At(trapX, trapY).FrameY = 0;
        // PlaceTile(137) frames its neighbors after installing the solid trap, not only after KillTile.
        new DungeonObjectPlacement1458(tiles, random).FrameSquare(trapX, trapY);
        if (trapX == left) At(trapX, trapY).FrameX += 18;
        int wireX = x, wireY = plateY;
        while (wireX != trapX || wireY != trapY)
        {
            At(wireX, wireY).Flags |= WorldTileFlags.WireRed;
            wireX += Math.Sign(trapX - wireX); At(wireX, wireY).Flags |= WorldTileFlags.WireRed;
            wireY += Math.Sign(trapY - wireY); At(wireX, wireY).Flags |= WorldTileFlags.WireRed;
        }
        return true;

        bool Anchor(int tx, int distance) => distance is > 5 and < 50 && Solid(tx, trapY + 1) &&
            At(tx, trapY) is not { IsActive: true, Type: 10 or 48 } && At(tx, trapY + 1) is not { IsActive: true, Type: 10 or 48 };
    }

    private void KillAnchor(int x, int y)
    {
        ref WorldTile cell = ref At(x, y);
        if (!cell.IsActive) return;
        WorldTile above = At(x, y - 1), below = At(x, y + 1);
        if (LockedDoor(below)) return;
        // MakeDungeon disables cracked-brick solidity, so the solid-block/above-object gate does not
        // apply to this branch. Its eight-neighbor RNG walk still runs during generation.
        if (cell.Type is >= 481 and <= 483) { KillCracked(x, y); return; }
        if (!WorldSmoothingCatalog1458.CanRemoveTileBelow(above, cell.TileType) ||
            above.IsActive && (above.Type is 21 or 88 or 467 or 468 or 470 or 475 or 441 or 77 || LockedDoor(above))) return;
        // Other materials can consume random dust or own object records and require separate evidence.
        if (cell.Type is not (0 or 1 or 41 or 43 or 44 or 53 or 59 or 137 or 147 or 161))
            throw new InvalidOperationException($"Unverified dungeon trap anchor destruction {cell.Type} at {x},{y}.");
        cell.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.Inactive | WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        cell.Type = 0; cell.Shape = 0; cell.TileColor = 0; cell.FrameX = cell.FrameY = -1;
        new DungeonObjectPlacement1458(tiles, random).FrameSquare(x, y);
    }

    private void KillCracked(int x, int y)
    {
        // An explicit DFS preserves source recursive order without putting a connected cracked floor
        // on the process call stack. Each child is deactivated before visiting another child.
        var pending = new List<(int X, int Y, int Neighbor)> { (x, y, 0) };
        ReadOnlySpan<(int X, int Y)> offsets = [(-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, -1), (-1, 1), (1, 1)];
        var framing = new DungeonObjectPlacement1458(tiles, random);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var step = pending[^1];
            if (step.Neighbor < offsets.Length)
            {
                pending[^1] = (step.X, step.Y, step.Neighbor + 1);
                int tx = step.X + offsets[step.Neighbor].X, ty = step.Y + offsets[step.Neighbor].Y;
                WorldTile neighbor = At(tx, ty);
                // The draw precedes the TYPE check, but follows active(), even for an ordinary brick.
                if (neighbor.IsActive && random.Next(step.Neighbor == 2 ? 3 : 6) == 0 && neighbor.Type is >= 481 and <= 483)
                {
                    At(step.X, step.Y).Flags &= ~WorldTileFlags.Active;
                    if (!LockedDoor(At(tx, ty + 1))) pending.Add((tx, ty, 0));
                }
                continue;
            }
            // The source additionally spawns transient projectiles736..738. Their NewProjectile path
            // consumes no RNG and neither ticks nor persists in this unpublished world-file pass.
            // Do not inject them into the live projectile authority while constructing a candidate.
            ref WorldTile cell = ref At(step.X, step.Y);
            cell.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.Inactive | WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
            cell.Type = 0; cell.Shape = 0; cell.TileColor = 0; cell.FrameX = cell.FrameY = -1;
            framing.FrameSquare(step.X, step.Y);
            pending.RemoveAt(pending.Count - 1);
        }
    }

    private void PrepareAnchor(int x, int y)
    {
        ref WorldTile cell = ref At(x, y);
        if (!cell.IsActive) HellFortGenerator1458.ClearPlacementAnchor(ref cell);
        else if (!VanillaWorldFrameImportance326.IsFrameImportant(cell.Type))
        {
            if (cell.Shape == 1) cell.Shape = 0;
            cell.FrameX = cell.FrameY = 0;
        }
    }

    private static bool LockedDoor(WorldTile cell) => cell.IsActive && cell.Type == 10 && cell.FrameY is >= 594 and <= 646 && cell.FrameX < 54;
    private bool Solid(int x, int y) => At(x, y).Type is not (379 or 481 or 482 or 483) && HellFortGenerator1458.Solid(At(x, y), noDoors: false);
    private bool Cracked(int x, int y) => At(x, y) is { IsActive: true, Type: 481 or 482 or 483 };
    private ref WorldTile At(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            throw new InvalidOperationException("Dungeon trap search left the generation workspace.");
        return ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
    }
}
