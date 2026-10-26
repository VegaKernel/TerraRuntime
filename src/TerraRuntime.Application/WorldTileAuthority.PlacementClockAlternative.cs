using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal bool TryPrepareIncompletePlacementClock(RuntimePlayerToolUse1458 tools,
        in RuntimePlayerUpdateWorld1458 world, ReadOnlySpan<RuntimePlayerInventoryItem> inventory,
        float pickSpeed, VanillaUnifiedRandom1458 beforeRandom, out ToolClockState1458 clocks,
        out ToolHitState1458 replacementHits, out VanillaUnifiedRandom1458? afterRandom,
        out ToolWorldReadPreparation? reads)
    {
        clocks = default; replacementHits = tools.ReplacementHits; afterRandom = null; reads = null;
        if (tiles is null || tools.SourcePlacement is not { IsWall: false } placement ||
            placement.CreateTile is not (0 or 1 or 30 or 40) || tools.SourceStagePose is not { } pose ||
            tools.BuildingPermission is not { IsCurrent: true, NoBuilding: false } || tools.Reach is not { } reach ||
            tools.PlacementReady is not { } history || history.UseEpoch != tools.PlacementHistoryEpoch ||
            history.Clocks.Selected.ItemTime != 0 || history.Clocks.Selected.Animation <= 0 ||
            (pose.Player.ControlFlags & 32) == 0 || inventory.Length != 219 ||
            !float.IsFinite(pickSpeed) || pickSpeed <= 0 || !world.TryCaptureToolDamageWorld(out var facts)) return false;
        ToolDefinition1458? best = null;
        for (int slot = 0; slot < 50; slot++)
        {
            var item = inventory[slot];
            if (item.IsEmpty || !VanillaToolCatalog1458.TryGet(item.ItemType, out var baseTool) || baseTool.Pick <= 0) continue;
            if (!VanillaToolCatalog1458.TryResolve(item.ItemType, item.Prefix,
                world.RemixWorld, world.WindowsItemPrefixArithmetic, out var tool)) return false;
            if (best is null || tool.Pick > best.Value.Pick) best = tool;
        }
        if (best is not { } pick) return false;
        double product = world.WindowsItemPrefixArithmetic ? pick.UseTime * (double)pickSpeed : pick.UseTime * pickSpeed;
        if (!double.IsFinite(product) || product > short.MaxValue) return false;
        int time = Math.Max(1, (int)product);
        if (time >= history.Clocks.Selected.Animation) return false; // Only the early-ready sibling needs this branch.
        var witness = CreateToolReadPreparation();
        if (witness is null) return false;
        int rangeX = Math.Min(reach.TileRangeX, 20) + reach.TileBoost;
        int rangeY = Math.Min(reach.TileRangeY, 20) + reach.TileBoost;
        int lowX = Math.Max(2, (int)(pose.Player.PositionX / 16f) - rangeX);
        int lowY = Math.Max(2, (int)(pose.Player.PositionY / 16f) - rangeY);
        int highX = Math.Min(world.MaxTilesX - 3, (int)Math.Ceiling((pose.Player.PositionX + pose.Width) / 16f) - 1 + rangeX);
        int highY = Math.Min(world.MaxTilesY - 3, (int)Math.Ceiling((pose.Player.PositionY + pose.Height) / 16f) - 1 + rangeY);
        if (highX - lowX > 40 || highY - lowY > 40) return false;
        Span<ToolTargetTile1458> window = stackalloc ToolTargetTile1458[VanillaToolDamage1458.TargetWindowCount];
        for (int x = lowX; x <= highX; x++)
        for (int y = lowY; y <= highY; y++)
        {
            if (!witness.TryReadBefore(x, y, out var cell) || !witness.TryReadBefore(x, y - 1, out var above)) return false;
            // Exact ordinary replacement subset. Above-active special anchors,
            // falling/torch/object drop branches remain outside this witness.
            if (!cell.IsActive || cell.Type is not (1 or 226) || cell.Type == placement.CreateTile ||
                cell.Wall == 350 || above.IsActive || placement.Item.Value is 3 or 1141) continue;
            if (!witness.TryCaptureTargetBefore(x, y, window) ||
                !VanillaToolWorldRules1458.TryEvaluate(tiles, x, y,
                    hardModeBaseline || progression.IsCompleted(VanillaWorldProgressionId.Hardmode), out var permission) ||
                !permission.CanKill || !VanillaToolDamage1458.TryGetPickDamage(pick.Pick, x, y, window, facts, out var preliminary) ||
                !tools.ReplacementHits.TryHitObject(x, y, 1, out var hits, out int hitSlot) ||
                !VanillaToolDamage1458.TryFinalizePickDamage(preliminary, cell.TileType, true, facts.GetGoodWorld,
                    true, hits, hitSlot, pick.Pick, -1, out int damage) || damage <= 0 ||
                !hits.TryAddDamage(hitSlot, damage, true, out var result, out int sum, out int samples)) continue;
            if (sum >= 100) continue; // A successful replacement must have its visible21/pay tail.
            var preview = beforeRandom.Clone();
            for (int sample = 0; sample < samples; sample++) preview.NextDouble();
            var dust = new ToolDustTile1458(cell.TileType, cell.FrameX, cell.FrameY);
            if (!VanillaToolDust1458.TryGetAmount(dust, true, out int amount)) return false;
            for (int particle = 0; particle < amount; particle++)
                if (!VanillaToolDust1458.TryAdvanceSuppressedParticle(dust, preview.Next)) return false;
            clocks = history.Clocks with { Selected = history.Clocks.Selected with
                { ItemTime = time, ItemTimeMax = time, Animation = time, AnimationMax = time } };
            replacementHits = result; afterRandom = preview; reads = witness;
            return witness.IsCurrent && tools.BuildingPermission.Value.IsCurrent;
        }
        return false;
    }
}
