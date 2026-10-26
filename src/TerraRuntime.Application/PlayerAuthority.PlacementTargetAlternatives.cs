using TerraRuntime.Core;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private RuntimePlayerPlacementAlternative1458? AdvancePlacementTargets(RemoteItemPhasePlan1458 plan,
        in RuntimePlayerUpdateWorld1458 world, VanillaUnifiedRandom1458 beforeCommon,
        VanillaUnifiedRandom1458 afterCommon, in ToolFrameFacts1458 facts,
        RuntimePlayerPlacementAlternative1458? selectionAlternative)
    {
        if (plan.Item is not { ToolUse: { SourcePlacement: { IsWall: false } placement,
                SourceStagePose: { } pose, Reach: { } reach } tools } item ||
            toolWorld is not { } sourceWorld || tools.Input is not null ||
            tools.SourceSlot != plan.Member.SelectedItem || tools.DeferredSelectionSlot is not null ||
            reach.TileRangeX != 5 || reach.TileRangeY != 3 || reach.TileBoost != 0 ||
            pose.Width != 20 || pose.Height != 42 || !HasSinglePlacementAlternativeActor(plan) ||
            !inventory.TryCaptureShopJournal(plan.Member.Connection, out var image) || image is null)
            return selectionAlternative;
        var old = plan.BeforeItem?.ToolUse?.PlacementAlternative;
        bool retain = old is not null && old.HistoryEpoch == tools.PlacementHistoryEpoch &&
            old.MatchesCanonical(beforeCommon) && IsPlacementAlternativeContext(plan, old, world);
        var reads = selectionAlternative?.Reads ?? (retain ? old!.Reads : sourceWorld.CreateToolReadPreparation());
        if (reads is null || !reads.IsCurrent) return selectionAlternative;
        var targets = new List<RuntimePlayerPlacementTarget1458>();
        if (retain)
            foreach (var prior in old!.Targets)
            {
                if (!reach.Contains(pose, prior.X, prior.Y)) continue;
                var preview = prior.CreatePreview();
                int roll = preview.Next(3);
                var localFacts = facts with { LocalOwner = true, SelectionBuffered = false,
                    LastUseSuccess = tools.LastUseSuccess, AnimationMultiplier = tools.PlacementSpeed };
                var definition = placement.Clocks;
                if (!VanillaLocalItemUse1458.TryAdvanceFrame(definition, prior.Clocks, localFacts,
                        roll, out var use) || use.ShootBoundary ||
                    !VanillaLocalItemUse1458.TryEnterOwnerStage(definition, use.State,
                        use.LocalToolStage, out var clocks) || prior.RemoteCrit < int.MinValue + 2) continue;
                var hits = prior.ReplacementHits;
                if (clocks.Selected.ItemTime == 0 && clocks.Selected.Animation > 0 && facts.ControlUseItem)
                {
                    var actionBefore = clocks;
                    if (!sourceWorld.TryPrepareIncompletePlacementAt(tools, world, image.Before, actionBefore,
                        hits, item.PermanentSpeeds?.PickSpeed ?? item.DerivedCombat!.Value.PickSpeed,
                        prior.X, prior.Y, preview, reads, out clocks, out hits, out var acted) || acted is null)
                        continue; // A completed world leg must have its visible21/payment cohort.
                    preview = acted;
                }
                targets.Add(new(prior.X, prior.Y, clocks, hits, preview,
                    prior.RemoteCrit - (roll == 0 ? 2 : 0)));
            }
        if (tools.Clocks.Selected.ItemTime == 0 && tools.Clocks.Selected.Animation > 0 && facts.ControlUseItem)
        {
            int lowX = Math.Max(2, (int)(pose.Player.PositionX / 16f) - reach.TileRangeX);
            int lowY = Math.Max(2, (int)(pose.Player.PositionY / 16f) - reach.TileRangeY);
            int highX = Math.Min(world.MaxTilesX - 3,
                (int)Math.Ceiling((pose.Player.PositionX + pose.Width) / 16f) - 1 + reach.TileRangeX);
            int highY = Math.Min(world.MaxTilesY - 3,
                (int)Math.Ceiling((pose.Player.PositionY + pose.Height) / 16f) - 1 + reach.TileRangeY);
            if ((long)(highX - lowX + 1) * (highY - lowY + 1) > RuntimePlayerPlacementAlternative1458.MaximumTargets)
                return selectionAlternative;
            for (int x = lowX; x <= highX; x++)
            for (int y = lowY; y <= highY; y++)
            {
                if (targets.Any(t => t.X == x && t.Y == y)) continue;
                if (sourceWorld.TryPrepareIncompletePlacementAt(tools, world, image.Before, tools.Clocks,
                    tools.ReplacementHits, item.PermanentSpeeds?.PickSpeed ?? item.DerivedCombat!.Value.PickSpeed,
                    x, y, afterCommon, reads, out var clocks, out var hits, out var preview) && preview is not null)
                    targets.Add(new(x, y, clocks, hits, preview, item.Selected.RevolverCritBonus));
            }
        }
        if (targets.Count == 0 || !reads.IsCurrent) return selectionAlternative;
        var root = selectionAlternative ?? new RuntimePlayerPlacementAlternative1458(afterCommon, afterCommon,
            tools.Clocks, tools.ReplacementHits, tools.PlacementHistoryEpoch, item, image, reads,
            plan.Buffs, plan.Equipment, plan.Appearance, item.Selected.RevolverCritBonus,
            item.Selected.RevolverCritBonus, plan.Member.OwnedPermanentUpgradeMask, world);
        // A shared witness accumulates the whole bounded candidate read set;
        // every retained branch must remain compatible with the actual world.
        return root.WithTargets(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(targets),
            selectionAlternative is not null);
    }
}
