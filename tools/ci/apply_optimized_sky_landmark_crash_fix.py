from pathlib import Path

path = Path("src/TerraRuntime.World/Generation/Optimized/OptimizedLandmarkWorldGenerationProvider.cs")
text = path.read_text(encoding="utf-8")


def replace_once(old: str, new: str, label: str) -> None:
    global text
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected 1 occurrence, found {count}")
    text = text.replace(old, new)


old = """        bool[] used = new bool[islands.Count];
        for (int i = 0; i < islands.Count && state.SkyHousesPlaced < state.SkyHouseTarget; i++)
        {
            if ((i & 1) != 0 && islands.Count > state.SkyHouseTarget)
                continue;
            if (!TryBuildSkyHouse(context, chests, islands[i], state.SkyHousesPlaced))
                continue;
            used[i] = true;
            state.SkyHousesPlaced++;
        }
        for (int i = 0; i < islands.Count && state.FloatingLakesPlaced < state.FloatingLakeTarget; i++)
        {
            if (used[i] || !TryBuildFloatingLake(context.Workspace, islands[i]))
                continue;
            used[i] = true;
            state.FloatingLakesPlaced++;
        }
"""
new = """        bool[] used = new bool[islands.Count];
        for (int i = 0; i < islands.Count && state.SkyHousesPlaced < state.SkyHouseTarget; i++)
        {
            if ((i & 1) != 0 && islands.Count > state.SkyHouseTarget)
                continue;
            if (!TryBuildSkyHouse(context, chests, islands[i], state.SkyHousesPlaced))
                continue;
            used[i] = true;
            state.SkyHousesPlaced++;
        }
        // The parity preference keeps visual variety, but it is not an admission rule. A canonical world may have a
        // progression object near the preferred center of an even-index island, so retry every still-unused island
        // before declaring the explicit house budget impossible.
        for (int i = 0; i < islands.Count && state.SkyHousesPlaced < state.SkyHouseTarget; i++)
        {
            if (used[i] || !TryBuildSkyHouse(context, chests, islands[i], state.SkyHousesPlaced))
                continue;
            used[i] = true;
            state.SkyHousesPlaced++;
        }
        for (int i = 0; i < islands.Count && state.FloatingLakesPlaced < state.FloatingLakeTarget; i++)
        {
            if (used[i] || !TryBuildFloatingLake(context.Workspace, islands[i]))
                continue;
            used[i] = true;
            state.FloatingLakesPlaced++;
        }
"""
replace_once(old, new, "sky landmark role fallback")

old = """    private static bool TryBuildSkyHouse(IWorldGenerationContext context, IWorldGenerationChestWorkspace chests, SkyIslandCandidate island, int ordinal)
    {
        const int width = 13;
        const int height = 7;
        int left = island.Center - width / 2;
        int floorY = FindFirstActiveY(context.Workspace, island.Center, Math.Max(6, island.SurfaceY - 12), Math.Min(context.Workspace.HeightTiles - 6, island.SurfaceY + 18));
        if (floorY < 0 || left < 3 || left + width >= context.Workspace.WidthTiles - 3 || floorY - height < 3)
            return false;
        if (HasProtectedContentNearby(context.Workspace, island.Center, floorY - height / 2, width))
            return false;
        int top = floorY - height;
"""
new = """    private static bool TryBuildSkyHouse(IWorldGenerationContext context, IWorldGenerationChestWorkspace chests, SkyIslandCandidate island, int ordinal)
    {
        const int width = 13;
        foreach (int centerX in GetSkyPlacementCenters(island, width / 2 + 1))
        {
            if (TryBuildSkyHouseAt(context, chests, island, centerX, ordinal))
                return true;
        }
        return false;
    }

    private static bool TryBuildSkyHouseAt(
        IWorldGenerationContext context,
        IWorldGenerationChestWorkspace chests,
        SkyIslandCandidate island,
        int centerX,
        int ordinal)
    {
        const int width = 13;
        const int height = 7;
        int left = centerX - width / 2;
        int floorY = FindFirstActiveY(context.Workspace, centerX, Math.Max(6, island.SurfaceY - 12), Math.Min(context.Workspace.HeightTiles - 6, island.SurfaceY + 18));
        if (floorY < 0 || left < 3 || left + width >= context.Workspace.WidthTiles - 3 || floorY - height < 3)
            return false;
        if (HasProtectedContentNearby(context.Workspace, centerX, floorY - height / 2, width))
            return false;
        int top = floorY - height;
"""
replace_once(old, new, "bounded sky house placement")

text = text.replace("TryPlaceChest(context.Workspace, chests, island.Center - 1, floorY - 2, 13, $\"Sky Cache {ordinal + 1}\", loot)", "TryPlaceChest(context.Workspace, chests, centerX - 1, floorY - 2, 13, $\"Sky Cache {ordinal + 1}\", loot)")

old = """    private static bool TryBuildFloatingLake(IWorldGenerationWorkspace workspace, SkyIslandCandidate island)
    {
        int centerX = island.Center;
        int floorY = FindFirstActiveY(workspace, centerX, Math.Max(5, island.SurfaceY - 8), Math.Min(workspace.HeightTiles - 4, island.SurfaceY + 18));
        if (floorY < 0)
            return false;
        int halfWidth = Math.Clamp(island.Width / 7, 4, 8);
        int depth = Math.Clamp(island.Width / 16, 3, 5);
        if (HasProtectedContentNearby(workspace, centerX, floorY, halfWidth + 6))
            return false;
"""
new = """    private static bool TryBuildFloatingLake(IWorldGenerationWorkspace workspace, SkyIslandCandidate island)
    {
        int halfWidth = Math.Clamp(island.Width / 7, 4, 8);
        foreach (int centerX in GetSkyPlacementCenters(island, halfWidth + 1))
        {
            if (TryBuildFloatingLakeAt(workspace, island, centerX, halfWidth))
                return true;
        }
        return false;
    }

    private static bool TryBuildFloatingLakeAt(
        IWorldGenerationWorkspace workspace,
        SkyIslandCandidate island,
        int centerX,
        int halfWidth)
    {
        int floorY = FindFirstActiveY(workspace, centerX, Math.Max(5, island.SurfaceY - 8), Math.Min(workspace.HeightTiles - 4, island.SurfaceY + 18));
        if (floorY < 0)
            return false;
        int depth = Math.Clamp(island.Width / 16, 3, 5);
        if (HasProtectedContentNearby(workspace, centerX, floorY, halfWidth + 6))
            return false;
"""
replace_once(old, new, "bounded floating lake placement")

marker = """    private static void BuildPyramids(IWorldGenerationContext context, IWorldGenerationChestWorkspace chests, ApproximateLayers layers, LandmarkState state)
"""
helper = """    private static int[] GetSkyPlacementCenters(SkyIslandCandidate island, int halfFootprint)
    {
        int minCenter = island.Left + Math.Max(1, halfFootprint);
        int maxCenter = island.Right - Math.Max(1, halfFootprint);
        if (minCenter > maxCenter)
            return [];

        int center = Math.Clamp(island.Center, minCenter, maxCenter);
        int step = Math.Max(4, island.Width / 6);
        int[] raw =
        [
            center,
            center - step,
            center + step,
            center - 2 * step,
            center + 2 * step,
            minCenter,
            maxCenter
        ];
        var unique = new List<int>(raw.Length);
        foreach (int value in raw)
        {
            int bounded = Math.Clamp(value, minCenter, maxCenter);
            if (!unique.Contains(bounded))
                unique.Add(bounded);
        }
        return unique.ToArray();
    }

"""
if text.count(marker) != 1:
    raise SystemExit("sky placement helper insertion point missing")
text = text.replace(marker, helper + marker)

path.write_text(text, encoding="utf-8")
