using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed record FishingItemAction1458(ulong Epoch, FishingPole1458 Pole,
    RuntimePlayerToolStagePose1458 Pose, FishingBobber1458? Pull = null,
    bool Caught = false, bool ConsumedBait = false, bool BrokeLine = false);

internal sealed record FishingLaunchReport1458(ConnectionHandle Connection,
    TerrariaProjectileUpdateState State, long Tick);

internal sealed partial class PlayerAuthority
{
    private readonly FishingLaunchReport1458?[] fishingLaunchReports = new FishingLaunchReport1458?[256];

    internal ToolReportObservationResult1458 ObserveFishingShoot(ConnectionHandle connection,
        in TerrariaProjectileUpdateState packet)
    {
        if (!FishingRules1458.IsBobber(packet.ProjectileType)) return ToolReportObservationResult1458.NotApplicable;
        if (!membership.TryGet(connection, out var member) || member.IsDead ||
            !inventory.TryGet(connection, member.SelectedItem, out var selected) ||
            selected.Prefix.Value != 0 || !FishingRules1458.TryGetPole(selected.ItemType.Value, out var pole) ||
            packet.ProjectileType != pole.ProjectileType || packet.Damage != 0 || packet.OriginalDamage != 0 ||
            packet.KnockBack != 0 || packet.BannerIdToRespondTo != 0 ||
            packet.Ai0 != 0 || packet.Ai1 != 0 || packet.Ai2 != 0 ||
            packet.Key.Spawner != connection.Player.Slot.Value || (member.ControlFlags & 32) == 0 ||
            member.ItemPhase?.ToolUse is not { LocalClocksKnown: true })
            return ToolReportObservationResult1458.Rejected;
        // One bounded report, one exact occupation. It expires at the next owned
        // ItemCheck; neither reports nor client animation initialize source clocks.
        var retained = fishingLaunchReports[member.Slot.Value];
        if (retained is not null && retained.Connection == connection && retained.State != packet &&
            retained.Tick == currentCombatTick) return ToolReportObservationResult1458.Rejected;
        fishingLaunchReports[member.Slot.Value] = new(connection, packet, currentCombatTick);
        toolProjectiles?.TryCommitFishingBirth(connection);
        return ToolReportObservationResult1458.Retained;
    }

    private bool TryPlanFishingItemCheck(RemoteItemPhasePlan1458 plan,
        in RuntimePlayerInventoryItem selected, in PlayerSelectedConsumableFacts1458 facts,
        in PlayerSelectedConsumableState1458 remoteClocks, ref RuntimePlayerItemPhase1458 phase,
        VanillaUnifiedRandom1458 random, out PlayerSelectedConsumableTransition1458 use)
    {
        use = default;
        if (toolProjectiles is null || selected.Prefix.Value != 0 ||
            !FishingRules1458.TryGetPole(selected.ItemType.Value, out var pole) ||
            phase.ToolUse is not { LocalClocksKnown: true } shadow || playerUpdateWorld is not { } world)
            return false;
        var local = shadow.Clocks;
        bool changed = shadow.SelectionInitialized && (shadow.SourceSlot != plan.Before.SelectedItem ||
            shadow.SourceItem != selected.ItemType || shadow.SourcePrefix != selected.Prefix);
        bool deferred = changed && (local.Selected.Animation != 0 || local.Selected.ItemTime != 0 || local.ReuseDelay != 0);
        var definition = new LocalItemClockDefinition1458(8, 8, 1, false, false, pole.ProjectileType);
        if (deferred)
        {
            if (shadow.SourceLocalDefinition is not { } old) return false;
            definition = old;
        }
        else if (changed)
            local = local with { Selected = local.Selected with { ItemTime = 0, ItemTimeMax = 0,
                ReleaseUseItem = facts.ControlUseItem || local.Selected.ReleaseUseItem }, Channel = false };
        var bobber = toolProjectiles.CaptureFishingBobber(plan.Member.Connection);
        var common = new ToolFrameFacts1458(true, facts.ControlUseItem, shadow.LastUseSuccess,
            facts.Cursed, facts.CrowdControlled, facts.SelectionBuffered || deferred, false, 1)
            { SourceUseAllowed = bobber is null, WindowsArithmetic = world.WindowsItemPrefixArithmetic };
        int crit = random.Next(3);
        if (!VanillaLocalItemUse1458.TryAdvanceFrame(definition, local, common, crit, out var frame) ||
            !VanillaLocalItemUse1458.TryAdvanceFrame(new(8, 8, 1, false, false, pole.ProjectileType),
                new(remoteClocks, phase.ToolTime ?? 0, false, false), common with { LocalOwner = false }, crit, out var remote))
            return false;
        if (frame.BeganActualUse && shadow.UseEpoch == ulong.MaxValue) return false;
        ulong epoch = frame.BeganActualUse ? shadow.UseEpoch + 1 : shadow.UseEpoch;
        var pose = new RuntimePlayerToolStagePose1458(plan.Before with { PositionX = plan.X,
            PositionY = plan.Y, VelocityX = plan.Vx, VelocityY = plan.Vy }, 20, 42,
            plan.Physics?.GravityDirection ?? 1);
        FishingItemAction1458? action = null;
        if (!deferred && frame.AttemptedUse && !facts.Cursed && !facts.CrowdControlled && bobber is { Returning: false })
        {
            // Source CanUseItem pulls existing bobbers even though it refuses a
            // second cast. Payment/line-break draws belong inside UpdatePlayers.
            plan.FishingBefore = bobber;
            bool caught = bobber.Result.HasBite && bobber.BiteClock < 0;
            bool consume = false, line = false;
            if (caught)
            {
                Span<FishingInventorySlot1458> image = stackalloc FishingInventorySlot1458[59];
                for (int i = 0; i < image.Length; i++) image[i] = new(plan.InventoryBefore[i].ItemType.Value, plan.InventoryBefore[i].Stack);
                int slot = FishingRules1458.SelectBait(image, bobber.Result.BaitItemType);
                caught = slot >= 0 && inventory.CanSpendFishingStock(plan.Member.Connection, slot, bobber.Result.BaitItemType);
                if (caught)
                {
                    var bait = plan.InventoryBefore[slot];
                    consume = FishingRules1458.ShouldConsumeBait(bait.ItemType.Value, bobber.Result.ItemType,
                        bobber.Result.NpcType, bobber.Result.ItemType is >= 2337 and <= 2339, false, new FishingRandom1458(random));
                    if (consume)
                        plan.InventoryMutations.Add(new(plan.Member.Connection, checked((short)slot), bait,
                            bait.Stack == 1 ? default : bait with { Stack = (short)(bait.Stack - 1) })
                            { ConsumeFundedQuantity = true, FishingBait = true });
                    if (bobber.Result.NpcType == 0) line = FishingRules1458.LineBreaks(false, new FishingRandom1458(random));
                }
            }
            action = new(epoch, pole, pose, bobber, caught, consume, line);
        }
        var clocks = frame.State;
        if (!deferred && frame.ShootBoundary)
        {
            if (!VanillaLocalItemUse1458.TryApplyShootTime(definition, clocks, 1, world.WindowsItemPrefixArithmetic, out clocks)) return false;
            action = new(epoch, pole, pose);
        }
        if (!VanillaLocalItemUse1458.TryEnterOwnerStage(definition, clocks, frame.LocalToolStage, out clocks)) return false;
        phase = phase with { ToolUse = shadow with { Clocks = clocks, LastUseSuccess = frame.LastUseSuccess,
            SelectionInitialized = true, SourceItem = deferred ? shadow.SourceItem : selected.ItemType,
            SourcePrefix = deferred ? shadow.SourcePrefix : selected.Prefix,
            SourceSlot = deferred ? shadow.SourceSlot : plan.Before.SelectedItem, SourceLocalDefinition = definition,
            SourceDefinition = null, SourcePlacement = null, PlacementReady = null, WeaponShoot = null,
            UseEpoch = epoch, LastFrameUpdate = currentCombatTick, FishingAction = action,
            SourceStagePose = pose, ShootEligible = false, ToolStagePending = false,
            DeferredSelectionSlot = deferred ? plan.Before.SelectedItem : null, RequiresFreshUse = deferred },
            ToolTime = remote.State.ToolTime, Channel = false };
        use = new(remote.State.Selected, facts.Life, facts.Mana, false, 0, 0)
            { PendingItemReuse = remote.PendingItemReuse };
        return true;
    }

    internal bool TryGetFishingLaunch(ConnectionHandle connection, out RuntimePlayerMember member,
        out FishingItemAction1458 action, out TerrariaProjectileUpdateState report)
    {
        action = null!; report = default;
        if (!membership.TryGet(connection, out member!) || member.IsDead ||
            member.ItemPhase?.ToolUse is not { LocalClocksKnown: true, FishingAction: { Pull: null } offered } ||
            fishingLaunchReports[member.Slot.Value] is not { } packet || packet.Connection != connection ||
            packet.Tick < currentCombatTick - 1 || packet.Tick > currentCombatTick ||
            !inventory.TryGet(connection, member.SelectedItem, out var held) || held.ItemType.Value != offered.Pole.ItemType ||
            member.SelectedItem != offered.Pose.Player.SelectedItem) return false;
        action = offered; report = packet.State; return true;
    }

    internal void AdoptFishingLaunch(RuntimePlayerMember member, FishingItemAction1458 action)
    {
        if (member.ItemPhase is not { ToolUse: { } tools } phase || !ReferenceEquals(tools.FishingAction, action))
            throw new InvalidOperationException("Fishing launch lost its owned ItemCheck.");
        member.ItemPhase = phase with { ToolUse = tools with { FishingAction = null } };
        fishingLaunchReports[member.Slot.Value] = null;
        member.MarkRemotePhaseSnapshot();
    }

    private void DrainFishingActionsAfterOwnedPhase()
    {
        foreach (var member in membership.Members.ToArray())
        {
            if (member.ItemPhase?.ToolUse?.FishingAction is not { } action) continue;
            if (action.Pull is not null) toolProjectiles?.CommitFishingPull(member.Connection, action);
            else toolProjectiles?.TryCommitFishingBirth(member.Connection);
        }
    }
}
