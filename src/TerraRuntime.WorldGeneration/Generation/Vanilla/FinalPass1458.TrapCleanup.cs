using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class FinalPass1458
{
    private const WorldTileFlags TrapCleanupWire = WorldTileFlags.WireRed;

    private static void ApplyRemoveBrokenTraps(IWorldGenerationContext context, Workspace workspace, IWorldGenerationVanillaRandom random)
    {
        WorldTileStore tiles = workspace.TileStore;
        int width = workspace.WidthTiles, height = workspace.HeightTiles;
        var framing = new GenerationTileFraming1458(tiles, random, workspace: workspace, useWorldGenerationSolidity: true);
        var visited = new HashSet<int>();
        for (int x = 50; x < width - 50; x++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            for (int y = 50; y < height - 50; y++)
            {
                int start = tiles.GetUncheckedIndex(x, y);
                if ((tiles.Tiles[start].Flags & TrapCleanupWire) == 0 || visited.Contains(start)) continue;
                var current = new Queue<int>(); var next = new Queue<int>();
                var component = new List<int>(); var componentMembership = new HashSet<int>();
                bool trap = false, trigger = false;
                next.Enqueue(start);
                // Native source bounds both breadth rounds and visits per round independently.
                // Duplicate pending coordinates are intentional: only processed membership filters neighbours.
                for (int round = 1; round < 2000 && next.Count > 0; round++)
                {
                    (current, next) = (next, current);
                    for (int visit = 1; visit < 2000 && current.TryDequeue(out int index); visit++)
                    {
                        int cx = index / height, cy = index % height;
                        if (cx < 5 || cy < 5 || cx >= width - 5 || cy >= height - 5) continue;
                        WorldTile tile = tiles.Tiles[index];
                        if ((tile.Flags & TrapCleanupWire) == 0) continue;
                        visited.Add(index); component.Add(index); componentMembership.Add(index);
                        trap |= IsTrapEndpoint(tile); trigger |= IsTriggerEndpoint(tile);
                        if (trap && trigger) break;
                        Add(index - height); Add(index + height); Add(index - 1); Add(index + 1);
                        void Add(int neighbour) { if (!componentMembership.Contains(neighbour)) next.Enqueue(neighbour); }
                    }
                    if (trap && trigger) break;
                }
                if (trap && trigger) continue;
                foreach (int index in component)
                {
                    int cx = index / height, cy = index % height;
                    ref WorldTile tile = ref tiles.Tiles[index];
                    tile.Flags &= ~TrapCleanupWire;
                    if ((tile.Flags & WorldTileFlags.Actuator) != 0)
                    {
                        tile.Flags &= ~WorldTileFlags.Actuator;
                        WorldTile above = tiles.Get(cx, cy - 1);
                        if (GenerationObjectSupport1458.IsBoulder(above.Type) && above.FrameX % 16 == 0)
                        {
                            for (int dx = 0; dx < 2; dx++)
                                for (int dy = -2; dy < 0; dy++)
                                {
                                    ref WorldTile member = ref tiles.Tiles[tiles.GetUncheckedIndex(cx + dx, cy + dy)];
                                    if (!member.IsActive || !GenerationObjectSupport1458.IsBoulder(member.Type)) continue;
                                    member.Type = 1; member.Flags = WorldTileFlags.Active; member.FrameX = 0; member.FrameY = 0; member.Shape = 0; member.TileColor = 0; member.WallColor = 0; member.LiquidAmount = 0; member.LiquidKind = WorldLiquidKind.Water;
                                    tiles.LiquidUpdates.ClearSkipNextUpdate(cx + dx, cy + dy);
                                }
                        }
                    }
                    if (IsTrapEndpoint(tile) && tile.Type != 105) framing.KillTile(cx, cy);
                    else if (IsTriggerEndpoint(tile))
                    {
                        if (tile.Type == 314) tile.FrameX = 1;
                        else framing.KillTile(cx, cy);
                    }
                }
            }
        }
    }

    // TileID.Sets.Wiring's immutable 1.4.5.8 capability tables; active() belongs to the caller.
    private static bool IsTrapEndpoint(in WorldTile tile) => tile.IsActive &&
        ((tile.Flags & WorldTileFlags.Actuator) != 0 || IsMechanism(tile.Type) && !IgnoreTrapValidation(tile.Type));
    private static bool IsTriggerEndpoint(in WorldTile tile) => tile.IsActive &&
        (tile.Type is 135 or 136 or 132 or 144 or 411 or 423 or 476 or 440 or 441 or 468 ||
         tile.Type == 467 && tile.FrameX / 36 == 4 || tile.Type == 314 && tile.FrameX is 20 or 21);
    private static bool IgnoreTrapValidation(ushort type) => type is 411 or 10 or 11 or 387 or 386 or 388 or 389 or 4 or 215 or 21 or 467 or 219 or 642 or 314;
    private static bool IsMechanism(ushort type) => type is
        420 or 419 or 411 or 4 or 33 or 100 or 93 or 42 or 34 or 215 or 405 or 92 or 35 or 126 or 95 or 149 or
        593 or 594 or 564 or 10 or 11 or 387 or 386 or 388 or 389 or 137 or 443 or 141 or 130 or 131 or 546 or
        557 or 421 or 422 or 209 or 212 or 216 or 338 or 335 or 497 or 406 or 244 or 452 or 565 or 139 or 506 or
        105 or 349 or 531 or 429 or 142 or 143 or 235 or 210 or 425 or 21 or 467 or 219 or 642 or 356 or 663 or
        255 or 256 or 257 or 258 or 259 or 260 or 261 or 262 or 263 or 264 or 265 or 266 or 267 or 268 or 207 or
        480 or 455 or 509 or 657 or 658 or 720 or 721 or 725 or 733 or 314;
}
