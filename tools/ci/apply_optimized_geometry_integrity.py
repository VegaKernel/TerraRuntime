from pathlib import Path

root = Path('.')

common = root / 'src/TerraRuntime.World/Generation/Common/WorldGenerationGeometry.cs'
common.parent.mkdir(parents=True, exist_ok=True)
common.write_text(r'''using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

internal static class WorldGenerationGeometry
{
    internal readonly record struct OceanBasinIntegrity(int SampledColumns, int WetColumns, int FlooredColumns, int MinimumSolidDepth);

    public static int FindFirstActiveY(IWorldGenerationWorkspace workspace, int x, int startInclusive, int endInclusive)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if ((uint)x >= (uint)workspace.WidthTiles)
            return -1;
        int start = Math.Clamp(startInclusive, 0, workspace.HeightTiles - 1);
        int end = Math.Clamp(endInclusive, start, workspace.HeightTiles - 1);
        for (int y = start; y <= end; y++)
        {
            if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) && (tile.Flags & WorldGenerationTileFlags.Active) != 0)
                return y;
        }
        return -1;
    }

    public static bool IsClearRectangle(IWorldGenerationWorkspace workspace, int left, int top, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (width <= 0 || height <= 0 || left < 1 || top < 1 || left + width > workspace.WidthTiles - 1 || top + height > workspace.HeightTiles - 1)
            return false;
        for (int x = left; x < left + width; x++)
        for (int y = top; y < top + height; y++)
        {
            if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile) || (tile.Flags & WorldGenerationTileFlags.Active) != 0 || tile.LiquidAmount != 0)
                return false;
        }
        return true;
    }

    public static void FillSolidHorizontal(IWorldGenerationWorkspace workspace, int left, int right, int y, ushort type, ushort? wall = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if ((uint)y >= (uint)workspace.HeightTiles)
            return;
        int boundedLeft = Math.Max(0, Math.Min(left, right));
        int boundedRight = Math.Min(workspace.WidthTiles - 1, Math.Max(left, right));
        for (int x = boundedLeft; x <= boundedRight; x++)
            SetSolid(workspace, x, y, type, wall);
    }

    public static void BuildOceanColumn(IWorldGenerationWorkspace workspace, int x, int waterTop, int floorY, ushort floorType, int solidDepth)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if ((uint)x >= (uint)workspace.WidthTiles)
            throw new ArgumentOutOfRangeException(nameof(x));
        int top = Math.Clamp(waterTop, 1, workspace.HeightTiles - 2);
        int floor = Math.Clamp(floorY, top + 1, workspace.HeightTiles - 2);
        for (int y = top; y < floor; y++)
            SetWater(workspace, x, y);
        int bottom = Math.Min(workspace.HeightTiles - 1, floor + Math.Max(1, solidDepth) - 1);
        for (int y = floor; y <= bottom; y++)
            SetSolid(workspace, x, y, floorType, null);
    }

    public static OceanBasinIntegrity InspectOceanBasin(IWorldGenerationWorkspace workspace, bool left, int oceanWidth, int scanTop, int scanBottom, int solidDepthProbe)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        int width = Math.Clamp(oceanWidth, 1, workspace.WidthTiles);
        int top = Math.Clamp(scanTop, 0, workspace.HeightTiles - 2);
        int bottom = Math.Clamp(scanBottom, top + 1, workspace.HeightTiles - 1);
        int step = Math.Max(1, width / 32);
        int sampled = 0, wet = 0, floored = 0, minSolid = int.MaxValue;
        for (int local = 0; local < width; local += step)
        {
            int x = left ? local : workspace.WidthTiles - 1 - local;
            sampled++;
            bool sawWater = false;
            int floor = -1;
            for (int y = top; y <= bottom; y++)
            {
                if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile))
                    continue;
                if (tile.LiquidAmount > 0 && tile.LiquidKind == WorldGenerationLiquidKind.Water)
                {
                    sawWater = true;
                    continue;
                }
                if (sawWater && (tile.Flags & WorldGenerationTileFlags.Active) != 0)
                {
                    floor = y;
                    break;
                }
            }
            if (sawWater) wet++;
            if (floor < 0) continue;
            floored++;
            int solid = 0;
            int probeBottom = Math.Min(workspace.HeightTiles - 1, floor + Math.Max(1, solidDepthProbe) - 1);
            for (int y = floor; y <= probeBottom; y++)
            {
                if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile) || (tile.Flags & WorldGenerationTileFlags.Active) == 0)
                    break;
                solid++;
            }
            minSolid = Math.Min(minSolid, solid);
        }
        return new OceanBasinIntegrity(sampled, wet, floored, minSolid == int.MaxValue ? 0 : minSolid);
    }

    public static void RequireOceanBasin(IWorldGenerationWorkspace workspace, bool left, int oceanWidth, int scanTop, int scanBottom, int minimumSolidDepth)
    {
        OceanBasinIntegrity i = InspectOceanBasin(workspace, left, oceanWidth, scanTop, scanBottom, minimumSolidDepth);
        if (i.WetColumns != i.SampledColumns || i.FlooredColumns != i.SampledColumns || i.MinimumSolidDepth < minimumSolidDepth)
            throw new InvalidOperationException($"{(left ? "Left" : "Right")} ocean basin integrity failed: wet {i.WetColumns}/{i.SampledColumns}, floored {i.FlooredColumns}/{i.SampledColumns}, minimum solid depth {i.MinimumSolidDepth}/{minimumSolidDepth}.");
    }

    private static void SetSolid(IWorldGenerationWorkspace workspace, int x, int y, ushort type, ushort? wall)
    {
        if (!workspace.TryGetTile(x, y, out WorldGenerationTile e))
            throw new InvalidOperationException($"Could not read generated tile ({x},{y}).");
        var tile = new WorldGenerationTile(type, wall ?? e.Wall, 0, 0, WorldGenerationTileFlags.Active, 0, e.TileColor, e.WallColor, 0, WorldGenerationLiquidKind.Water);
        if (!workspace.TrySetTile(x, y, in tile))
            throw new InvalidOperationException($"Could not write generated solid tile ({x},{y}).");
    }

    private static void SetWater(IWorldGenerationWorkspace workspace, int x, int y)
    {
        if (!workspace.TryGetTile(x, y, out WorldGenerationTile e))
            throw new InvalidOperationException($"Could not read generated tile ({x},{y}).");
        var tile = new WorldGenerationTile(0, e.Wall, 0, 0, WorldGenerationTileFlags.None, byte.MaxValue, 0, e.WallColor, 0, WorldGenerationLiquidKind.Water);
        if (!workspace.TrySetTile(x, y, in tile))
            throw new InvalidOperationException($"Could not write generated water tile ({x},{y}).");
    }
}
''', encoding='utf-8')

path = root / 'src/TerraRuntime.World/Generation/Optimized/OptimizedWorldGenerationProvider.cs'
text = path.read_text(encoding='utf-8')
old = '''            for (int y = Math.Max(1, waterLine - 8); y < floor; y++)
            {
                SetTile(
                    workspace,
                    x,
                    y,
                    type: 0,
                    wall: 0,
                    flags: WorldGenerationTileFlags.None,
                    liquidAmount: byte.MaxValue,
                    liquidKind: WorldGenerationLiquidKind.Water);
            }

            for (int y = floor; y < Math.Min(height - 1, floor + 10); y++)
                SetTile(workspace, x, y, sand, 0, WorldGenerationTileFlags.Active);
'''
new = '''            int floorDepth = Math.Clamp(height / 80, 12, 28);
            WorldGenerationGeometry.BuildOceanColumn(workspace, x, Math.Max(1, waterLine - 8), floor, sand, floorDepth);
'''
assert text.count(old) == 1, text.count(old)
text = text.replace(old, new)
old = '''                    if (water >= 64)
                        return;
'''
assert text.count(old) == 1, text.count(old)
text = text.replace(old, '''                    if (water >= 64)
                        break;
''')
old = '''        throw new InvalidOperationException(
            $"Optimized generator validation found insufficient {(left ? "left" : "right")} ocean water.");
    }
'''
new = '''        if (water < 64)
            throw new InvalidOperationException($"Optimized generator validation found insufficient {(left ? "left" : "right")} ocean water.");

        WorldGenerationGeometry.RequireOceanBasin(
            workspace,
            left,
            state.OceanWidth,
            Math.Max(1, state.BaseSurface - 12),
            Math.Min(workspace.HeightTiles - 2, state.RockLayer + 32),
            minimumSolidDepth: 8);
    }
'''
assert text.count(old) == 1, text.count(old)
text = text.replace(old, new)
path.write_text(text, encoding='utf-8')

path = root / 'src/TerraRuntime.World/Generation/Optimized/OptimizedLandmarkWorldGenerationProvider.cs'
text = path.read_text(encoding='utf-8')
old = '''            for (int x = centerX - half; x <= centerX + half; x++)
            {
                if (Math.Abs(x - centerX) >= half - 1 || row <= 1)
                    SetBlock(context.Workspace, x, y, SandstoneBrick);
                else
                    SetAir(context.Workspace, x, y);
            }
'''
new = '''            WorldGenerationGeometry.FillSolidHorizontal(context.Workspace, centerX - half, centerX + half, y, SandstoneBrick, wall: 0);
'''
assert text.count(old) == 1, text.count(old)
text = text.replace(old, new)
old = '        for (int y = topY + 3; y <= chamberTop + 1; y++)\n'
assert text.count(old) == 1, text.count(old)
text = text.replace(old, '        for (int y = topY; y <= chamberTop + 1; y++)\n')
old = '''            if (CountActiveTile(context.Workspace, SandstoneBrick) < state.PyramidTarget * 30)
                throw new InvalidOperationException("Optimized landmark validation found too little pyramid material.");
'''
new = '''            if (CountActiveTile(context.Workspace, SandstoneBrick) < state.PyramidTarget * 120)
                throw new InvalidOperationException("Optimized landmark validation found too little solid pyramid material.");
'''
assert text.count(old) == 1, text.count(old)
text = text.replace(old, new)
path.write_text(text, encoding='utf-8')

path = root / 'src/TerraRuntime.World/Generation/Optimized/OptimizedSurfaceDecorationWorldGenerationProvider.cs'
text = path.read_text(encoding='utf-8')
old = '''            for (int y = start; y <= end; y++)
            {
                if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                    (tile.Flags & WorldGenerationTileFlags.Active) != 0)
                    return y;
            }
            return -1;
'''
assert text.count(old) == 1, text.count(old)
text = text.replace(old, '            return WorldGenerationGeometry.FindFirstActiveY(workspace, x, start, end);\n')
old = '''            if (left < 1 || top < 1 || left + width >= workspace.WidthTiles - 1 || top + height >= workspace.HeightTiles - 1)
                return false;
            for (int x = left; x < left + width; x++)
            for (int y = top; y < top + height; y++)
            {
                if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile) ||
                    (tile.Flags & WorldGenerationTileFlags.Active) != 0 || tile.LiquidAmount != 0)
                    return false;
            }
            return true;
'''
assert text.count(old) == 1, text.count(old)
text = text.replace(old, '            return WorldGenerationGeometry.IsClearRectangle(workspace, left, top, width, height);\n')
path.write_text(text, encoding='utf-8')

path = root / 'tests/TerraRuntime.Tests/OptimizedWorldGenerationProviderTests.cs'
text = path.read_text(encoding='utf-8-sig')
old = '        Assert.True(CountActiveTiles(world, 151) >= 30, "At least one sandstone-brick pyramid must exist.");\n'
assert text.count(old) == 1, text.count(old)
text = text.replace(old, '        Assert.True(CountActiveTiles(world, 151) >= 120, "Optimized pyramids must contain a substantial solid sandstone-brick mass, not only an outline.");\n')
path.write_text(text, encoding='utf-8-sig')

path = root / 'docs/roadmap/optimized-worldgen.md'
text = path.read_text(encoding='utf-8-sig')
text = text.replace('- [x] bounded left/right oceans and beach floor;\n', '- [x] bounded left/right oceans with continuous solid basin floors and beach transitions;\n')
text = text.replace('- [x] pyramids with deterministic count budgets, internal shafts/chambers and persistent caches;\n', '- [x] solid-mass pyramids with deterministic count budgets, carved surface openings/shafts/chambers and persistent caches;\n')
text = text.replace('- [x] validate both oceans;\n', '- [x] validate both oceans for water coverage plus sampled continuous solid basin floors;\n')
needle = '- [x] deterministic ordinary forest/jungle/snow trees plus surface undergrowth and sunflower patches, with explicit density budgets and frame-important-object avoidance;\n'
assert needle in text
text = text.replace(needle, needle + '- [x] share bounded surface probing, clearance, solid-fill and ocean-column integrity primitives across optimized generation layers;\n')
path.write_text(text, encoding='utf-8-sig')

for lang, old_line, new_line, shell_old, shell_new in [
    ('en', '- both oceans and beaches;\n', '- both oceans and beaches with validated continuous basin floors;\n', 'world-width-scaled pyramid budget, builds a sandstone-brick shell, opens an internal shaft and chamber, and persists a\ncache inside the chamber.\n', 'world-width-scaled pyramid budget, builds a solid sandstone-brick mass, then carves a surface opening, internal shaft\nand chamber before persisting a cache inside the chamber.\n'),
    ('ru', '- оба океана и beaches;\n', '- оба океана и beaches с проверяемым непрерывным дном basin;\n', 'budget из ширины мира, строит sandstone-brick shell, внутренний shaft и chamber, после чего сохраняет cache внутри.\n', 'budget из ширины мира, сначала строит сплошную sandstone-brick массу, затем вырезает surface opening, внутренний shaft\nи chamber, после чего сохраняет cache внутри.\n')
]:
    p = root / f'docs/{lang}/optimized-world-generation.md'
    t = p.read_text(encoding='utf-8-sig')
    t = t.replace(old_line, new_line).replace(shell_old, shell_new)
    p.write_text(t, encoding='utf-8-sig')

audit = root / 'docs/roadmap/vanilla-worldgen-visual-parity-audit-2026-08-31.md'
audit.write_text('''# Vanilla worldgen visual parity audit - 2026-08-31\n\nThese observed defects remain vanilla-specific parity debt and are not papered over with optimized geometry helpers.\n\n- [ ] terrain silhouette: add final post-pass reference-world fixtures for canonical Small/Medium/Large;\n- [ ] surface shaping: identify every non-zero tile `Shape` writer near ordinary surface and compare half-block/slope output to TerrariaServer 1.4.5.8;\n- [ ] trees: replace the explicitly source-shaped trunk/branch scaffold with complete 1.4.5.8 framing, crowns and branches;\n- [ ] dungeon: replace/verify the current source-shaped vertical shaft + periodic-room approximation against Terraria dungeon graph geometry;\n- [ ] oceans: prove continuous floors and beach transitions after late `AlignOcean` correction on all canonical sizes.\n\nObserved symptoms: segmented trees without crowns, jagged/half-block-heavy surface patches, unusual dungeon geometry and ocean regions that can look under-generated despite containing water.\n''', encoding='utf-8')
