using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal readonly record struct ToolWorldSourceResult1458(bool Changed, bool SourceFailed,
    bool RequiresPlayerLift);

internal sealed partial class WorldTileAuthority
{
    internal bool TryCreateToolSourcePass(ToolWorldTileOverlay1458 overlay,
        VanillaUnifiedRandom1458 sourceRandom, bool clientRole, bool hardMode,
        in ToolTreeWorldFacts1458 facts, ReadOnlySpan<ToolTreeActor1458> actors,
        ToolWorldMutationPlan? serverPlan, out ToolWorldSourcePass1458? pass)
    {
        pass = null;
        if (tiles is null || !ReferenceEquals(tiles, overlay.Tiles) ||
            !overlay.IsCurrentOwned || sourceRandom is null ||
            (!clientRole && (serverPlan is null || !ReferenceEquals(serverPlan.Overlay, overlay) ||
                !ReferenceEquals(serverPlan.SourceRandom, sourceRandom))) ||
            (clientRole && serverPlan is not null) || actors.Length > byte.MaxValue ||
            hardMode != (hardModeBaseline || progression.IsCompleted(VanillaWorldProgressionId.Hardmode)))
            return false;
        if (serverPlan is not null && !serverPlan.TryBindSourceActors(actors)) return false;
        pass = new(this, overlay, sourceRandom, clientRole, hardMode, facts, actors, serverPlan);
        return true;
    }

    /// <summary>
    /// Ordered WorldGen calls over a detached role-specific world. Client calls
    /// use the local ItemCheck RNG and never allocate server drops; server calls
    /// share the complete action's one world RNG/allocation preview. Neither
    /// role adopts live state or publishes observers here.
    /// </summary>
    internal sealed partial class ToolWorldSourcePass1458
    {
        private const int MaximumSteps = 4096;
        private readonly WorldTileAuthority owner;
        private readonly VanillaUnifiedRandom1458 random;
        private readonly bool clientRole;
        private readonly bool hardMode;
        private readonly ToolTreeWorldFacts1458 facts;
        private readonly ToolTreeActor1458[] actors;
        private readonly ToolWorldMutationPlan? server;
        private readonly bool skeletronDowned;
        private readonly bool golemDowned;
        private readonly Stack<SourceStep> work = new();
        private int steps;
        private int mutationOrdinal;
        private bool failed;
        private enum StepKind : byte { Kill, Frame, PalmAfterKill, FallingAfterFrame, CrackedNeighbor, CrackedAfterNeighbor, KillAfterSpecial, ContainerNextCell, FossilNeighbor, FossilAfterNeighbor, KillAfterPreEffects, SaplingNextCell, PlantKillComplete, LarvaNextCell, LarvaAfterKills, FurnitureNextCell }
        private readonly record struct SourceStep(StepKind Kind, int X, int Y,
            bool Fail = false, bool EffectOnly = false, bool NoItem = false, bool ResetFrame = false)
        {
            internal int Index { get; init; }
            internal WorldTile SourceTile { get; init; }
        }

        internal ToolWorldSourcePass1458(WorldTileAuthority owner, ToolWorldTileOverlay1458 overlay,
            VanillaUnifiedRandom1458 random, bool clientRole, bool hardMode,
            in ToolTreeWorldFacts1458 facts, ReadOnlySpan<ToolTreeActor1458> actors,
            ToolWorldMutationPlan? server)
        {
            this.owner = owner; Overlay = overlay; this.random = random;
            this.clientRole = clientRole; this.hardMode = hardMode; this.facts = facts;
            this.actors = actors.ToArray(); this.server = server;
            skeletronDowned = owner.skeletronDownedBaseline || owner.progression.IsCompleted(VanillaWorldProgressionId.Skeletron);
            golemDowned = owner.golemDownedBaseline || owner.progression.IsCompleted(VanillaWorldProgressionId.Golem);
        }

        public ToolWorldTileOverlay1458 Overlay { get; }
        public bool IsCurrentOwned => !failed && Overlay.IsCurrentOwned && IsFurnitureSourceCurrent && AreSourceContainerPermissionsCurrent() &&
            (server is null || server.IsCurrentOwned) &&
            (owner.hardModeBaseline || owner.progression.IsCompleted(VanillaWorldProgressionId.Hardmode)) == hardMode &&
            (owner.skeletronDownedBaseline || owner.progression.IsCompleted(VanillaWorldProgressionId.Skeletron)) == skeletronDowned &&
            (owner.golemDownedBaseline || owner.progression.IsCompleted(VanillaWorldProgressionId.Golem)) == golemDowned;

        public bool TryKillTile(int x, int y, bool fail, bool effectOnly, bool noItem,
            out ToolWorldSourceResult1458 result)
        {
            result = default;
            if (!IsCurrentOwned || work.Count != 0 || !Overlay.TryReadSourceSafely(x, y, out var before))
                return Reject();
            int changed = mutationOrdinal;
            bool sourceFail = fail;
            if (before.IsActive)
            {
                if (!VanillaToolWorldRules1458.TryEvaluate(Overlay.Tiles, Overlay, x, y, hardMode, out var permission)) return Reject();
                sourceFail |= permission.Breakability == ToolWorldBreakability1458.FailedEffect;
            }
            if (!Push(new(StepKind.Kill, x, y, fail, effectOnly, noItem)) || !Drain()) return false;
            result = new(mutationOrdinal != changed, sourceFail, false);
            return IsCurrentOwned;
        }

        public bool TryKillWall(int x, int y, bool fail, out ToolWorldSourceResult1458 result)
        {
            result = default;
            if (!IsCurrentOwned || work.Count != 0 || !Overlay.TryReadSourceSafely(x, y, out var before))
                return Reject();
            if (before.Wall == 0) return true;
            if (!VanillaWallDefinitionCatalog.TryGet(before.WallType, out _) ||
                !VanillaToolTileCatalog1458.TryGet(before.TileType, out var tileFacts)) return Reject();
            bool sourceFail = fail || !VanillaWallBreakRules1458.IsProgressionUnlocked(before.WallType,
                skeletronDowned, golemDowned);
            int dustCount = sourceFail ? 3 : 10;
            for (int index = 0; index < dustCount; index++)
                if (!VanillaToolDust1458.TryAdvanceSuppressedWallParticle(before.WallType, random.Next)) return Reject();
            if (!sourceFail)
            {
                if (!clientRole)
                {
                    if (server is null || !VanillaWallDropCatalog1458.TryResolve(before.WallType, out var item)) return Reject();
                    if (!item.IsNone && !server.TryStageSourceItem(item, 1, x, y)) return Reject();
                }
                var after = before; after.Wall = 0; after.WallColor = 0;
                after.Flags &= ~(WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall);
                if (!Stage(WorldTileMutationKind.KillWall, x, y, in before, in after)) return Reject();
            }
            if (!FrameWalls(x, y)) return Reject();
            if (!sourceFail && tileFacts.FramesOnKillWall &&
                (!Push(new(StepKind.Frame, x, y)) || !Drain())) return Reject();
            result = new(!sourceFail, sourceFail, false);
            return IsCurrentOwned;
        }

        private bool FrameWalls(int x, int y)
        {
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    int tx = x + dx, ty = y + dy;
                    var dimensions = Overlay.Tiles.Dimensions;
                    if (tx <= 0 || ty <= 0 || tx >= dimensions.WidthTiles - 1 || ty >= dimensions.HeightTiles - 1) continue;
                    if (!Overlay.TryRead(tx, ty, out var before) ||
                        !VanillaToolTileCatalog1458.TryGetWallFrameMode(before.WallType, out byte mode)) return false;
                    if (before.Wall == 0)
                    {
                        var after = before; after.WallColor = 0;
                        after.Flags &= ~(WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall);
                        if (!before.Equals(after) && !Stage(WorldTileMutationKind.TransformTile, tx, ty, in before, in after)) return false;
                        continue;
                    }
                    // Wall frame fields are cosmetic, not part of WorldTile. Guard
                    // all source neighbor reads even when only the reset RNG is retained.
                    if (!Overlay.RecordRead(tx, ty - 1) || !Overlay.RecordRead(tx - 1, ty) ||
                        !Overlay.RecordRead(tx + 1, ty) || !Overlay.RecordRead(tx, ty + 1)) return false;
                    if (dx == 0 && dy == 0 && mode is not (1 or 2))
                    { random.Next(3); if (before.Wall == 21) random.Next(2); }
                }
            return true;
        }

        public bool TrySlopeTile(int x, int y, byte slope, out ToolWorldSourceResult1458 result) =>
            TryShape(x, y, false, slope, ToolWorldShapeOperation1458.Slope, out result);

        public bool TryPoundTile(int x, int y, out ToolWorldSourceResult1458 result)
        {
            result = default;
            if (!Overlay.TryRead(x, y, out var before)) return Reject();
            return TryShape(x, y, !before.IsHalfBrick, before.SourceSlope,
                ToolWorldShapeOperation1458.Pound, out result);
        }

        public bool TryResetSlopeAndPound(int x, int y, out ToolWorldSourceResult1458 result)
        {
            result = default;
            if (!IsCurrentOwned || work.Count != 0 || !Overlay.TryRead(x, y, out var original) ||
                !VanillaToolWorldRules1458.TryEvaluate(Overlay.Tiles, Overlay, x, y, hardMode, out var permission)) return Reject();
            if (!permission.CanPound) return true;
            var rawReset = original;
            rawReset.TrySetSourceShape(original.IsHalfBrick, 0);
            if (!Stage(WorldTileMutationKind.TransformTile, x, y, in original, in rawReset)) return Reject();
            // Player performs a raw slope write, then exactly ONE PoundTile call.
            // SlopeTile here would introduce another dust/frame/RNG cycle.
            return TryShape(x, y, !rawReset.IsHalfBrick, 0, ToolWorldShapeOperation1458.Pound,
                out result, original);
        }

        private bool TryShape(int x, int y, bool half, byte slope,
            ToolWorldShapeOperation1458 operation, out ToolWorldSourceResult1458 result,
            WorldTile? sourceBefore = null)
        {
            result = default;
            if (!IsCurrentOwned || slope > 4 || work.Count != 0 ||
                !Overlay.TryRead(x, y, out var before) ||
                !VanillaToolWorldRules1458.TryEvaluate(Overlay.Tiles, Overlay, x, y, hardMode, out var permission))
                return Reject();
            if (!permission.CanPound) return true;
            var after = before;
            after.TrySetSourceShape(half, slope);
            if (!Stage(WorldTileMutationKind.SetToolShape, x, y, in before, in after) ||
                !AdvanceDust(in after, true) || !PushSquare(x, y) || !Drain() ||
                !Overlay.TryRead(x, y, out var final)) return Reject();
            bool lift = operation == ToolWorldShapeOperation1458.Slope ? final.SourceSlope == 0 : !final.IsHalfBrick;
            var original = sourceBefore ?? before;
            server?.RecordSourceShape(x, y, in original, in final, operation, lift);
            result = new(!original.Equals(final), false, lift);
            return IsCurrentOwned;
        }

        private bool Drain()
        {
            while (work.TryPop(out var next))
            {
                if (++steps > MaximumSteps || !IsCurrentOwned) return Reject();
                bool accepted = next.Kind switch
                {
                    StepKind.Kill => Kill(in next),
                    StepKind.Frame => Frame(in next),
                    StepKind.CrackedNeighbor => CrackedNeighbor(in next),
                    StepKind.CrackedAfterNeighbor => CrackedAfterNeighbor(in next),
                    StepKind.KillAfterSpecial => KillAfterSpecial(in next),
                    StepKind.ContainerNextCell => ContainerNextCell(next.Index),
                    StepKind.FossilNeighbor => FossilNeighbor(in next),
                    StepKind.FossilAfterNeighbor => FossilAfterNeighbor(in next),
                    StepKind.KillAfterPreEffects => KillAfterPreEffects(in next),
                    StepKind.SaplingNextCell => SaplingNextCell(in next),
                    StepKind.PlantKillComplete => FinishPlantKill(),
                    StepKind.LarvaNextCell => LarvaNextCell(next.Index),
                    StepKind.LarvaAfterKills => LarvaAfterKills(),
                    StepKind.FurnitureNextCell => FurnitureNextCell(next.Index),
                    StepKind.PalmAfterKill => PalmContinuation(next.X, next.Y),
                    StepKind.FallingAfterFrame => server is not null && server.TryStageSourceTileSquare(next.X, next.Y),
                    _ => false
                };
                if (!accepted) return Reject();
            }
            return IsCurrentOwned;
        }

        private bool Kill(in SourceStep step)
        {
            if (!Overlay.TryReadSourceSafely(step.X, step.Y, out var before)) return false;
            if (!before.IsActive) return true;
            if (!VanillaToolWorldRules1458.TryEvaluate(Overlay.Tiles, Overlay,
                step.X, step.Y, hardMode, out var permission) ||
                !VanillaToolTileCatalog1458.TryGet(before.TileType, out var tileFacts)) return false;
            if (permission.Breakability == ToolWorldBreakability1458.NoEffect) return true;
            if (IsExceptionalFurniture(before.Type))
            {
                if (!TryFurnitureBeforeDust(in step, ref before, out bool stop)) return false;
                if (stop) return true;
            }
            bool sourceFail = step.Fail || permission.Breakability == ToolWorldBreakability1458.FailedEffect;
            // Stored items/progression and complete object footprints require their
            // typed metadata participant, never a raw cell-clear fallback.
            if (HasStoredOrProgressionEffect(before.Type) && !IsExceptionalFurniture(before.Type) && !AllowsContainerKill(before.Type) &&
                !(step.Fail && before.Type is 21 or 467 or 88 or 26)) return false;
            if (before.Type == LarvaTile && !TryVerifyLarvaFootprint(step.X, step.Y, in before, out _, out _)) return false;
            if (!AdvanceDust(in before, sourceFail)) return false;
            if (step.EffectOnly) return true;
            if (before.Type == 404 && !clientRole && !fossilBreak)
            {
                fossilBreak = true;
                return Push(step with { Kind = StepKind.FossilNeighbor, Fail = sourceFail, SourceTile = before, Index = 0 });
            }
            return KillAfterPreEffects(step with { Fail = sourceFail, SourceTile = before });
        }

        private bool KillAfterPreEffects(in SourceStep step)
        {
            var before = step.SourceTile;
            bool sourceFail = step.Fail;
            if (!VanillaToolTileCatalog1458.TryGet(before.TileType, out var tileFacts)) return false;
            if (sourceFail)
            {
                if (!clientRole && tileFacts.IsShakeable && !TryShakeTree(step.X, step.Y, in before)) return false;
                var transformed = before;
                transformed.Type = before.Type switch
                {
                    2 or 23 or 109 or 199 or 477 or 492 => 0,
                    633 => 57,
                    60 or 661 or 662 or 70 => 59,
                    _ => tileFacts.IsMoss ? (ushort)1 : tileFacts.IsMossBrick ? (ushort)38 : before.Type
                };
                if (!before.Equals(transformed) &&
                    !Stage(WorldTileMutationKind.TransformTile, step.X, step.Y, in before, in transformed)) return false;
                return PushSquare(step.X, step.Y);
            }
            if (!clientRole && ((facts.GoodWorld && before.Type is 57 or 230) ||
                (facts.GoodWorld && facts.RemixWorld && !facts.DrunkWorld && before.Type is 147 or 224))) return false;
            if (!clientRole && before.Type is >= 481 and <= 483)
                return Push(step with { Kind = StepKind.CrackedNeighbor, Index = 0, SourceTile = before });
            return KillAfterSpecial(step with { SourceTile = before });
        }

        private bool KillAfterSpecial(in SourceStep step)
        {
            var before = step.SourceTile;
            if (!clientRole && !step.NoItem && !StageDrops(step.X, step.Y, in before)) return false;
            var after = before;
            after.Type = 0;
            after.FrameX = -1; after.FrameY = -1;
            after.TileColor = 0;
            after.ClearSourceShape();
            after.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.Inactive |
                WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
            if (!clientRole && before.Type == 58 && step.Y > owner.tiles!.Dimensions.HeightTiles - 200)
            { after.LiquidKind = WorldLiquidKind.Lava; after.LiquidAmount = 128; }
            if (!Overlay.TryReadSourceSafely(step.X, step.Y, out var current) ||
                !Stage(WorldTileMutationKind.ClearTile, step.X, step.Y, in current, in after)) return false;
            return PushSquare(step.X, step.Y);
        }

        private bool StageDrops(int x, int y, in WorldTile before)
        {
            if (server is null) return false;
            if (IsExceptionalFurniture(before.Type) || AllowsContainerKill(before.Type) || before.Type is 20 or LarvaTile) return true;
            if (IsSourcePlant(before.Type)) return TryStagePlantDrops(x, y, in before);
            if (before.Type is 5 or 72 or 80 or 323 or >= 583 and <= 589 or 596 or 616 or 634)
            {
                if (!TryResolveTreeDrops(x, y, in before, out var drops)) return false;
                return (drops.Primary.IsNone || server.TryStageSourceItem(drops.Primary, drops.PrimaryStack, x, y)) &&
                    (drops.Secondary.IsNone || server.TryStageSourceItem(drops.Secondary, drops.SecondaryStack, x, y));
            }
            return server.TryStageSourceSimpleDrop(x, y, in before);
        }

        private bool AdvanceDust(in WorldTile tile, bool fail)
        {
            var facts = new ToolDustTile1458(tile.TileType, tile.FrameX, tile.FrameY);
            if (!VanillaToolDust1458.TryGetAmount(in facts, fail, out int amount)) return false;
            for (int index = 0; index < amount; index++)
                if (!VanillaToolDust1458.TryAdvanceSuppressedParticle(in facts, random.Next)) return false;
            return true;
        }

        private bool Frame(in SourceStep step)
        {
            var dimensions = Overlay.Tiles.Dimensions;
            if (step.X <= 5 || step.Y <= 5 || step.X >= dimensions.WidthTiles - 5 || step.Y >= dimensions.HeightTiles - 5)
                return true;
            if (!Overlay.TryRead(step.X, step.Y, out var tile)) return false;
            if (!tile.IsActive)
            {
                var cleared = tile;
                cleared.ClearSourceShape(); cleared.TileColor = 0;
                cleared.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
                return tile.Equals(cleared) || Stage(WorldTileMutationKind.TransformTile, step.X, step.Y, in tile, in cleared);
            }
            if (!VanillaTileDefinitionCatalog.TryGet(tile.TileType, out var definition)) return false;
            if (IsFramedTree(tile.Type)) return FrameTree(step.X, step.Y, in tile);
            if (tile.Type == 80) return FrameCactus(step.X, step.Y, in tile);
            if (!VanillaToolTileCatalog1458.TryGet(tile.TileType, out var sourceFacts)) return false;
            if (sourceFacts.IsPlatform) return FramePlatform(step.X, step.Y, in tile);
            if (!clientRole && IsSourceFallingTile(tile.Type))
            {
                if (!TrySourceFalling(step.X, step.Y, in tile, out bool fell)) return false;
                if (fell) return true;
            }
            if (IsSourcePlant(tile.Type)) return FramePlant(step.X, step.Y, in tile);
            if (tile.Type == 20) return FrameSapling(step.X, step.Y, in tile);
            if (tile.Type is 21 or 467 or 88) return FrameContainer(step.X, step.Y, in tile);
            if (tile.Type == 26) return TryVerifyIntactAltar(step.X, step.Y, in tile);
            if (tile.Type is 49 or 136) return FrameSingleCell(step.X, step.Y, in tile);
            if (tile.Type is 137 or 424 or 442) return TryFrameToolFurniture(step.X, step.Y, in tile);
            if (tile.Type == LarvaTile) return FrameLarva(step.X, step.Y, in tile);
            if (IsExceptionalFurniture(tile.Type)) return FrameExceptionalFurniture(step.X, step.Y, in tile);
            if (tile.Type == VanillaMinecartTrackFrame1458.TileType) return FrameTrack(step.X, step.Y, in tile);
            if (definition.IsFrameImportant) return false;
            // Source non-important cosmetic frames are not file state. Their
            // reset draw remains part of both role RNG streams. The special
            // SelfFrame8Way path also consumes one reset sample.
            if (step.ResetFrame) random.Next(3);
            return true;
        }

        private bool FramePlatform(int x, int y, in WorldTile tile)
        {
            Span<WorldTile> window = stackalloc WorldTile[9];
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (!Overlay.TryReadSourceSafely(x + dx, y + dy, out window[(dy + 1) * 3 + dx + 1])) return false;
            // Visibility changes only cosmetic merge culling; this source pass
            // uses the dedicated/default hidden-echo projection, not a client asset renderer.
            if (!VanillaToolPlatformFrames1458.TryPlan(in tile, window, false, out short frameX)) return false;
            var after = tile; after.FrameX = frameX;
            return after.Equals(tile) || Stage(WorldTileMutationKind.TransformTile, x, y, in tile, in after);
        }

        private bool FrameTree(int x, int y, in WorldTile tile)
        {
            if (!Overlay.TryReadSourceSafely(x, y - 1, out var up) ||
                !Overlay.TryReadSourceSafely(x, y + 1, out var down) ||
                !Overlay.TryReadSourceSafely(x - 1, y, out var left) ||
                !Overlay.TryReadSourceSafely(x + 1, y, out var right) ||
                !VanillaToolTreeFrames1458.TryGetGroundValidity(tile.Type, in down, out bool ground) ||
                !VanillaToolTreeFrames1458.TryPlan(in tile, in up, in down, in left, in right, ground, out var frame)) return false;
            if (frame.Kill)
            {
                if (frame.TopRoll == ToolTreeTopRoll1458.PalmAfterKill && !Push(new(StepKind.PalmAfterKill, x, y))) return false;
                return Push(new(StepKind.Kill, x, y));
            }
            var after = tile;
            after.FrameX = frame.FrameX; after.FrameY = frame.FrameY;
            if (frame.TopRoll == ToolTreeTopRoll1458.Palm) after.FrameX = (short)(random.Next(7, 10) * 22);
            else if (frame.TopRoll == ToolTreeTopRoll1458.Mushroom)
            { after.FrameX = 18; after.FrameY = (short)(random.Next(3) * 18); }
            if (!after.Equals(tile) && !Stage(WorldTileMutationKind.TransformTile, x, y, in tile, in after)) return false;
            return !frame.FrameOrthogonalNeighbours || PushOrthogonal(x, y);
        }

        private bool PalmContinuation(int x, int y)
        {
            if (!Overlay.TryRead(x, y, out var before)) return false;
            if (before.FrameX > 44) return true;
            var after = before;
            after.FrameX = (short)(random.Next(7, 10) * 22);
            return Stage(WorldTileMutationKind.TransformTile, x, y, in before, in after);
        }

        private bool FrameCactus(int x, int y, in WorldTile tile)
        {
            // The source root-column walk is shared by CheckCactus and its
            // cosmetic frame. Never infer a trunk from a normalized frame alone.
            int rootX = x, groundY = y;
            while (true)
            {
                if (!Overlay.TryReadSourceSafely(rootX, groundY, out var current)) return false;
                if (!current.IsActive || current.Type != 80) break;
                groundY++;
                if (!Overlay.TryReadSourceSafely(rootX, groundY, out var below)) return false;
                if (below.IsActive && below.Type == 80) continue;
                if (!Overlay.TryReadSourceSafely(rootX - 1, groundY, out var left) ||
                    !Overlay.TryReadSourceSafely(rootX - 1, groundY - 1, out var leftAbove)) return false;
                if (rootX >= x && IsCactus(in left) && IsCactus(in leftAbove)) rootX--;
                if (!Overlay.TryReadSourceSafely(rootX + 1, groundY, out var right) ||
                    !Overlay.TryReadSourceSafely(rootX + 1, groundY - 1, out var rightAbove)) return false;
                if (rootX <= x && IsCactus(in right) && IsCactus(in rightAbove)) rootX++;
            }
            if (!Overlay.TryReadSourceSafely(rootX, groundY, out var ground) ||
                !Overlay.TryReadSourceSafely(x, y + 1, out var down) ||
                !Overlay.TryReadSourceSafely(x - 1, y, out var l) ||
                !Overlay.TryReadSourceSafely(x + 1, y, out var r)) return false;
            if (VanillaToolCactusFrames1458.ShouldKill(in ground, x - rootX, in down, in l, in r))
                return Push(new(StepKind.Kill, x, y));
            if (!Overlay.TryReadSourceSafely(x - 2, y, out var ll) ||
                !Overlay.TryReadSourceSafely(x, y - 1, out var up) ||
                !Overlay.TryReadSourceSafely(x - 1, y + 1, out var ld) ||
                !Overlay.TryReadSourceSafely(x + 1, y + 1, out var rd) ||
                !VanillaToolTileCatalog1458.TryGet(down.TileType, out var downFacts)) return false;
            var frame = VanillaToolCactusFrames1458.PlanFrame(in tile, x - rootX, in ll, in l,
                in r, in up, in down, in ld, in rd, downFacts.IsSolid);
            var after = tile; after.FrameX = frame.X; after.FrameY = frame.Y;
            if (!after.Equals(tile) && !Stage(WorldTileMutationKind.TransformTile, x, y, in tile, in after)) return false;
            if (frame.ReframeCosmeticNeighbours)
            {
                // LIFO reverse of the source's left/up/down/right order.
                return Push(new(StepKind.Frame, x + 1, y)) && Push(new(StepKind.Frame, x, y + 1)) &&
                    Push(new(StepKind.Frame, x, y - 1)) && Push(new(StepKind.Frame, x - 1, y));
            }
            return true;
        }

        private bool Stage(WorldTileMutationKind kind, int x, int y, in WorldTile before, in WorldTile after)
        {
            if (before.Equals(after)) return true;
            if (mutationOrdinal == MaximumSteps) return Reject();
            if (!(server is null ? Overlay.TryStage(x, y, in after) : server.TryStageSourceCell(kind, x, y, in before, in after))) return false;
            mutationOrdinal++;
            return true;
        }

        private bool PushSquare(int x, int y)
        {
            for (int dx = 1; dx >= -1; dx--)
                for (int dy = 1; dy >= -1; dy--)
                    if (!Push(new(StepKind.Frame, x + dx, y + dy, ResetFrame: dx == 0 && dy == 0))) return false;
            return true;
        }
        private bool PushOrthogonal(int x, int y) =>
            Push(new(StepKind.Frame, x, y + 1)) && Push(new(StepKind.Frame, x, y - 1)) &&
            Push(new(StepKind.Frame, x + 1, y)) && Push(new(StepKind.Frame, x - 1, y));
        private bool Push(in SourceStep step)
        {
            if (work.Count >= MaximumSteps) return Reject();
            work.Push(step); return true;
        }
        private bool Reject() { failed = true; return false; }
        private static bool IsCactus(in WorldTile tile) => tile.IsActive && tile.Type == 80;
        private static bool IsFramedTree(ushort type) => type is 5 or 72 or 323 or >= 583 and <= 589 or 596 or 616 or 634;
        private static bool HasStoredOrProgressionEffect(ushort type) =>
            type is 21 or 26 or 31 or 88 or 128 or 269 or 334 or 395 or 423 or 441 or 467 or 470 or 471 or
            475 or 488 or 520 or 597 or 619 or 621 or 695 or 696 or 698 or 704 or 723 or 724;
    }
}
