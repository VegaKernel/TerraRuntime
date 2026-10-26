using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal readonly record struct ToolWorldReplantResult1458(bool SourceCanPlace, bool Placed,
    TerrariaPlaceObjectState Report)
{
    internal ReadOnlyMemory<TerrariaTileManipulationState> CutReports { get; init; }
}

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class ToolWorldSourcePass1458
    {
        private bool saplingDestroying;
        // CLIENT TryReplantingTree and SERVER WorldGen.PlaceObject both execute
        // CanPlace (including its Next3); server then overrides random from79.
        // A matched random0..2 is also an existing same-location preview-cache
        // alternative on client. No extra preview history or funding is created.
        internal bool TryReplantTree(int x, int y, int direction, out ToolWorldReplantResult1458 result,
            int? sourcePreviewRandom = null)
        {
            result = default;
            if (!IsCurrentOwned || work.Count != 0 || direction is not (-1 or 1) ||
                sourcePreviewRandom is < 0 or > 2) return Reject();
            int top = y - 1;
            var dimensions = Overlay.Tiles.Dimensions;
            if (x < 5 || x + 1 > dimensions.WidthTiles - 5 || top < 5 || top + 2 > dimensions.HeightTiles - 5)
                return true; // original early bounds return precedes its random draw
            Span<WorldTile> body = stackalloc WorldTile[2];
            bool canPlace = true;
            for (int row = 0; row < 2; row++)
            {
                if (!Overlay.TryRead(x, top + row, out body[row]) ||
                    !VanillaToolTileCatalog1458.TryGet(body[row].TileType, out var facts)) return Reject();
                canPlace &= body[row].LiquidAmount == 0 &&
                    (!body[row].IsActive || VanillaToolSapling1458.IsBodyReplaceable(in facts, body[row].TileType));
            }
            if (!Overlay.TryRead(x, y + 1, out var anchor) ||
                !VanillaToolTileCatalog1458.TryGet(anchor.TileType, out var anchorFacts)) return Reject();
            bool validAnchorType = VanillaToolSapling1458.TryGetAlternate(anchor.TileType, out byte alternate);
            canPlace &= validAnchorType && anchor.IsActive && !anchor.IsActuated &&
                anchorFacts.IsSolid && !anchorFacts.IsSolidTop && !anchorFacts.NoAttach &&
                !anchor.IsHalfBrick && anchor.SourceSlope == 0;
            int drawn = random.Next(3); // consumed even if footprint/anchor/liquid denies placement
            if (!canPlace) return IsCurrentOwned;
            int chosen = sourcePreviewRandom ?? drawn;
            var cuts = new List<TerrariaTileManipulationState>(2);
            for (int row = 0; row < 2; row++)
            {
                if (!Overlay.TryRead(x, top + row, out var before) ||
                    !VanillaToolTileCatalog1458.TryGet(before.TileType, out var facts)) return Reject();
                if (before.IsActive && VanillaToolSapling1458.IsKilledDuringPlace(in facts, before.TileType))
                {
                    if (!TryKillTile(x, top + row, false, false, false, out _) ||
                        !Overlay.TryRead(x, top + row, out var after)) return Reject();
                    if (!after.IsActive)
                    {
                        var cut = new TerrariaTileManipulationState(0, checked((short)x), checked((short)(top + row)), 0, 0);
                        cuts.Add(cut);
                        if (server is not null && !server.TryStageSourceTileManipulation(x, top + row, 0)) return Reject();
                    }
                }
            }
            short frameX = checked((short)((alternate * 3 + chosen) * 18));
            for (int row = 0; row < 2; row++)
            {
                if (!Overlay.TryRead(x, top + row, out var before)) return Reject();
                if (before.IsActive) continue; // TileObject.Place never overwrites a surviving active cell
                var after = before;
                after.Flags |= WorldTileFlags.Active;
                after.Type = 20; after.FrameX = frameX; after.FrameY = checked((short)(row * 18));
                if (!Stage(WorldTileMutationKind.PlaceTile, x, top + row, in before, in after)) return Reject();
            }
            if (!PushSquare(x, y) || !Drain()) return false;
            var report = new TerrariaPlaceObjectState(checked((short)x), checked((short)y), 20, 0,
                checked((byte)alternate), checked((sbyte)chosen), direction == 1);
            result = new(true, true, report) { CutReports = cuts.ToArray() };
            return IsCurrentOwned;
        }

        private bool FrameSapling(int x, int y, in WorldTile tile)
        {
            if (plantDestroying || saplingDestroying || container?.Destroying == true) return true;
            if (tile.FrameY < 0 || tile.FrameY % 40 is not (0 or 18)) return false;
            int top = y - tile.FrameY % 40 / 18, verticalStyle = tile.FrameY / 40;
            if (!Overlay.TryReadSourceSafely(x, top, out var a) || !Overlay.TryReadSourceSafely(x, top + 1, out var b) ||
                !Overlay.TryReadSourceSafely(x, top + 2, out var ground) ||
                !VanillaToolTileCatalog1458.TryGet(ground.TileType, out var facts)) return false;
            bool valid = a.Type == 20 && b.Type == 20 && a.FrameY == verticalStyle * 40 && b.FrameY == verticalStyle * 40 + 18;
            bool validGroundType = VanillaToolSapling1458.TryGetAlternate(ground.TileType, out byte alternate);
            int platformFrame = ground.FrameX / 18;
            bool properPlatform = facts.IsPlatform && (platformFrame is >= 0 and <= 7 or >= 12 and <= 16 or >= 25 and <= 26);
            valid &= ground.IsActive && !ground.IsActuated && !ground.IsHalfBrick &&
                (facts.IsSolid || facts.IsSolidTop) && (ground.SourceSlope is not (1 or 2) || properPlatform) && validGroundType;
            if (valid)
            {
                if (a.FrameX / 54 == alternate) return true;
                short frame = checked((short)((alternate * 3 + random.Next(3)) * 18));
                var aa = a; var bb = b; aa.FrameX = frame; bb.FrameX = frame;
                return Stage(WorldTileMutationKind.TransformTile, x, top, in a, in aa) &&
                    Stage(WorldTileMutationKind.TransformTile, x, top + 1, in b, in bb);
            }
            saplingDestroying = true;
            return Push(new(StepKind.SaplingNextCell, x, top) { Index = 0 });
        }
        private bool SaplingNextCell(in SourceStep step)
        {
            if (step.Index == 2) { saplingDestroying = false; return true; }
            int y = step.Y + step.Index;
            if (!Overlay.TryReadSourceSafely(step.X, y, out var tile) ||
                !Push(step with { Index = step.Index + 1 })) return false;
            return tile.Type != 20 || Push(new(StepKind.Kill, step.X, y));
        }
    }
}
