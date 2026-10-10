using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

// The caller owns source-trusted ordinary4/5, wind-disabled motion and this exact typed world.
internal sealed class OrdinaryArrowTerrainCapture
{
    private const int MaximumSections = 16;
    private const int MaximumCutProbeTiles = 4096;
    private readonly WorldTileStore tiles;
    private readonly (WorldSectionId Section, long Version)[] sections;
    private readonly int left, top, right, bottom;

    private OrdinaryArrowTerrainCapture(WorldTileStore tiles,
        (WorldSectionId Section, long Version)[] sections, int left, int top, int right, int bottom)
    {
        this.tiles = tiles;
        this.sections = sections;
        this.left = left; this.top = top; this.right = right; this.bottom = bottom;
    }

    internal bool MayReach(float x, float y, int width, int height) =>
        x < (right + 1) * 16f && x + width > left * 16f &&
        y < (bottom + 1) * 16f && y + height > top * 16f;

    internal bool IsCurrent
    {
        get
        {
            foreach (var captured in sections)
                if ((captured.Version & 1L) != 0 || tiles.GetSectionVersion(captured.Section) != captured.Version)
                    return false;
            return true;
        }
    }

    internal static bool TryCapture(WorldTileStore tiles, in ProjectileSnapshot initial,
        bool windPhysics, out OrdinaryArrowTerrainCapture? capture)
    {
        capture = null;
        if (windPhysics || initial.Type.Value is not (4 or 5) ||
            BitConverter.SingleToInt32Bits(initial.Ai.Ai2) is not (0 or 3) ||
            !float.IsFinite(initial.PositionX) || !float.IsFinite(initial.PositionY) ||
            !float.IsFinite(initial.VelocityX) || !float.IsFinite(initial.VelocityY) ||
            !VanillaDefinitionCatalog.TryGet(initial.Type, out var definition))
            return false;
        int steps = initial.Type.Value == 5 ? 2 : 1;
        double reachX = steps * 2d * Math.Abs(initial.VelocityX);
        double reachY = steps * 2d * Math.Max(Math.Abs(initial.VelocityY), 16f);
        double minX = initial.PositionX - reachX, maxX = initial.PositionX + reachX;
        double minY = initial.PositionY - reachY, maxY = initial.PositionY + reachY;
        // These are bounded refusal policies, not vanilla ranges. Do not scan a client-sized world rectangle.
        if (Math.Max(Math.Abs(minX), Math.Abs(maxX)) > float.MaxValue ||
            Math.Max(Math.Abs(minY), Math.Abs(maxY)) > float.MaxValue)
            return false;
        double width = Math.Max(definition.Width, definition.CollisionWidth + Math.Abs(definition.CollisionOffsetX));
        double height = Math.Max(definition.Height, definition.CollisionHeight + Math.Abs(definition.CollisionOffsetY));
        int left = Clamp(Math.Floor(minX / 16d) - 2, tiles.Dimensions.WidthTiles);
        int top = Clamp(Math.Floor(minY / 16d) - 2, tiles.Dimensions.HeightTiles);
        int right = Clamp(Math.Floor((maxX + width) / 16d) + 2, tiles.Dimensions.WidthTiles);
        int bottom = Clamp(Math.Floor((maxY + height) / 16d) + 2, tiles.Dimensions.HeightTiles);
        var first = TerrariaSectionGeometry.FromTile(tiles.Dimensions, left, top);
        var last = TerrariaSectionGeometry.FromTile(tiles.Dimensions, right, bottom);
        long sectionCount = (long)(last.X - first.X + 1) * (last.Y - first.Y + 1);
        if (sectionCount > MaximumSections)
            return false;
        // Include the full future cut sweep and y+1 support before any external callback or terrain query.
        var start = VanillaWorldProjectileTileCut.GetCutBounds(tiles.Dimensions,
            (float)minX, (float)minY, definition.Width, definition.Height);
        var end = VanillaWorldProjectileTileCut.GetCutBounds(tiles.Dimensions,
            (float)maxX, (float)maxY, definition.Width, definition.Height);
        long cutTiles = (long)(Math.Max(start.ExclusiveRight, end.ExclusiveRight) - Math.Min(start.X, end.X)) *
            (Math.Max(start.ExclusiveBottom, end.ExclusiveBottom) - Math.Min(start.Y, end.Y));
        if (cutTiles > MaximumCutProbeTiles)
            return false;
        var versions = new (WorldSectionId, long)[(int)sectionCount];
        int index = 0;
        for (int y = first.Y; y <= last.Y; y++)
        for (int x = first.X; x <= last.X; x++)
        {
            var section = new WorldSectionId(x, y);
            long version = tiles.GetSectionVersion(section);
            if ((version & 1L) != 0)
                return false;
            versions[index++] = (section, version);
        }
        var prepared = new OrdinaryArrowTerrainCapture(tiles, versions, left, top, right, bottom);
        // Source-local owner0/my0 reaches Damage.CutTiles too. Until its effects are owned, refuse candidates.
        if (definition.CanCutTiles && initial.Damage > 0 &&
            VanillaWorldProjectileTileCut.HasCandidateAlongSweep(tiles,
                (float)minX, (float)minY, (float)maxX, (float)maxY, definition.Width, definition.Height))
            return false;
        if (!prepared.IsCurrent)
            return false;
        capture = prepared;
        return true;
    }

    private static int Clamp(double value, int count) => (int)Math.Clamp(value, 0d, count - 1d);
}
