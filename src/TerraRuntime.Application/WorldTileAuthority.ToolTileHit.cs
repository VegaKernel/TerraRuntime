using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    // This is the pre-WorldGen part of ActuallyUseMiningTool, not use admission.
    // Preparing Hurt before constructing the role worlds lets their actor census
    // include the source death afterimage before tree/drop ownership is chosen.
    internal bool TryPrepareToolTileHit(PlayerToolAttemptPreparation player,
        VanillaUnifiedRandom1458 localRandom, int x, int y,
        out ToolTileHitPreparation1458? preparation)
    {
        preparation = null;
        bool hardMode = hardModeBaseline || progression.IsCompleted(VanillaWorldProgressionId.Hardmode);
        var capture = player.Capture;
        if (!player.IsCurrent || !capture.HasCurrentSourceStage || localRandom is null ||
            player.Before.SourceStagePose is not { } pose ||
            !VanillaToolCatalog1458.TryResolve(capture.Selected.ItemType, capture.Selected.Prefix,
                capture.World.RemixWorld, capture.World.WindowsItemPrefixArithmetic, out var tool) ||
            capture.World.GetGoodWorld is not { } good ||
            capture.World.DontStarveWorld is not { } constant ||
            CreateToolReadPreparation() is not { } reads || !reads.TryReadBefore(x, y, out var tile) ||
            !tile.IsActive || !VanillaToolTileCatalog1458.TryGet(tile.TileType, out var roles) ||
            !VanillaToolWorldRules1458.TryEvaluate(tiles!, x, y, hardMode, out var permission) ||
            permission.RequiresChestPermission ||
            !player.Before.Hits.TryHitObject(x, y, 1, out var hits, out int slot)) return false;

        // Hammer has source priority over axe on a hammer target. Pick/shovel
        // branches have their own damage and packet125 chronology.
        int damage;
        ToolSelfHurtKind1458? hurtKind = null;
        if (roles.IsHammerTarget)
        {
            if (tool.Hammer <= 0 ||
                !VanillaToolDamage1458.TryGetHammerDamage(tool.Hammer, tile.TileType,
                    hardMode, permission.CanKill, out damage, out bool altar)) return false;
            if (altar) hurtKind = ToolSelfHurtKind1458.Altar;
        }
        else if (roles.IsAxeTarget)
        {
            if (tool.Axe <= 0 || !VanillaToolDamage1458.TryGetAxeDamage(tool.Axe,
                tile.TileType, good, permission.CanKill, out damage)) return false;
            if (constant && damage > 0 && tile.Type == 80) hurtKind = ToolSelfHurtKind1458.Cactus;
        }
        else return false;

        PlayerAuthority.ToolSelfHurtPreparation? hurt = null;
        if (hurtKind is { } kind)
        {
            int direction = (pose.Player.ControlFlags & 64) != 0 ? 1 : -1;
            if (pose.Player.NpcLife is not { } sourceLife ||
                !VanillaToolSelfHurt1458.TryPrepare(kind, sourceLife, pose.Player.Luck,
                    direction, localRandom.Next, localRandom.NextDouble, out var request) ||
                !players.TryPrepareToolSelfHurt(player, in request, out hurt) || hurt is null) return false;
        }
        // DamageVar/Hurt precede AddDamage's visual Main.rand sample. Even an
        // immune cactus attempt still consumes DamageVar's source samples.
        if (!hits.TryAddDamage(slot, damage, true, out var after, out int accumulated,
            out int samples)) return false;
        for (int index = 0; index < samples; index++) localRandom.NextDouble();
        preparation = new(this, hardMode, player, reads, tool, tile, x, y, slot, after, damage, accumulated, hurt, localRandom);
        return preparation.IsCurrent;
    }

    internal sealed class ToolTileHitPreparation1458(WorldTileAuthority owner, bool hardMode,
        PlayerToolAttemptPreparation player,
        ToolWorldReadPreparation reads, ToolDefinition1458 tool, WorldTile before,
        int x, int y, int hitSlot, ToolHitState1458 hits, int damage, int accumulated,
        PlayerAuthority.ToolSelfHurtPreparation? hurt, VanillaUnifiedRandom1458 localRandom)
    {
        internal ToolDefinition1458 Tool => tool;
        internal WorldTile Before => before;
        internal int X => x;
        internal int Y => y;
        internal int HitSlot => hitSlot;
        internal ToolHitState1458 Hits => hits;
        internal int Damage => damage;
        internal int Accumulated => accumulated;
        internal PlayerAuthority.ToolSelfHurtPreparation? SelfHurt => hurt;
        internal VanillaUnifiedRandom1458 LocalRandom => localRandom;
        internal bool RequiresLucyMessage => accumulated >= 100 && tool.Item.Value == 5095 &&
            VanillaToolTileCatalog1458.TryGet(before.TileType, out var roles) &&
            (roles.IsTreeTrunk || before.Type is 323 or 80);
        internal bool RequiresTreeReplant => accumulated >= 100 && tool.Item.Value == 5295 &&
            VanillaToolTileCatalog1458.TryGet(before.TileType, out var roles) &&
            (roles.IsTreeTrunk || before.Type == 323) &&
            !(before.Type != 323 && before.FrameY is >= 132 and <= 176 && before.FrameX is 22 or 44);
        internal bool IsCurrent => player.IsCurrent && reads.IsCurrent && (hurt is null || hurt.IsCurrent) &&
            (owner.hardModeBaseline || owner.progression.IsCompleted(VanillaWorldProgressionId.Hardmode)) == hardMode;
    }
}
