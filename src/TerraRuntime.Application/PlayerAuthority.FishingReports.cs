using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    // An observation in the existing inventory journal is never a reward budget.
    // A source grammar must prove the entire operation before these deltas can
    // be replayed through the ordinary inventory/world-item transaction.
    private sealed record FishingReportRewards(FishingReward1458[] Unordered, Dictionary<int, int> Prefixes);

    private static bool TryExtractFishingReportRewards(PendingShopJournal journal, short debitSlot, int debitType,
        out FishingReportRewards? result)
    {
        result = null;
        if (journal.Invalid || journal.IsSealed || journal.IsRetired || !journal.Capture.IsCurrent ||
            journal.Seen.AsSpan(59).Contains(true)) return false;
        var delta = new Dictionary<(int Type, int Prefix), int>();
        long coins = 0;
        bool Add(int type, int stack, int prefix, int sign)
        {
            if (type == 0 || stack == 0) return true;
            if (type is >= 71 and <= 74)
            {
                if (prefix != 0) return false;
                long value = type switch { 71 => 1, 72 => 100, 73 => 10_000, _ => 1_000_000 };
                coins += sign * value * stack;
            }
            else
            {
                var key = (type, prefix);
                delta[key] = delta.GetValueOrDefault(key) + sign * stack;
            }
            return true;
        }
        for (int slot = 0; slot < 59; slot++)
        {
            var before = journal.Capture.Before[slot];
            var after = journal.Reports[slot];
            if (!before.IsCanonical || !after.IsCanonical) return false;
            int stack = before.Stack;
            if (slot == debitSlot)
            {
                if (before.IsEmpty || before.ItemType.Value != debitType) return false;
                stack--;
            }
            if (!Add(before.ItemType.Value, stack, before.Prefix.Value, -1) ||
                !Add(after.ItemType.Value, after.Stack, after.Prefix.Value, 1)) return false;
        }
        if (journal.FishingOverflow is { } overflow)
            foreach (var offer in overflow.Offers)
                if (!Add(offer.ItemNetId, offer.Stack, offer.Prefix, 1)) return false;
        if (coins < 0 || delta.Values.Any(value => value < 0)) return false;
        var grants = new List<FishingReward1458>();
        var prefixes = new Dictionary<int, int>();
        foreach (var pair in delta)
        {
            if (pair.Value == 0) continue;
            // A single opening cannot turn one source grant into differently
            // prefixed items. Angler and bobber grants have prefix zero.
            if (pair.Value > short.MaxValue || !prefixes.TryAdd(pair.Key.Type, pair.Key.Prefix)) return false;
            grants.Add(new(pair.Key.Type, pair.Value));
        }
        if (coins > 0)
        {
            int type = coins % 10_000 == 0 ? 73 : 72;
            long stack = coins / (type == 73 ? 10_000 : 100);
            if (coins % 100 != 0 || stack is <= 0 or > short.MaxValue) return false;
            grants.Add(new(type, (int)stack)); prefixes.Add(type, 0);
        }
        if (grants.Count is 0 or > 16) return false;
        result = new(grants.ToArray(), prefixes);
        return true;
    }

    private bool TryAdoptFishingReportWitness(ConnectionHandle connection, RuntimePlayerMember member,
        PendingCursorMove receipt, PendingShopJournal journal, short debitSlot, int debitType, bool debitAfterRewards,
        IReadOnlyList<FishingReward1458> ordered, FishingReportRewards candidate, bool naturalPrefix,
        Func<bool>? additionalGuard = null, Action? adopt = null)
    {
        if (PickupWorld is not { } world || PickupWindowsArithmetic is not { } windows) return false;
        foreach (var reward in ordered)
        {
            if (!candidate.Prefixes.TryGetValue(reward.Type, out int prefix) ||
                (naturalPrefix ? !VanillaItemPrefixNormalization1458.CanProduceNaturally(new(reward.Type), new(prefix), windows, world)
                    : prefix != 0)) return false;
        }
        bool Current() => ReferenceEquals(member.CursorMove?.Shop, journal) && journal.Capture.IsCurrent &&
            IsCurrentShopActor(member, receipt) && !journal.Invalid && !journal.IsSealed && !journal.IsRetired &&
            additionalGuard?.Invoke() != false;
        return TryFishingRewards(connection, debitSlot, debitType, debitAfterRewards, rewards =>
        {
            foreach (var reward in ordered)
                if (!rewards.Grant(reward.Type, reward.Stack, candidate.Prefixes[reward.Type], canGoIntoVoidVault: naturalPrefix))
                    return false;
            return rewards.MatchesReports(journal.Reports, journal.Seen);
        }, Current, () => { DropBankBatchAlternative(member, journal); adopt?.Invoke(); }, journal.Capture,
            journal.FishingOverflow?.Offers ?? [], clientOwnedRolls: true);
    }
}
