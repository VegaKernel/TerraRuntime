using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    internal VanillaWaterCheckDiagnostic1458 WaterCheckLiquidsDuringGeneration()
    {
        bool previous = useGenerationSolidity, previousIgnore = ignoreWaterSolidity;
        useGenerationSolidity = true;
        ignoreWaterSolidity = true;
        try
        {
            var sink = new GenerationLiquidMergeSink(this);
            return new VanillaWorldLiquidSimulator1458(store, sideEffects: sink,
                random: new GenerationLiquidRandom(random)).WaterCheckDuringWorldGeneration(sink);
        }
        finally { useGenerationSolidity = previous; ignoreWaterSolidity = previousIgnore; }
    }

    private sealed class GenerationLiquidRandom(IWorldGenerationVanillaRandom random) : IVanillaLiquidRandom1458
    {
        public bool NextFullFromNearlyFullLevel() => random.Next(30) == 0;
    }

    internal void FinalizeLiquidsDuringGeneration(CancellationToken cancellationToken)
    {
        bool previous = useGenerationSolidity, previousPost = UsesPostShimmerSolidity;
        bool rollingCactusSolid = !previous || previousPost;
        useGenerationSolidity = true;
        UsesPostShimmerSolidity = rollingCactusSolid;
        try
        {
            new VanillaWorldLiquidSimulator1458(store).FinalizeDuringWorldGeneration(
                new GenerationLiquidMergeSink(this), rollingCactusSolid, cancellationToken);
        }
        finally
        {
            useGenerationSolidity = previous;
            UsesPostShimmerSolidity = previousPost;
        }
    }

    internal void CheckLiquidDuringGeneration(int x, int y, bool rollingCactusSolid = true)
    {
        bool previous = useGenerationSolidity, previousPost = UsesPostShimmerSolidity;
        useGenerationSolidity = true;
        UsesPostShimmerSolidity = rollingCactusSolid;
        try
        {
            new VanillaWorldLiquidSimulator1458(store).CheckLiquidDuringWorldGeneration(x, y,
                new GenerationLiquidMergeSink(this), rollingCactusSolid);
        }
        finally
        {
            useGenerationSolidity = previous;
            UsesPostShimmerSolidity = previousPost;
        }
    }

    private sealed class GenerationLiquidMergeSink(GenerationTileFraming1458 framing) : IVanillaLiquidTileSideEffectSink1458
    {
        private VanillaLiquidMergeTileRequest1458 prepared;
        private bool hasPrepared;

        public bool TryCutTile(int x, int y)
        {
            EnsureTargetOwned(x, y);
            framing.KillTile(x, y);
            return true;
        }

        public bool TryKillLiquidSensitiveObject(int x, int y)
        {
            EnsureTargetOwned(x, y, allowWetHive: true);
            framing.KillTile(x, y);
            return true; // Handled includes CheckTileBreakability refusing a protected platform.
        }

        public bool TryPrepareMergeTile(in VanillaLiquidMergeTileRequest1458 request)
        {
            if (hasPrepared)
                throw new InvalidOperationException("A generation liquid merge is already prepared.");
            if (request.MergeTileType.Value is not (56 or 229 or 230 or 659))
                throw new InvalidOperationException("Unknown generation liquid merge material.");
            WorldTile target = framing.At(request.X, request.Y);
            EnsureTargetOwned(request.X, request.Y);
            if (target.IsActive && framing.CanReplaceLiquidTarget(request.X, request.Y, request.MergeTileType.Value) &&
                VanillaTileDefinitionCatalog.TryGet(target.TileType, out VanillaTileDefinition definition) &&
                definition.DropRule.Kind == VanillaTileDropRuleKind.Contextual && target.Type != 19)
                throw new NotSupportedException("Generation liquid replacement needs this target's contextual drop RNG closure.");
            // Platform19's contextual drop is only GetItemDrop_Platforms(frameY/18), a pure style
            // lookup. All65,536 raw frameY values were checked in the untouched source: no tile/RNG
            // mutation. Generation suppresses the resulting Item.NewItem call.
            prepared = request;
            hasPrepared = true;
            return true;
        }

        private void EnsureTargetOwned(int x, int y, bool allowWetHive = false)
        {
            WorldTile target = framing.At(x, y);
            if (allowWetHive && target.IsActive && target.Type == 444 && framing.HasWetBeeHiveFootprint(x, y)) return;
            if (target.IsActive && target.Type is 85 or 231 or 334 or 378 or 395 or 404 or 444 or
                470 or 471 or 475 or 520 or 597 or 698)
                throw new NotSupportedException("Generation liquid mutation needs this target's retained entity or fossil owner.");
        }

        public void CommitPreparedMergeTile(in VanillaLiquidMergeTileRequest1458 request)
        {
            if (!hasPrepared || request != prepared)
                throw new InvalidOperationException("Generation liquid merge preparation changed before commit.");
            hasPrepared = false;
            prepared = default;
            int x = request.X, y = request.Y;
            ushort material = checked((ushort)request.MergeTileType.Value);
            if (!framing.At(x, y).IsActive || !framing.TryReplaceLiquidTarget(x, y, material))
            {
                framing.KillTile(x, y);
                framing.PlaceLiquidMaterial(x, y, material);
            }
            framing.SquareTileFrame(x, y);
        }

        public void AbortPreparedMergeTile()
        {
            prepared = default;
            hasPrepared = false;
        }
    }

    private bool CanReplaceLiquidTarget(int x, int y, int material)
    {
        WorldTile current = At(x, y);
        // Reset disables solidity of ShimmerBlock659 for generation. Other merge materials are ordinary solids.
        if (material == 659 || !(VanillaTileIds.IsPlatform(current.TileType) ||
            IsFramingSolid(current.TileType) &&
            !VanillaTileCollisionCatalog.IsSolidTop(current.TileType) &&
            (!VanillaWorldFrameImportance326.IsFrameImportant(current.Type) || IsGenerationRope(current.Type))))
            return false;
        if (current.Wall == 350)
            return false;
        WorldTile above = At(x, y - 1);
        if (!above.IsActive)
            return true;
        if (above.Type is 80 or 488)
            return false;
        if (above.Type != current.Type && VanillaLiquidInteractionFacts1458.PreventsReplacementWhenOnTop(above.TileType))
        {
            if (BreakabilityTreeType(above.Type))
                return above.FrameX == 66 && above.FrameY is >= 0 and <= 44 ||
                    above.FrameX == 88 && above.FrameY is >= 66 and <= 110 || above.FrameY >= 198;
            if (above.Type == 323)
                return above.FrameX is not (66 or 220);
            return false;
        }
        return true;
    }

    private bool TryReplaceLiquidTarget(int x, int y, ushort material)
    {
        if (!CanReplaceLiquidTarget(x, y, material))
            return false;
        WorldTile before = At(x, y);
        ConsumeTileDust(x, y, before);
        // ReplaceTile removes natural attachments before changing the support material.
        WorldTile above = At(x, y - 1);
        if (above.IsActive && (VanillaLiquidInteractionFacts1458.BreaksWhenSupportIsReplacedAbove(above.TileType) ||
            above.Type == 165 && above.FrameY is 36 or 54 or 90))
            KillTile(x, y - 1);
        WorldTile below = At(x, y + 1);
        if (below.IsActive && (VanillaLiquidInteractionFacts1458.BreaksWhenSupportIsReplacedBelow(below.TileType) ||
            below.Type == 165 && below.FrameY is 0 or 18 or 72))
            KillTile(x, y + 1);
        ref WorldTile target = ref At(x, y);
        target.Type = material;
        target.TileColor = 0;
        target.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        if (At(x, y - 1).IsActive && ForbidsSloping(x, y - 1) || !CanRemoveTile(x, y))
            target.ClearSourceShape();
        SquareTileFrame(x, y);
        return true;
    }

    private void PlaceLiquidMaterial(int x, int y, ushort material)
    {
        ref WorldTile target = ref At(x, y);
        // Forced PlaceTile still refuses an active generating/loading fallen log.
        if (target.IsActive && target.Type == 488)
            return;
        if (!target.IsActive)
        {
            target.ClearSourceShape();
            target.TileColor = 0;
            target.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
            target.FrameX = 0;
            target.FrameY = 0;
        }
        else if (!VanillaWorldFrameImportance326.IsFrameImportant(target.Type))
        {
            target.TrySetSourceShape(false, target.SourceSlope);
            target.FrameX = 0;
            target.FrameY = 0;
        }
        target.Type = material;
        target.Flags |= WorldTileFlags.Active;
        SquareTileFrame(x, y);
    }
}
