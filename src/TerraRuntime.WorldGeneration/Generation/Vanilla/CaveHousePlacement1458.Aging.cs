using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal static partial class CaveHousePlacement1458
{
    internal static void AgeRooms(Workspace workspace, IWorldGenerationVanillaRandom random,
        CaveHouseType1458 type, ReadOnlySpan<CaveHouseRoom1458> rooms)
    {
        foreach (CaveHouseRoom1458 room in rooms) AgeRoom(workspace, random, type, room);
    }

    /// <summary>The seven ordinary house aging profiles, sharing the retained tile and wall framers.</summary>
    internal static void AgeRoom(Workspace workspace, IWorldGenerationVanillaRandom random,
        CaveHouseType1458 type, CaveHouseRoom1458 room)
    {
        if (!workspace.TryGetLayers(out var layers))
            throw new InvalidOperationException("Cave-house aging requires retained world layers.");
        var aging = new AgingRoom(workspace, random);
        CaveHousePalette1458 palette = ResolvePalette(type);
        bool underground = room.Y > layers.WorldSurface;

        if (type == CaveHouseType1458.Wood)
        {
            for (int patch = 0; patch < room.Width * room.Height / 16; patch++)
            {
                int x = room.X + random.Next(1, room.Width - 1);
                int y = room.Y + random.Next(1, room.Height - 1);
                aging.Scatter(new CaveHouseRoom1458(x, y, 2, 2), 0.5, 0.3, (column, row) =>
                {
                    if (!aging.At(column, row).IsActive) aging.SetTile(column, row, 51, keepWall: false, frame: true);
                });
            }
            aging.Scatter(room, 0.85, 0.3, (x, y) =>
            {
                if (aging.At(x, y).Wall != palette.WallType || aging.SkipWall(x, y)) return;
                if (underground) aging.ClearWall(x, y, neighbors: true);
                else aging.Walls.PlaceWall(x, y, 2);
            });
            aging.Scan(room, (x, y) =>
            {
                if (random.NextDouble() >= 0.95 && aging.At(x, y) is { IsActive: true, Type: 30 or 321 or 158 })
                    aging.ClearTile(x, y);
            });
            return;
        }

        ushort aged = type switch
        {
            CaveHouseType1458.Ice => 161,
            CaveHouseType1458.Desert => 396,
            CaveHouseType1458.Jungle => 60,
            CaveHouseType1458.Mushroom => 70,
            CaveHouseType1458.Granite => 368,
            CaveHouseType1458.Marble => 367,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        double failure = type == CaveHouseType1458.Desert ? 0.8 : type == CaveHouseType1458.Mushroom ? 0.7 : 0.6;
        double blotches = type == CaveHouseType1458.Desert ? 0.2 : type == CaveHouseType1458.Mushroom ? 0.5 : 0.6;
        aging.Scatter(room, failure, blotches, (x, y) =>
        {
            if (!aging.At(x, y).IsActive || aging.At(x, y).Type != palette.TileType) return;
            aging.SetTile(x, y, aged, keepWall: true, frame: true);
            if (type is CaveHouseType1458.Ice or CaveHouseType1458.Desert or CaveHouseType1458.Jungle)
            {
                double secondFailure = type == CaveHouseType1458.Desert ? 0.5 : 0.8;
                if (random.NextDouble() >= secondFailure)
                    aging.SetTile(x, y, type == CaveHouseType1458.Ice ? (ushort)147 :
                        type == CaveHouseType1458.Jungle ? (ushort)59 : (ushort)397, keepWall: true, frame: true);
            }
        });

        for (int edge = 0; edge < 2; edge++)
        {
            int y = edge == 0 ? room.Y : room.Bottom - 1;
            for (int x = room.X + 1; x < room.Right - 1; x++)
            {
                double edgeFailure = type == CaveHouseType1458.Mushroom ? 0.6 :
                    type is CaveHouseType1458.Granite or CaveHouseType1458.Marble ? 0.8 : 0.5;
                if (random.NextDouble() < edgeFailure) continue;
                WorldTile support = aging.At(x, y);
                if (!support.IsActive || !(type == CaveHouseType1458.Desert ? support.Type is 396 or 397 : support.Type == aged)) continue;
                int targetY = y + (type == CaveHouseType1458.Mushroom ? -1 : 1);
                if (type == CaveHouseType1458.Mushroom)
                {
                    if (!aging.At(x, targetY).IsActive) aging.SetTile(x, targetY, 71, keepWall: false, frame: false);
                }
                else if (type == CaveHouseType1458.Jungle)
                {
                    if (aging.At(x, targetY).IsActive) continue;
                    int length = random.Next(3, room.Height + 1);
                    for (int row = targetY; row < targetY + length && !aging.At(x, row).IsActive; row++)
                    {
                        ref WorldTile vine = ref aging.At(x, row);
                        vine.Type = 62;
                        vine.Flags |= WorldTileFlags.Active;
                    }
                }
                else aging.Speleothems.PlaceTight(x, targetY);
            }
        }

        double wallFailure = type == CaveHouseType1458.Desert ? 0.8 : 0.85;
        double wallBlotches = type == CaveHouseType1458.Ice ? 0.8 : 0.3;
        aging.Scatter(room, wallFailure, wallBlotches, (x, y) =>
        {
            if (type == CaveHouseType1458.Ice)
            {
                if (aging.SkipWall(x, y)) return;
                if (underground) aging.ClearWall(x, y, neighbors: true);
                else aging.Walls.PlaceWall(x, y, 40);
            }
            else if (type == CaveHouseType1458.Mushroom) aging.ClearWall(x, y, neighbors: false);
            else
            {
                if (type == CaveHouseType1458.Desert && aging.At(x, y).Wall != palette.WallType) return;
                ushort wall = type switch
                {
                    CaveHouseType1458.Desert => 216,
                    CaveHouseType1458.Jungle => 64,
                    CaveHouseType1458.Granite => 180,
                    CaveHouseType1458.Marble => 178,
                    _ => 0
                };
                aging.Walls.PlaceWall(x, y, wall);
            }
        });
    }

    /// <summary>Transient traversal over the existing candidate; all mutations go to its retained store.</summary>
    private sealed class AgingRoom(Workspace workspace, IWorldGenerationVanillaRandom random)
    {
        private readonly GenerationTileFraming1458 tiles = new(workspace.TileStore, random, workspace, useWorldGenerationSolidity: workspace.HasVanillaGenerationSolidity);
        public GenerationWallFraming1458 Walls { get; } = new(workspace.TileStore, random);
        public SpeleothemPass1458 Speleothems { get; } = new(workspace.TileStore, random, 0, 0, 0, CancellationToken.None);
        public ref WorldTile At(int x, int y) => ref workspace.TileStore.Tiles[workspace.TileStore.GetUncheckedIndex(x, y)];

        public void Scan(CaveHouseRoom1458 area, Action<int, int> action)
        {
            for (int x = area.Left; x < area.Right; x++)
                for (int y = area.Top; y < area.Bottom; y++) action(x, y);
        }

        public void Scatter(CaveHouseRoom1458 area, double failure, double chance, Action<int, int> action)
        {
            Scan(area, (x, y) =>
            {
                if (random.NextDouble() < failure) return;
                random.NextDouble(); // Blotches consumes this discarded sample before testing its chance.
                if (random.NextDouble() >= chance) { action(x, y); return; }
                int left = random.Next(-1, 1), right = random.Next(0, 2);
                int top = random.Next(-1, 1), bottom = random.Next(0, 2);
                for (int dx = left; dx <= right; dx++)
                    for (int dy = top; dy <= bottom; dy++) action(x + dx, y + dy);
            });
        }

        public bool SkipWall(int x, int y) => At(x, y) is { IsActive: true, Type: 245 or 246 or 240 or 241 or 242 };

        public void ClearWall(int x, int y, bool neighbors)
        {
            At(x, y).Wall = 0;
            if (!neighbors) return;
            Walls.SquareWallFrame(x + 1, y);
            Walls.SquareWallFrame(x - 1, y);
            Walls.SquareWallFrame(x, y + 1);
            Walls.SquareWallFrame(x, y - 1);
        }

        public void ClearTile(int x, int y)
        {
            CaveHousePlacement1458.ClearTile(ref At(x, y));
            tiles.TileFrame(x + 1, y);
            tiles.TileFrame(x - 1, y);
            tiles.TileFrame(x, y + 1);
            tiles.TileFrame(x, y - 1);
        }

        public void SetTile(int x, int y, ushort type, bool keepWall, bool frame)
        {
            ref WorldTile tile = ref At(x, y);
            ushort wall = keepWall ? tile.Wall : (ushort)0;
            WorldTileFlags wires = tile.Flags & (WorldTileFlagMasks.Wires | WorldTileFlagMasks.Actuation);
            tile = new WorldTile { Type = type, Wall = wall, Flags = wires | WorldTileFlags.Active };
            if (!frame) return;
            tiles.TileFrame(x, y, resetFrame: true);
            tiles.TileFrame(x + 1, y, resetFrame: true);
            tiles.TileFrame(x - 1, y, resetFrame: true);
            tiles.TileFrame(x, y + 1, resetFrame: true);
            tiles.TileFrame(x, y - 1, resetFrame: true);
        }
    }
}
