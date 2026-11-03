using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal static partial class CaveHousePlacement1458
{
    private static void PlacePriorityTool(Workspace workspace, ChestPlacementState1458 state,
        IWorldGenerationVanillaRandom random, IReadOnlyList<CaveHouseRoom1458> rooms, CaveHouseType1458 type)
    {
        int maximum = state.Bootstrap?.ExtraBastStatueCountMax ??
            throw new InvalidOperationException("Cave-house placement requires Reset bootstrap state.");
        if (type != CaveHouseType1458.Desert || state.CaveHouseBastStatueCount >= maximum)
            return;
        if (TryPlaceRoomTool(workspace, random, rooms, 506, roof: false) ||
            TryPlaceRoomTool(workspace, random, rooms, 506, roof: true))
            state.CaveHouseBastStatueCount++;
    }

    private static void PlaceBiomeTool(Workspace workspace, ChestPlacementState1458 state,
        IWorldGenerationVanillaRandom random, IReadOnlyList<CaveHouseRoom1458> rooms, CaveHouseType1458 type)
    {
        if (type is not (CaveHouseType1458.Jungle or CaveHouseType1458.Desert))
            return;
        bool jungle = type == CaveHouseType1458.Jungle;
        int count = jungle ? state.CaveHouseSharpenerCount : state.CaveHouseExtractinatorCount;
        if (count >= random.Next(2, 5))
            return;
        if (!TryPlaceRoomTool(workspace, random, rooms, jungle ? (ushort)377 : (ushort)219, roof: false))
            return;
        if (jungle) state.CaveHouseSharpenerCount++;
        else state.CaveHouseExtractinatorCount++;
    }

    private static bool TryPlaceRoomTool(Workspace workspace, IWorldGenerationVanillaRandom random,
        IReadOnlyList<CaveHouseRoom1458> rooms, ushort type, bool roof)
    {
        foreach (CaveHouseRoom1458 room in rooms)
        {
            int y = roof ? room.Top - 1 : room.Bottom - 2;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                int x = room.Left + random.Next(2, room.Width - 2);
                PlaceHouseObject(workspace, random, x, y, type, forced: true);
                var tile = workspace.TileStore.Get(x, y);
                if (tile.IsActive && tile.Type == type)
                    return true;
            }
            // The deterministic fallback uses PlaceTile's return value, not the identity readback.
            for (int x = room.Left + 2; x <= room.Right - 2; x++)
                if (PlaceHouseObject(workspace, random, x, y, type, forced: true))
                    return true;
        }
        return false;
    }

    private static BuriedChestResult1458? PlaceRoomChest(Workspace workspace, ChestPlacementState1458 state,
        IWorldGenerationVanillaRandom random, IReadOnlyList<CaveHouseRoom1458> rooms, in CaveHousePalette1458 palette)
    {
        random.NextDouble(); // Every ordinary CaveHouseBiome chest chance is 1.0; the source still samples it.
        var placer = state.CreatePlacer(workspace, random);
        int configuredStyle = palette.ChestStyle;
        ushort configuredType = palette.ChestTileType;
        bool Try(int x, int y)
        {
            bool underground = y > (int)state.WorldSurface;
            return placer.TryAdd(x, y, out _, out _, chestStyle: underground ? configuredStyle : 0,
                chestTileType: underground ? configuredType : (ushort)21);
        }
        foreach (bool roof in new[] { false, true })
        foreach (CaveHouseRoom1458 room in rooms)
        {
            int y = roof ? room.Top - 1 : room.Bottom - 1;
            for (int attempt = 0; attempt < 10; attempt++)
                if (Try(room.Left + random.Next(2, room.Width - 2), y))
                    return placer.Chests[^1];
            for (int x = room.Left + 2; x <= room.Right - 2; x++)
                if (Try(x, y))
                    return placer.Chests[^1];
        }
        for (int attempt = 0; attempt < 1000; attempt++)
            if (Try(random.Next(rooms[0].Left - 30, rooms[0].Left + 30),
                random.Next(rooms[0].Top - 30, rooms[0].Top + 30)))
                return placer.Chests[^1];
        return null;
    }
}
