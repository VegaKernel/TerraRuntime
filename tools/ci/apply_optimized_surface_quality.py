from pathlib import Path

root = Path('.')

# Extend shared geometry with shape-preserving surface mutation.
path = root / 'src/TerraRuntime.World/Generation/Common/WorldGenerationGeometry.cs'
text = path.read_text(encoding='utf-8')
needle = '''    public static void FillSolidHorizontal(IWorldGenerationWorkspace workspace, int left, int right, int y, ushort type, ushort? wall = null)\n    {\n'''
assert needle in text
insert = '''    public static bool TrySetShape(IWorldGenerationWorkspace workspace, int x, int y, byte shape)\n    {\n        ArgumentNullException.ThrowIfNull(workspace);\n        if (shape > 5 || (uint)x >= (uint)workspace.WidthTiles || (uint)y >= (uint)workspace.HeightTiles)\n            return false;\n        if (!workspace.TryGetTile(x, y, out WorldGenerationTile current) ||\n            (current.Flags & WorldGenerationTileFlags.Active) == 0)\n        {\n            return false;\n        }\n\n        var shaped = new WorldGenerationTile(\n            Type: current.Type,\n            Wall: current.Wall,\n            FrameX: current.FrameX,\n            FrameY: current.FrameY,\n            Flags: current.Flags,\n            LiquidAmount: current.LiquidAmount,\n            TileColor: current.TileColor,\n            WallColor: current.WallColor,\n            Shape: shape,\n            LiquidKind: current.LiquidKind);\n        return workspace.TrySetTile(x, y, in shaped);\n    }\n\n'''
text = text.replace(needle, insert + needle, 1)
path.write_text(text, encoding='utf-8')

# Add a deterministic surface-finishing pass and proper foliage anchors to ordinary optimized trees.
path = root / 'src/TerraRuntime.World/Generation/Optimized/OptimizedSurfaceDecorationWorldGenerationProvider.cs'
text = path.read_text(encoding='utf-8')
text = text.replace(
    '/// Final visual surface-life overlay for <c>terraruntime:optimized</c>. It runs after landmark construction but before\n/// the final progression validator, so decoration sees the complete structure/chest layout and the candidate is still\n/// structurally revalidated before publication. The algorithms are custom/deterministic; tree and plant tile identities\n/// and the conservative tree framing scaffold are reused from the repository\'s source-backed 1.4.5.8 vegetation work.\n',
    '/// Final surface-quality overlay for <c>terraruntime:optimized</c>. It runs after landmark construction but before\n/// the final progression validator. A deterministic finishing pass shapes one-step natural surface transitions, then\n/// surface-life decoration places trees/plants while preserving progression structures. Ordinary tree crowns use the\n/// vanilla tree foliage-anchor frame contract; placement remains custom and is not claimed to be seed-identical.\n')
old = '''    private static readonly WorldGenerationPassId LandmarkValidationId = new("terraruntime:optimized/landmark-validation");\n    private static readonly WorldGenerationPassId SurfaceLifeId = new("terraruntime:optimized/surface-life");\n'''
new = '''    private static readonly WorldGenerationPassId LandmarkValidationId = new("terraruntime:optimized/landmark-validation");\n    private static readonly WorldGenerationPassId SurfaceShapingId = new("terraruntime:optimized/surface-shaping");\n    private static readonly WorldGenerationPassId SurfaceLifeId = new("terraruntime:optimized/surface-life");\n'''
assert text.count(old) == 1, text.count(old)
text = text.replace(old, new)
old = '''            builder.Add(\n                new WorldGenerationPassDescriptor(\n                    SurfaceLifeId,\n                    WorldGenerationRngMode.IsolatedDeterministic,\n                    requiredAfter: [LandmarkValidationId]),\n                SurfaceLifePass.Instance);\n            builder.Add(CloneDescriptor(entry.Descriptor, [SurfaceLifeId]), entry.Pass);\n'''
new = '''            builder.Add(\n                new WorldGenerationPassDescriptor(\n                    SurfaceShapingId,\n                    WorldGenerationRngMode.IsolatedDeterministic,\n                    requiredAfter: [LandmarkValidationId]),\n                SurfaceShapingPass.Instance);\n            builder.Add(\n                new WorldGenerationPassDescriptor(\n                    SurfaceLifeId,\n                    WorldGenerationRngMode.IsolatedDeterministic,\n                    requiredAfter: [SurfaceShapingId]),\n                SurfaceLifePass.Instance);\n            builder.Add(CloneDescriptor(entry.Descriptor, [SurfaceLifeId]), entry.Pass);\n'''
assert text.count(old) == 1, text.count(old)
text = text.replace(old, new)
marker = '''    private sealed class SurfaceLifePass : IWorldGenerationPass\n    {\n'''
assert marker in text
shaping = r'''    private sealed class SurfaceShapingPass : IWorldGenerationPass
    {
        private const ushort Dirt = 0;
        private const ushort Grass = 2;
        private const ushort Sand = 53;
        private const ushort Mud = 59;
        private const ushort JungleGrass = 60;
        private const ushort SnowBlock = 147;

        public static SurfaceShapingPass Instance { get; } = new();

        public void Execute(IWorldGenerationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            IWorldGenerationMetadataWorkspace metadata = context.Metadata ??
                throw new InvalidOperationException("Optimized surface shaping requires semantic world metadata.");
            if (!metadata.TryGetLayers(out WorldGenerationLayers layers) || !metadata.TryGetSpawn(out WorldGenerationPoint spawn))
                throw new InvalidOperationException("Optimized surface shaping requires layer and spawn metadata.");

            int width = context.Workspace.WidthTiles;
            int startY = Math.Clamp((int)Math.Floor(layers.WorldSurface) - 55, 2, context.Workspace.HeightTiles - 3);
            int endY = Math.Clamp((int)Math.Ceiling(layers.WorldSurface) + 120, startY + 1, context.Workspace.HeightTiles - 2);
            int margin = Math.Clamp(layers.OceanWidth / 3, 3, Math.Max(3, width / 5));
            int shaped = 0;

            for (int x = Math.Max(2, margin); x < width - Math.Max(2, margin); x++)
            {
                if ((x & 127) == 0)
                    context.CancellationToken.ThrowIfCancellationRequested();
                if (Math.Abs(x - spawn.X) < 22)
                    continue;

                int y = WorldGenerationGeometry.FindFirstActiveY(context.Workspace, x, startY, endY);
                int leftY = WorldGenerationGeometry.FindFirstActiveY(context.Workspace, x - 1, startY, endY);
                int rightY = WorldGenerationGeometry.FindFirstActiveY(context.Workspace, x + 1, startY, endY);
                if (y < 0 || leftY < 0 || rightY < 0)
                    continue;
                if (!context.Workspace.TryGetTile(x, y, out WorldGenerationTile tile) || !IsNaturalSurface(tile.Type))
                    continue;
                if (tile.LiquidAmount != 0 || tile.Shape != 0)
                    continue;
                if (context.Workspace.TryGetTile(x, y - 1, out WorldGenerationTile above) &&
                    ((above.Flags & WorldGenerationTileFlags.Active) != 0 || above.LiquidAmount != 0))
                {
                    continue;
                }

                byte shape = 0;
                // WorldTile shape 2/3 map to the two walkable top slopes. Use them only for a clean one-tile
                // height transition; isolated one-block peaks become half blocks rather than square teeth.
                if (rightY == y + 1 && leftY <= y)
                    shape = 2;
                else if (leftY == y + 1 && rightY <= y)
                    shape = 3;
                else if (leftY == y + 1 && rightY == y + 1)
                    shape = 1;

                if (shape != 0 && WorldGenerationGeometry.TrySetShape(context.Workspace, x, y, shape))
                    shaped++;
            }

            if (shaped == 0)
                throw new InvalidOperationException("Optimized surface shaping found no eligible natural surface transitions.");

            context.ReportProgress(1d, $"Shaped {shaped} optimized natural surface transitions");
        }

        private static bool IsNaturalSurface(ushort type) =>
            type is Dirt or Grass or Sand or Mud or JungleGrass or SnowBlock;
    }

'''
text = text.replace(marker, shaping + marker, 1)
old = '''                ushort ground = ReadType(context.Workspace, x, floor);\n                int style = ground switch\n'''
new = '''                ushort ground = ReadType(context.Workspace, x, floor);\n                if (!IsFlatSupport(context.Workspace, x, floor))\n                    continue;\n                int style = ground switch\n'''
assert text.count(old) == 1, text.count(old)
text = text.replace(old, new)
old = '''                for (int y = floor - 1; y >= top; y--)\n                    SetPlant(context.Workspace, x, y, Trees, style * 22, 0);\n\n                if (height >= 13)\n'''
new = '''                for (int y = floor - 1; y >= top; y--)\n                    SetPlant(context.Workspace, x, y, Trees, style * 22, 0);\n\n                // Terraria treats tree cells with frameY >= 198 and frameX >= 22 as foliage anchors. Keep the\n                // custom optimized placement, but publish a valid crown marker instead of a bare trunk tip.\n                SetPlant(context.Workspace, x, top, Trees, Math.Max(22, style * 22), 198);\n\n                if (height >= 13)\n'''
assert text.count(old) == 1, text.count(old)
text = text.replace(old, new)
old = '''                int floor = FindSurfaceFloor(context.Workspace, x, layers);\n                if (floor <= 2 || !IsAir(context.Workspace, x, floor - 1))\n                    continue;\n\n                ushort ground = ReadType(context.Workspace, x, floor);\n'''
new = '''                int floor = FindSurfaceFloor(context.Workspace, x, layers);\n                if (floor <= 2 || !IsAir(context.Workspace, x, floor - 1) || !IsFlatSupport(context.Workspace, x, floor))\n                    continue;\n\n                ushort ground = ReadType(context.Workspace, x, floor);\n'''
assert text.count(old) == 1, text.count(old)
text = text.replace(old, new)
old = '''                int floor = FindSurfaceFloor(context.Workspace, left, layers);\n                if (floor < 6 || ReadType(context.Workspace, left, floor) != Grass || ReadType(context.Workspace, left + 1, floor) != Grass)\n                    continue;\n'''
new = '''                int floor = FindSurfaceFloor(context.Workspace, left, layers);\n                if (floor < 6 || ReadType(context.Workspace, left, floor) != Grass || ReadType(context.Workspace, left + 1, floor) != Grass ||\n                    !IsFlatSupport(context.Workspace, left, floor) || !IsFlatSupport(context.Workspace, left + 1, floor))\n                    continue;\n'''
assert text.count(old) == 1, text.count(old)
text = text.replace(old, new)
marker = '''        private static bool IsAir(IWorldGenerationWorkspace workspace, int x, int y) =>\n'''
assert marker in text
flat = '''        private static bool IsFlatSupport(IWorldGenerationWorkspace workspace, int x, int y) =>\n            workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&\n            (tile.Flags & WorldGenerationTileFlags.Active) != 0 && tile.Shape == 0;\n\n'''
text = text.replace(marker, flat + marker, 1)
path.write_text(text, encoding='utf-8')

# Strengthen executable evidence: trees must have crown anchors and the natural surface must actually contain shapes.
path = root / 'tests/TerraRuntime.Tests/OptimizedWorldGenerationProviderTests.cs'
text = path.read_text(encoding='utf-8-sig')
needle = '''        Assert.True(CountActiveTiles(world, 5) >= 120, "Ordinary forest/jungle/snow tree trunks must decorate the optimized surface.");\n'''
assert needle in text
text = text.replace(needle, needle + '''        Assert.True(CountTreeFoliageAnchors(world) >= 10, "Optimized ordinary trees must publish foliage anchors instead of bare trunk tips.");\n        Assert.True(CountShapedNaturalSurface(world, result.Metadata.Layers) >= 4, "Optimized surface finishing must create non-square natural transitions.");\n''', 1)
needle = '''        Assert.True(CountActiveTiles(result.Candidate, 5) >= 700, "Canonical Small optimized worlds must contain a substantial ordinary-tree population.");\n'''
assert needle in text
text = text.replace(needle, needle + '''        Assert.True(CountTreeFoliageAnchors(result.Candidate) >= 80, "Canonical Small optimized trees must include persistent foliage anchors.");\n        Assert.True(CountShapedNaturalSurface(result.Candidate, result.Metadata.Layers) >= 20, "Canonical Small optimized terrain must retain visible shaped surface transitions.");\n''', 1)
marker = '''    private static int CountWall(RuntimeWorldGenerationWorkspace workspace, ushort wall)\n'''
assert marker in text
helpers = r'''    private static int CountTreeFoliageAnchors(RuntimeWorldGenerationWorkspace workspace)
    {
        int count = 0;
        for (int y = 0; y < workspace.HeightTiles; y++)
        for (int x = 0; x < workspace.WidthTiles; x++)
        {
            if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                (tile.Flags & WorldGenerationTileFlags.Active) != 0 &&
                tile.Type == 5 && tile.FrameX >= 22 && tile.FrameY >= 198)
            {
                count++;
            }
        }
        return count;
    }

    private static int CountShapedNaturalSurface(RuntimeWorldGenerationWorkspace workspace, WorldGenerationLayers layers)
    {
        int start = Math.Clamp((int)Math.Floor(layers.WorldSurface) - 60, 0, workspace.HeightTiles - 1);
        int end = Math.Clamp((int)Math.Ceiling(layers.WorldSurface) + 120, start, workspace.HeightTiles - 1);
        int count = 0;
        for (int y = start; y <= end; y++)
        for (int x = 1; x < workspace.WidthTiles - 1; x++)
        {
            if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile) || tile.Shape == 0 ||
                (tile.Flags & WorldGenerationTileFlags.Active) == 0)
            {
                continue;
            }
            if (tile.Type is 0 or 2 or 53 or 59 or 60 or 147)
                count++;
        }
        return count;
    }

'''
text = text.replace(marker, helpers + marker, 1)
path.write_text(text, encoding='utf-8-sig')

# Roadmap and paired docs.
path = root / 'docs/roadmap/optimized-worldgen.md'
text = path.read_text(encoding='utf-8-sig')
text = text.replace('- [ ] slope-aware beaches and cliffs;\n', '- [x] slope-aware one-tile natural surface transitions with persisted top slopes/half-blocks;\n')
needle = '- [x] deterministic ordinary forest/jungle/snow trees plus surface undergrowth and sunflower patches, with explicit density budgets and frame-important-object avoidance;\n'
assert needle in text
text = text.replace(needle, needle + '- [x] ordinary optimized trees publish persistent vanilla-format foliage anchors and executable crown-count regressions;\n', 1)
path.write_text(text, encoding='utf-8-sig')

for lang in ('en', 'ru'):
    path = root / f'docs/{lang}/optimized-world-generation.md'
    text = path.read_text(encoding='utf-8-sig')
    if lang == 'en':
        old = '''    LVal["landmark validator"]\n    Surf["OptimizedSurfaceDecorationWorldGenerationProvider<br/>ordinary trees / undergrowth / sunflowers"]\n    Prog["OptimizedProgressionValidationWorldGenerationProvider<br/>resource / structure / reachability gate"]\n'''
        new = '''    LVal["landmark validator"]\n    Shape["surface shaping<br/>natural top slopes / half-block transitions"]\n    Surf["OptimizedSurfaceDecorationWorldGenerationProvider<br/>foliage-anchored trees / undergrowth / sunflowers"]\n    Prog["OptimizedProgressionValidationWorldGenerationProvider<br/>resource / structure / reachability gate"]\n'''
        text = text.replace(old, new).replace('    Base --> Play --> Land --> Meta --> PVal --> LVal --> Surf --> Prog --> Commit\n', '    Base --> Play --> Land --> Meta --> PVal --> LVal --> Shape --> Surf --> Prog --> Commit\n')
        needle = '- deterministic ordinary forest, jungle and snow trees plus grass/jungle undergrowth and sunflower patches, all placed after landmarks so progression objects and caches are protected.\n'
        assert needle in text
        text = text.replace(needle, needle + '- a deterministic surface-finishing pass converts clean one-tile natural height transitions into persisted walkable slopes/half-blocks; ordinary optimized trees mark their crown cells with the vanilla tree foliage-frame contract instead of ending as bare trunk tiles.\n', 1)
    else:
        old = '''    LVal["landmark validator"]\n    Surf["OptimizedSurfaceDecorationWorldGenerationProvider<br/>ordinary trees / undergrowth / sunflowers"]\n    Prog["OptimizedProgressionValidationWorldGenerationProvider<br/>resource / structure / reachability gate"]\n'''
        new = '''    LVal["landmark validator"]\n    Shape["surface shaping<br/>natural top slopes / half-block transitions"]\n    Surf["OptimizedSurfaceDecorationWorldGenerationProvider<br/>trees с foliage anchors / undergrowth / sunflowers"]\n    Prog["OptimizedProgressionValidationWorldGenerationProvider<br/>resource / structure / reachability gate"]\n'''
        text = text.replace(old, new).replace('    Base --> Play --> Land --> Meta --> PVal --> LVal --> Surf --> Prog --> Commit\n', '    Base --> Play --> Land --> Meta --> PVal --> LVal --> Shape --> Surf --> Prog --> Commit\n')
        needle = '- детерминированные обычные forest/jungle/snow trees, surface undergrowth и sunflower patches, которые ставятся после landmarks и обходят progression objects/caches.\n'
        assert needle in text
        text = text.replace(needle, needle + '- отдельный deterministic surface-finishing pass превращает чистые однотайловые перепады natural terrain в сохраняемые walkable slopes/half-blocks; верхушки обычных optimized trees получают vanilla-format foliage anchors вместо голого последнего trunk tile.\n', 1)
    path.write_text(text, encoding='utf-8-sig')
