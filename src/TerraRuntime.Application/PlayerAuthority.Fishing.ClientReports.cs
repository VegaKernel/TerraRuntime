using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private bool TryBeginFishingClientReport(PlayerEquipmentRuntimeCommand command, RuntimePlayerMember member)
    {
        if (!TryCaptureFishingClientClaim(command.Connection, out var claim) || claim is null ||
            !RuntimePlayerInventoryItem.TryFromNormalized(command.Request, out var reported)) return false;
        _ = TryMatchFishingClientBaitDebit(claim, command.Request.SlotId, in reported);
        bool baitDebit = command.Request.SlotId == claim.BaitSlot && !claim.ConsumedBait &&
            claim.BaitBefore.ItemType.Value == claim.BaitType && reported ==
                (claim.BaitBefore.Stack == 1 ? default : claim.BaitBefore with { Stack = (short)(claim.BaitBefore.Stack - 1) });
        if (!baitDebit && !(claim.PullReported && claim.ClientClosed && claim.ItemType > 0)) return false;
        if (!TryEnsureFishingReportJournal(command.Connection, member, out var journal) || journal is null ||
            journal.AnglerClient is not null) return false;
        journal.ClientFishing = claim;
        return true;
    }

    private bool TrySealFishingClientReports(ConnectionHandle connection)
    {
        if (!membership.TryGet(connection, out var member) ||
            member.CursorMove is not { Shop: { ClientFishing: { } claim } journal } receipt) return false;
        journal.FishingBoundarySeen = true;
        if (!claim.PullReported) return true; // Packet-first input waits for owned ItemCheck, not a fabricated clock.
        return CompleteFishingClientPublication(claim, () =>
        {
            bool accepted = false;
            bool Current() => ReferenceEquals(member.CursorMove?.Shop, journal) && IsFishingClientClaimCurrent(claim) &&
                journal.Capture.IsCurrent && IsCurrentShopActor(member, receipt) && !journal.Invalid && !journal.IsRetired;
            if (Current())
            {
                for (short slot = 0; slot < 58; slot++)
                    if (journal.Seen[slot] && TryMatchFishingClientBaitDebit(claim, slot, in journal.Reports[slot])) break;
                bool needsBait = !claim.ConsumedBait &&
                    journal.Capture.Before[claim.BaitSlot].ItemType.Value == claim.BaitType &&
                    journal.Reports[claim.BaitSlot].Stack < journal.Capture.Before[claim.BaitSlot].Stack;
                short debit = needsBait ? claim.BaitSlot : (short)-1;
                bool allowed = claim.PaymentCommitted && !needsBait ||
                    (claim.Consumption & (needsBait ? FishingBaitConsumption1458.Consumed : FishingBaitConsumption1458.Conserved)) != 0;
                allowed &= claim.PaymentCommitted || inventory.CanSpendFishingStock(connection, claim.BaitSlot, claim.BaitType, journal.Capture);
                var endingImage = journal.Capture.Before.AsSpan(0, 59).ToArray();
                if (needsBait)
                    endingImage[claim.BaitSlot] = endingImage[claim.BaitSlot].Stack == 1 ? default :
                        endingImage[claim.BaitSlot] with { Stack = (short)(endingImage[claim.BaitSlot].Stack - 1) };
                bool stackBounds = TryGetFishingClientStackBounds(claim, endingImage, out int minimum, out int maximum);
                if (allowed && claim.ClientClosed && claim.ItemType > 0 &&
                    TryExtractFishingReportRewards(journal, debit, claim.BaitType, out var candidate) && candidate is not null &&
                    candidate.Unordered is [{ } reward] && reward.Type == claim.ItemType &&
                    stackBounds && reward.Stack >= minimum && reward.Stack <= maximum)
                {
                    accepted = TryAdoptFishingReportWitness(connection, member, receipt, journal, debit, claim.BaitType, false,
                        candidate.Unordered, candidate, naturalPrefix: false, Current, () =>
                        {
                            if (!claim.PaymentCommitted) AdoptFishingClientPayment(claim, needsBait);
                            else if (needsBait && !claim.ConsumedBait) AdoptFishingClientConsumedBait(claim);
                            AdoptFishingClientReward(claim);
                        });
                }
                else if (allowed && needsBait && (claim.Consumption & FishingBaitConsumption1458.Consumed) != 0)
                {
                    accepted = TryFishingRewards(connection, claim.BaitSlot, claim.BaitType, false,
                        rewards => rewards.MatchesReports(journal.Reports, journal.Seen), Current, () =>
                        {
                            DropBankBatchAlternative(member, journal);
                            if (!claim.PaymentCommitted) AdoptFishingClientPayment(claim, true);
                            else AdoptFishingClientConsumedBait(claim);
                        }, journal.Capture, [], clientOwnedRolls: true);
                }
            }
            if (!accepted) RefuseFishingCrateReports(connection, member, receipt, journal);
            return true;
        });
    }

    internal void TryCompleteFishingClientReports(ConnectionHandle connection)
    {
        if (!TryCaptureFishingClientClaim(connection, out var claim) || claim is null || !claim.PullReported) return;
        _ = CompleteFishingClientPublication(claim, () =>
        {
            if (membership.TryGet(connection, out var member) &&
                member.CursorMove is { Shop: { ClientFishing: not null, FishingBoundarySeen: true } })
            {
                TrySealFishingClientReports(connection);
                return true;
            }
            if (!claim.PaymentCommitted && (claim.ClientClosed || claim.NpcReported) &&
                (claim.Consumption & FishingBaitConsumption1458.Conserved) != 0 &&
                inventory.CanSpendFishingStock(connection, claim.BaitSlot, claim.BaitType) && IsFishingClientClaimCurrent(claim))
                AdoptFishingClientPayment(claim, false);
            if (claim.PaymentCommitted && claim.ClientClosed && claim.ItemType > 0 &&
                membership.TryGet(connection, out member) &&
                member.CursorMove is { Shop: { FishingOverflow: { Offers.Count: > 0 } } journal } &&
                !journal.Seen.Contains(true))
            {
                // A wholly overflowing conserved catch sends21 then29 and no5/138.
                // Source29 is its final boundary; every offered item still needs the
                // one paid capability and the exact source storage/overflow replay.
                journal.ClientFishing = claim;
                TrySealFishingClientReports(connection);
            }
            return true;
        });
    }

    private bool CompleteFishingClientPublication(FishingClientClaim1458 claim, Func<bool> operation)
    {
        bool result = false;
        Exception? failure = null;
        try { result = operation(); }
        catch (Exception exception) { failure = exception; }
        // Joint adoption may already have removed the actor and inventory journal.
        // Its attached terminal publication must drain even when an earlier item,
        // world-item or NPC observer throws; a later capture cannot recover it.
        try { PublishFishingClientEffects(claim); }
        catch (Exception exception) { failure ??= exception; }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return result;
    }

}
