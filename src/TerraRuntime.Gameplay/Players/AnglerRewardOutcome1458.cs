namespace TerraRuntime.Gameplay.Players;

public sealed record AnglerOutcomeContext1458(int QuestFish, bool HardMode, bool ExpertMode,
    float PriceAdjustment, IReadOnlyList<int> OwnedItems);

public readonly record struct AnglerQuestCountRange1458(int Minimum, int Maximum);

/// <summary>Immutable possible completed-character counts, before the next turn-in.</summary>
public sealed class AnglerQuestCountEvidence1458
{
    private readonly AnglerQuestCountRange1458[] ranges;
    public IReadOnlyList<AnglerQuestCountRange1458> Ranges { get; }
    public static AnglerQuestCountEvidence1458 Unknown { get; } =
        new([new(0, VanillaAnglerReward1458.MaximumSupportedOutcomeCount)]);

    internal AnglerQuestCountEvidence1458(AnglerQuestCountRange1458[] ranges)
    {
        this.ranges = ranges;
        Ranges = Array.AsReadOnly(ranges);
    }

    public static AnglerQuestCountEvidence1458 FromKnown(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return new([new(count, count)]);
    }

    public bool TryGetKnown(out int count)
    {
        if (ranges.Length == 1 && ranges[0].Minimum == ranges[0].Maximum)
        { count = ranges[0].Minimum; return true; }
        count = 0;
        return false;
    }

    public bool Allows(int count)
    {
        foreach (var range in ranges)
            if (count >= range.Minimum && count <= range.Maximum) return true;
        return false;
    }
}

public static partial class VanillaAnglerReward1458
{
    // Admission bounds, not vanilla character limits. Keeping even the largest pre-float
    // money product within Int32 excludes pathological float-to-int overflow outcomes.
    public const int MaximumSupportedOutcomeCount = (int.MaxValue / 200) * 2 - 50;
    public const int MaximumOutcomeOffers = 64;
    public const int MaximumOutcomeWork = 524_288;
    private const int MaximumOwnedItems = 512;
    private const int StableMoneyCount = 1730;
    private const int StableOtherCount = 249;

    public static bool ValidateOutcome(AnglerOutcomeContext1458 context, AnglerQuestCountEvidence1458 evidence,
        IReadOnlyList<AnglerReward1458> unordered, out AnglerReward1458[] orderedWitness,
        out AnglerQuestCountEvidence1458 nextEvidence) =>
        ValidateOutcome(context, evidence, unordered, out orderedWitness, out nextEvidence, out _);

    /// <summary>
    /// Finds legal source choices, not a seed or the server's random cursor. Every surviving character
    /// count advances together; an unknown count is never silently replaced with a selected witness.
    /// </summary>
    public static bool ValidateOutcome(AnglerOutcomeContext1458 context, AnglerQuestCountEvidence1458 evidence,
        IReadOnlyList<AnglerReward1458> unordered, out AnglerReward1458[] orderedWitness,
        out AnglerQuestCountEvidence1458 nextEvidence, out int workPerformed)
    {
        orderedWitness = [];
        nextEvidence = evidence;
        workPerformed = 0;
        if (context is null || evidence is null || unordered is null || context.OwnedItems is null ||
            context.OwnedItems.Count > MaximumOwnedItems || unordered.Count is <= 0 or > MaximumOutcomeOffers ||
            !float.IsFinite(context.PriceAdjustment) || context.PriceAdjustment is < .5f or > 2f)
            return false;
        bool questFish = false;
        for (int quest = 0; quest < VanillaAnglerQuest1458.QuestCount; quest++)
            questFish |= VanillaAnglerQuest1458.ItemForQuest(quest) == context.QuestFish;
        if (!questFish || !TryReadOutcome(unordered, out var items, out long copper)) return false;

        var budget = new OutcomeBudget();
        var narrowed = new List<AnglerQuestCountRange1458>();
        var stages = new Dictionary<int, OutcomeStages>();
        AnglerReward1458[]? witness = null;
        try
        {
            bool Accept(int completed)
            {
                budget.Spend();
                var actual = new AnglerRewardContext1458(completed, context.QuestFish, context.HardMode,
                    context.ExpertMode, context.PriceAdjustment, context.OwnedItems);
                if (!TryMoney(actual, copper, budget, out var money)) return false;
                int key = Math.Min(completed, StableOtherCount);
                if (!stages.TryGetValue(key, out var stage))
                {
                    var category = actual with { QuestsDone = key };
                    float rarity = RewardRarity(category);
                    stage = new(
                        Possible(choice => MainOffers(choice, category, rarity), budget),
                        Possible(choice => Optional(Decoration(choice, category, rarity)), budget),
                        Possible(choice => Optional(Bait(choice, category, rarity)), budget));
                    stages.Add(key, stage);
                }
                if (!TryMatch(stage, items, money, budget, out var current)) return false;
                witness ??= current;
                return true;
            }

            foreach (var range in evidence.Ranges)
            {
                // The finite prefix is inspected exactly. Beyond1730, all money offers
                // clamp to ten gold; the other stages already stabilize at249.
                int prefixEnd = Math.Min(range.Maximum, StableMoneyCount - 2);
                for (int before = range.Minimum; before <= prefixEnd; before++)
                    if (Accept(before + 1)) AddRange(narrowed, before + 1, before + 1);
                int tailStart = Math.Max(range.Minimum, StableMoneyCount - 1);
                int tailEnd = Math.Min(range.Maximum, MaximumSupportedOutcomeCount - 1);
                if (tailStart <= tailEnd && Accept(tailStart + 1))
                    AddRange(narrowed, tailStart + 1, tailEnd + 1);
            }
            if (witness is null || narrowed.Count == 0) return false;
            orderedWitness = witness;
            nextEvidence = new(narrowed.ToArray());
            return true;
        }
        catch (OutcomeLimitException) { return false; }
        finally { workPerformed = budget.Work; }
    }

    private static bool TryReadOutcome(IReadOnlyList<AnglerReward1458> offers,
        out Dictionary<int, int> items, out long copper)
    {
        items = [];
        copper = 0;
        foreach (var offer in offers)
        {
            if (offer.Type <= 0 || offer.Stack <= 0) return false;
            long coinValue = offer.Type switch { 71 => 1, 72 => 100, 73 => 10_000, 74 => 1_000_000, _ => 0 };
            if (coinValue != 0)
            {
                copper += coinValue * offer.Stack;
                if (copper > 100_000) return false;
            }
            else
            {
                int old = items.GetValueOrDefault(offer.Type);
                // One source turn-in has at most three costume pieces, one decoration
                // and one bait offer; every non-coin stack is at most seven.
                if (offer.Stack > 7 - old) return false;
                items[offer.Type] = old + offer.Stack;
                if (items.Count > 5) return false;
            }
        }
        return copper != 0 && items.Count != 0;
    }

    private static bool TryMoney(AnglerRewardContext1458 context, long copper, OutcomeBudget budget,
        out AnglerReward1458 offer)
    {
        var choices = new MoneyChoice(budget);
        int low = 50, high = 200;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            choices.Sample = middle;
            offer = Money(choices, context);
            long value = offer.Stack * (offer.Type == 73 ? 10_000L : 100L);
            if (value == copper) return true;
            if (value < copper) low = middle + 1;
            else high = middle - 1;
        }
        offer = default;
        return false;
    }

    private static bool TryMatch(OutcomeStages stages, Dictionary<int, int> items, AnglerReward1458 money,
        OutcomeBudget budget, out AnglerReward1458[] witness)
    {
        foreach (var main in stages.Main)
        {
            budget.Spend();
            if (!Contains(items, main) || items.Count - main.Length > 2) continue;
            foreach (var decoration in stages.Decoration)
            {
                budget.Spend();
                if (!Contains(items, decoration)) continue;
                foreach (var bait in stages.Bait)
                {
                    budget.Spend();
                    if (items.Count != main.Length + decoration.Length + bait.Length || !Contains(items, bait)) continue;
                    witness = new AnglerReward1458[main.Length + decoration.Length + 1 + bait.Length];
                    main.CopyTo(witness, 0);
                    decoration.CopyTo(witness, main.Length);
                    witness[main.Length + decoration.Length] = money;
                    bait.CopyTo(witness, main.Length + decoration.Length + 1);
                    return true;
                }
            }
        }
        witness = [];
        return false;
    }

    private static bool Contains(Dictionary<int, int> items, AnglerReward1458[] offers)
    {
        foreach (var offer in offers)
            if (!items.TryGetValue(offer.Type, out int stack) || stack != offer.Stack) return false;
        return true;
    }

    private static AnglerReward1458[] Optional(AnglerReward1458? offer) => offer is { } value ? [value] : [];

    private static AnglerReward1458[][] Possible(Func<RewardChoices, AnglerReward1458[]> roll, OutcomeBudget budget)
    {
        var choices = new PossibleChoices(budget);
        var outcomes = new Dictionary<string, AnglerReward1458[]>(StringComparer.Ordinal);
        do
        {
            budget.Spend();
            choices.BeginPath();
            var outcome = roll(choices);
            string key = string.Join(';', outcome.Select(static offer => $"{offer.Type}:{offer.Stack}"));
            outcomes.TryAdd(key, outcome);
            if (outcomes.Count > 64) throw new OutcomeLimitException();
        }
        while (choices.AdvancePath());
        return outcomes.Values.ToArray();
    }

    private static void AddRange(List<AnglerQuestCountRange1458> ranges, int minimum, int maximum)
    {
        if (ranges.Count != 0 && ranges[^1].Maximum >= minimum - 1)
            ranges[^1] = ranges[^1] with { Maximum = Math.Max(ranges[^1].Maximum, maximum) };
        else ranges.Add(new(minimum, maximum));
    }

    private sealed record OutcomeStages(AnglerReward1458[][] Main, AnglerReward1458[][] Decoration, AnglerReward1458[][] Bait);
    private sealed class OutcomeLimitException : Exception { }
    private sealed class OutcomeBudget
    {
        internal int Work { get; private set; }
        internal void Spend() { if (Work == MaximumOutcomeWork) throw new OutcomeLimitException(); Work++; }
    }

    private sealed class PossibleChoices(OutcomeBudget budget) : RewardChoices
    {
        private readonly List<(int Value, int Count)> path = [];
        private int cursor;
        internal void BeginPath() => cursor = 0;
        internal bool AdvancePath()
        {
            if (cursor < path.Count) path.RemoveRange(cursor, path.Count - cursor);
            while (path.Count != 0)
            {
                var last = path[^1];
                if (last.Value + 1 < last.Count)
                { path[^1] = (last.Value + 1, last.Count); return true; }
                path.RemoveAt(path.Count - 1);
            }
            return false;
        }
        private int Select(int count)
        {
            budget.Spend();
            if (count <= 1) return 0;
            if (count > 32 || cursor >= 32) throw new OutcomeLimitException();
            if (cursor == path.Count) path.Add((0, count));
            var decision = path[cursor++];
            if (decision.Count != count) throw new OutcomeLimitException();
            return decision.Value;
        }
        internal override bool OneIn(int maximum) => AtMost(maximum, 0);
        internal override bool AtMost(int maximum, int inclusive)
        {
            if (inclusive < 0) return false;
            return Math.Max(0, maximum - 1) <= inclusive || Select(2) != 0;
        }
        internal override bool DoubleBelow(float threshold) =>
            threshold > 0 && (threshold > 2147483646d / 2147483647d || Select(2) != 0);
        internal override bool SingleAtMost(float threshold) =>
            threshold >= 0 && (threshold >= 1 || Select(2) != 0);
        internal override int Pick(int count) => Select(count);
        internal override int Range(int minimum, int maximum) => minimum + Select(maximum - minimum);
        internal override int CountAtMost(ReadOnlySpan<int> maxima, int inclusive)
        {
            int forced = 0, possible = 0;
            foreach (int maximum in maxima)
            {
                if (inclusive >= 0) possible++;
                if (Math.Max(0, maximum - 1) <= inclusive) forced++;
            }
            return forced + Select(possible - forced + 1);
        }
    }

    private sealed class MoneyChoice(OutcomeBudget budget) : RewardChoices
    {
        internal int Sample { get; set; }
        internal override int Range(int minimum, int maximum)
        { budget.Spend(); if (minimum != 50 || maximum != 201) throw new OutcomeLimitException(); return Sample; }
        internal override bool OneIn(int maximum) => throw new OutcomeLimitException();
        internal override bool AtMost(int maximum, int inclusive) => throw new OutcomeLimitException();
        internal override bool DoubleBelow(float threshold) => throw new OutcomeLimitException();
        internal override bool SingleAtMost(float threshold) => throw new OutcomeLimitException();
        internal override int Pick(int count) => throw new OutcomeLimitException();
        internal override int CountAtMost(ReadOnlySpan<int> maxima, int inclusive) => throw new OutcomeLimitException();
    }
}
