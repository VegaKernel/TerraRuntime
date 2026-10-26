using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Tiles;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    // One detached no-wire replacement leg. The enclosing ToolUse history owns
    // its clock/RNG endpoints; this method neither adopts state nor grants an
    // observed action, quantity or world mutation.
    internal bool TryPrepareIncompletePlacementAt(RuntimePlayerToolUse1458 tools,
        in RuntimePlayerUpdateWorld1458 world, ReadOnlySpan<RuntimePlayerInventoryItem> inventory,
        in ToolClockState1458 ownerStage, ToolHitState1458 replacementBefore,
        float pickSpeed, int x, int y, VanillaUnifiedRandom1458 beforeRandom,
        ToolWorldReadPreparation reads, out ToolClockState1458 after,
        out ToolHitState1458 replacementAfter, out VanillaUnifiedRandom1458? afterRandom)
    {
        after = default; replacementAfter = replacementBefore; afterRandom = null;
        if (tiles is null || tools.SourcePlacement is not { IsWall: false } placement ||
            tools.SourceStagePose is not { } pose || tools.Reach is not { } reach ||
            tools.BuildingPermission is not { IsCurrent: true, NoBuilding: false } ||
            inventory.Length != 219 || !reach.Contains(pose, x, y) ||
            (pose.Player.ControlFlags & 32) == 0 || !reads.IsCurrent ||
            !world.TryCaptureToolDamageWorld(out var damageWorld) ||
            !TryCaptureToolTreeWorld(out var context) || context is null || context.World != world)
            return false;
        ToolDefinition1458? best = null;
        for (int slot = 0; slot < 50; slot++)
        {
            var stock = inventory[slot];
            if (stock.IsEmpty || !VanillaToolCatalog1458.TryGet(stock.ItemType, out var baseTool) ||
                baseTool.Pick <= 0) continue;
            if (!VanillaToolCatalog1458.TryResolve(stock.ItemType, stock.Prefix,
                world.RemixWorld, world.WindowsItemPrefixArithmetic, out var pick)) return false;
            if (best is null || pick.Pick > best.Value.Pick) best = pick;
        }
        if (best is not { } selectedPick ||
            !VanillaLocalPlacement1458.TryPlanIncompleteReplacementClock(ownerStage,
                selectedPick.UseTime, pickSpeed, world.WindowsItemPrefixArithmetic, out after)) return false;
        var preview = beforeRandom.Clone();
        var overlay = new ToolWorldTileOverlay1458(tiles);
        if (!TryCreateToolSourcePass(overlay, preview, true, context.HardMode,
                context.Facts, [], null, out var local) || local is null ||
            !local.TryGetOrdinaryReplacementDrop(x, y, new(placement.CreateTile), placement.Item,
                selectedPick.Pick, out bool eligible) || !eligible) return false;
        Span<ToolTargetTile1458> window = stackalloc ToolTargetTile1458[VanillaToolDamage1458.TargetWindowCount];
        if (!reads.TryCaptureTargetBefore(x, y, window) ||
            !reads.TryReadBefore(x, y - 1, out _) ||
            !reads.TryReadBefore(x, y, out var before) ||
            !VanillaToolDamage1458.TryGetPickDamage(selectedPick.Pick, x, y, window, damageWorld, out var preliminary) ||
            !replacementBefore.TryHitObject(x, y, 1, out var hits, out int slotIndex) ||
            !VanillaToolDamage1458.TryFinalizePickDamage(preliminary, before.TileType, true,
                damageWorld.GetGoodWorld, false, hits, slotIndex, selectedPick.Pick, -1, out int damage) ||
            damage <= 0 || !hits.TryAddDamage(slotIndex, damage, true, out replacementAfter,
                out int accumulated, out int samples) || accumulated >= 100 ||
            !local.TryAdvanceReplacementHit(samples) || !local.TryAdvanceIncompleteReplacementDust(before))
            return false;
        // The target window plus the explicit above read covers every
        // eligibility/damage query in this no-world-write source leg.
        if (!reads.IsCurrent || !context.IsCurrent || !local.IsCurrentOwned) return false;
        afterRandom = preview;
        return true;
    }

    internal sealed partial class ToolWorldSourcePass1458
    {
        internal bool TryAdvanceIncompleteReplacementDust(in TerraRuntime.World.WorldTile tile)
            => IsCurrentOwned && AdvanceDust(tile, true);
    }
}
